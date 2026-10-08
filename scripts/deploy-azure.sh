#!/bin/sh
# Builds the whole app as one image (the web app and the compiler together, which is what
# Blazemoji/Dockerfile builds when no target is named) and pushes it to the Azure Container
# Registry that the site's App Service takes its image from.
#
#   scripts/deploy-azure.sh --registry <name> [--image blazemoji] [--tag latest]
#                           [--point <web app> <resource group>]
#                           [--restart <web app> <resource group>]
#                           [--in-azure] [--with-grapevine] [--build-only] [--yes]
#
# An App Service runs the image its settings name, tag and all. One that names a tag which
# moves, such as "latest", takes a new image when it restarts (--restart). One that names a
# fixed tag, as a publish from Visual Studio leaves it, goes on running that image whatever
# is pushed: --point sets it to the image this script has just pushed under its commit, and
# says what it was running before and how to go back.
#
# --in-azure has the registry build the image itself (docker/registry-build.yaml), from the
# source as GitHub has it at this commit. Nothing is uploaded from this machine but a few
# lines, where a push sends every layer: the one that holds the C++ compiler is about
# 250 MB, and a connection that drops during an upload fails the whole thing ("use of
# closed network connection"). It needs no Docker here. The commit has to be pushed to
# GitHub first, and it cannot be combined with --with-grapevine. With --build-only the
# registry builds and pushes nothing.
#
# It builds what is committed at HEAD, not what is in the working folder, so the image is
# the same whoever builds it. That also leaves Grapevine out: its package is built into
# folders git ignores, and the site is public while Grapevine is not. --with-grapevine
# builds from the working folder instead, with whatever is in it.
#
# Needs Docker running, unless --in-azure, and for anything but --build-only the Azure CLI
# signed in to the account that owns the registry (az login).
set -eu
cd "$(dirname "$0")/.."

registry=""
image="blazemoji"
tag="latest"
web_app=""
resource_group=""
point_app=""
point_group=""
with_grapevine=0
build_only=0
in_azure=0
asked=1

while [ $# -gt 0 ]; do
  case "$1" in
    --registry) registry="$2"; shift 2 ;;
    --image) image="$2"; shift 2 ;;
    --tag) tag="$2"; shift 2 ;;
    --restart) web_app="$2"; resource_group="$3"; shift 3 ;;
    --point) point_app="$2"; point_group="$3"; shift 3 ;;
    --with-grapevine) with_grapevine=1; shift ;;
    --build-only) build_only=1; shift ;;
    --in-azure) in_azure=1; shift ;;
    --yes) asked=0; shift ;;
    *) echo "Unknown argument: $1" >&2; grep -A3 '^#   scripts/deploy-azure.sh' "$0" >&2; exit 2 ;;
  esac
done

if [ -z "$registry" ] && { [ "$build_only" -eq 0 ] || [ "$in_azure" -eq 1 ]; }; then
  echo "Say which registry with --registry <name>, or build here without pushing with --build-only." >&2
  exit 2
fi

if [ "$with_grapevine" -eq 1 ] && [ "$in_azure" -eq 1 ]; then
  echo "--in-azure builds what is committed, and Grapevine is not. Leave out one of the two." >&2
  exit 2
fi

commit="$(git rev-parse --short HEAD)"
name="${registry:+$registry.azurecr.io/}$image"

# Whatever is made along the way and is not wanted afterwards. None of the paths has a space.
made=""
trap 'rm -rf $made' EXIT

# Says what is about to change and as whom, and stops unless the answer is yes.
confirm() {
  [ "$asked" -eq 1 ] || return 0
  echo
  echo "About to $1 $name:$tag (commit $commit) as $(az account show --query user.name -o tsv)"
  echo "in the subscription \"$(az account show --query name -o tsv)\"."
  if [ -n "$point_app" ]; then
    echo "The App Service $point_app will then be set to run it: the public site changes."
  else
    echo "If the App Service follows that tag, the public site changes when this is done."
  fi
  printf "Go ahead? [y/N] "
  read -r answer
  case "$answer" in
    y | Y | yes) ;;
    *) echo "Nothing was pushed."; exit 1 ;;
  esac
}

point_if_asked() {
  [ -n "$point_app" ] || return 0
  was="$(az webapp config show --name "$point_app" --resource-group "$point_group" --query linuxFxVersion -o tsv)"
  az webapp config set --name "$point_app" --resource-group "$point_group" --linux-fx-version "DOCKER|$name:$commit" --output none
  echo "$point_app is now set to run $name:$commit. It was running ${was#DOCKER|}."
  echo "To go back: az webapp config set --name $point_app --resource-group $point_group --linux-fx-version \"$was\""
}

restart_if_asked() {
  [ -n "$web_app" ] || return 0
  az webapp restart --name "$web_app" --resource-group "$resource_group"
  echo "Restarted $web_app."
}

if [ "$in_azure" -eq 1 ]; then
  full="$(git rev-parse HEAD)"
  source="$(git remote get-url origin | sed -E 's#^git@github.com:#https://github.com/#; s#\.git$##').git"
  git fetch -q origin
  if [ -z "$(git branch -r --contains "$full")" ]; then
    echo "The registry fetches the source from GitHub, and commit $commit is not there yet. Push it first." >&2
    exit 2
  fi

  changed="$(git status --porcelain | wc -l | tr -d ' ')"
  [ "$changed" -eq 0 ] || echo "Note: $changed uncommitted change(s) in the working folder are not in this image."

  push="yes"
  if [ "$build_only" -eq 1 ]; then
    push="no"
    echo "Having the registry build commit $commit, and push nothing."
  else
    confirm "have the registry build and push"
  fi

  az acr run --registry "$registry" --platform linux/amd64 --file docker/registry-build.yaml \
    --set image="$image" --set tag="$tag" --set commit="$commit" --set push="$push" "$source#$full"

  if [ "$build_only" -eq 1 ]; then
    echo "Built in the registry. Nothing was pushed."
    exit 0
  fi

  point_if_asked
  restart_if_asked
  echo "Built in the registry and pushed as $name:$tag and $name:$commit."
  exit 0
fi

if [ "$with_grapevine" -eq 1 ]; then
  context="."
  echo "Building from the working folder, with whatever is in it."
else
  context="$(mktemp -d)"
  made="$made $context"
  git archive HEAD | tar -x -C "$context"
  changed="$(git status --porcelain | wc -l | tr -d ' ')"
  [ "$changed" -eq 0 ] || echo "Note: $changed uncommitted change(s) in the working folder are not in this image."
  echo "Building what is committed at $commit."
fi

docker build --platform linux/amd64 -f "$context/Blazemoji/Dockerfile" -t "$name:$tag" -t "$name:$commit" "$context"

if [ "$build_only" -eq 1 ]; then
  echo "Built $name:$tag and $name:$commit. Nothing was pushed."
  exit 0
fi

confirm "push"
az acr login --name "$registry"
docker push "$name:$commit"
docker push "$name:$tag"
point_if_asked
restart_if_asked

echo "Pushed $name:$tag and $name:$commit."
echo "To go back, point the App Service at the tag of an earlier commit, or push that image as $tag again."
