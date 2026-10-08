#!/bin/sh
# Builds the whole app as one image (the web app and the compiler together, which is what
# Blazemoji/Dockerfile builds when no target is named) and pushes it to the Azure Container
# Registry that the site's App Service takes its image from.
#
#   scripts/deploy-azure.sh --registry <name> [--image blazemoji] [--tag latest]
#                           [--restart <web app> <resource group>]
#                           [--in-azure] [--with-grapevine] [--build-only] [--yes]
#
# --in-azure has the registry build the image itself, from the clean copy of what is
# committed. Only the source is sent, a few tens of megabytes, where a push from this
# machine sends every layer: the one that holds the C++ compiler is about 250 MB, and a
# connection that drops during it fails the whole push with "use of closed network
# connection". It needs no Docker here, and it cannot be combined with --with-grapevine.
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
    --with-grapevine) with_grapevine=1; shift ;;
    --build-only) build_only=1; shift ;;
    --in-azure) in_azure=1; shift ;;
    --yes) asked=0; shift ;;
    *) echo "Unknown argument: $1" >&2; sed -n '6,8p' "$0" >&2; exit 2 ;;
  esac
done

if [ "$build_only" -eq 0 ] && [ -z "$registry" ]; then
  echo "Say which registry with --registry <name>, or build without pushing with --build-only." >&2
  exit 2
fi

if [ "$build_only" -eq 1 ] && [ "$in_azure" -eq 1 ]; then
  echo "--build-only builds here and pushes nothing; --in-azure builds in the registry. Choose one." >&2
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

# Says what is about to change and as whom, and stops unless the answer is yes.
confirm() {
  [ "$asked" -eq 1 ] || return 0
  echo
  echo "About to $1 $name:$tag (commit $commit) as $(az account show --query user.name -o tsv)"
  echo "in the subscription \"$(az account show --query name -o tsv)\"."
  echo "If the App Service follows that tag, the public site changes when this is done."
  printf "Go ahead? [y/N] "
  read -r answer
  case "$answer" in
    y | Y | yes) ;;
    *) echo "Nothing was pushed."; exit 1 ;;
  esac
}

restart_if_asked() {
  [ -n "$web_app" ] || return 0
  az webapp restart --name "$web_app" --resource-group "$resource_group"
  echo "Restarted $web_app."
}

if [ "$in_azure" -eq 1 ]; then
  # Two things about the registry's builder, both found by trying it.
  #
  # It stops at a FROM line that names a platform ("unable to understand line FROM
  # --platform=..."). The Dockerfile names one on each base image so that a plain build on
  # an Apple machine still comes out as x86-64. The registry builds on x86-64 and is told
  # the platform besides, so its copy of the Dockerfile goes without them. The copy has a
  # name that .dockerignore lets through, which "Dockerfile" is not.
  #
  # And left to itself it uses Docker's older builder, which builds every stage, the one
  # that runs the tests included, and does not hand a build argument on to the stages
  # after the one that declares it. BuildKit is what the Dockerfile is written for and
  # what Docker Desktop uses, so the build is asked for as a task that turns it on.
  sed 's|^FROM --platform=linux/amd64 |FROM |' "$context/Blazemoji/Dockerfile" > "$context/blazemoji.dockerfile"
  cat > "$context/deploy-task.yaml" <<TASK
version: v1.1.0
steps:
  - build: --platform linux/amd64 -f blazemoji.dockerfile -t \$Registry/$image:$tag -t \$Registry/$image:$commit .
    env: ["DOCKER_BUILDKIT=1"]
  - push:
      - \$Registry/$image:$tag
      - \$Registry/$image:$commit
TASK

  confirm "have the registry build"
  az acr run --registry "$registry" --platform linux/amd64 --file deploy-task.yaml "$context"
  restart_if_asked
  echo "Built in the registry as $name:$tag and $name:$commit."
  exit 0
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
restart_if_asked

echo "Pushed $name:$tag and $name:$commit."
echo "To go back, point the App Service at the tag of an earlier commit, or push that image as $tag again."
