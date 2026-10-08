# Blazemoji
<pre>                                                                                                    
   (    (                                                
 ( )\   )\      )          (       )            (    (   
 )((_) ((_)  ( /(   (     ))\     (       (     )\   )\  
((_)_   _    )(_))  )\   /((_)    )\  '   )\   ((_) ((_) 
 | _ ) | |  ((_)_  ((_) (_))    _((_))   ((_)    !   (_) 
 | _ \ | |  / _` | |_ / / -_)  | '  \() / _ \   | |  | | 
 |___/ |_|  \__,_| /__| \___|  |_|_|_|  \___/  _/ |  |_| 
                                              |__/      
</pre>

```emojicode
🏁 🍇
    😀 🔤A Blazor 🔥 powered emojicode editor🔤❗️
🍉
```

![image](https://github.com/thatplatypus/Blazemoji/assets/29233866/f63a3fc3-5e02-4753-b309-bc80a98d1963)

![image](https://github.com/thatplatypus/Blazemoji/assets/29233866/1ccffb14-344a-4810-a855-cab21c4ffbff)

## Overview
Blazemoji is a modern web application designed specifically for emojicode. It's about time we start building a great editor for a great language. The project is still in early stages so all feedback and feature requests are welcome. 

Features include:
- Compile and run emojicode right in the browser!
- Light / Dark theme built in
- Emojicode quick reference toolbox
- Code editor keybindings for common emojis like `shift`+`"` turns into `🔤`
- Pairs that close themselves: typing `🍇`, `🤜`, `🍿`, `🐚` or `🔤` brings its closer, and the editor shows which `🍉` belongs to which `🍇`
- Library of sample scripts with support for saving scripts of your own
- More coming soon
  - Researching syntax highlighting in monaco for ☁️ and 🔤

## Prerequisites
- .NET 10 SDK
- Docker, for anything that compiles or runs Emojicode

The Emojicode compiler bundled in this repo is the x86_64 Linux build of 1.0 beta 2, so it runs in a `linux/amd64` container. On Apple Silicon, Docker Desktop emulates it.

## How it fits together

| Project | What it is |
| --- | --- |
| `Blazemoji` | The web host: a Blazor Server app that shows the editor and keeps projects in the browser. It holds no compiler. |
| `Blazemoji.Desktop` | The desktop host: the same editor in a window of its own, with projects kept as folders on disk. |
| `Blazemoji.Components` | The editor as Razor components, with the state behind them: projects, editor, toolbox, output, problems and requests. |
| `Blazemoji.Core` | The keyword catalog, the code intelligence, the project model and keeping projects on disk. No UI. |
| `Blazemoji.Toolchain` | The toolchain contract (`IToolchain`) and `HttpToolchain`, its client. |
| `Blazemoji.Toolchain.Local` | Compiles and runs programs as local processes. Holds the compiler and stock packages. |
| `Blazemoji.Toolchain.Service` | A small HTTP service in front of the local toolchain. |

The web app reaches the toolchain service at `ToolchainClient:BaseUrl` (default `http://localhost:5290`). Any host that can make HTTP requests can use the same service; that setting is the only thing it needs. [docs/hosting.md](docs/hosting.md) lists what a host has to supply to show the editor, and what is particular to the desktop one.

## Run the app

```bash
docker compose up --build
```

Then open http://localhost:5080.

This starts two containers. `web` is published on this machine only. `toolchain` publishes no port and sits on an internal Docker network, so the programs it runs have no route to the internet or to your network.

The app compiles and runs whatever code it is given, with no sign-in, and every program shares the one toolchain container. Do not expose it beyond your own machine.

### From an IDE

The web app does not hold the compiler: it asks the toolchain service, which can only run on x86_64 Linux. That leaves two ways to run from an IDE, and both work on a Mac.

**Build and run the Dockerfile.** With no target named, `Blazemoji/Dockerfile` builds an image that holds the web app and the toolchain service together, so an IDE's "run in Docker" gives a working app in one container:

```bash
docker build -f Blazemoji/Dockerfile -t blazemoji .
docker run -p 5000:8080 blazemoji
```

**Run the web project itself,** to debug it. Start the toolchain service in a container, then run the project as usual (the `http` launch profile, or `dotnet run --project Blazemoji`). The app looks for the service at `http://localhost:5290`, which is where the script puts it:

```bash
scripts/dev-toolchain.sh        # start; "scripts/dev-toolchain.sh stop" stops it
```

Either way the programs you run can reach the network, which `docker compose up` does not allow, so keep both to your own machine. For Grapevine to be there, run `scripts/build-grapevine.sh` once in this checkout first.

### As a desktop app

```bash
scripts/dev-toolchain.sh                    # the compiler, in its container
dotnet run --project Blazemoji.Desktop
```

opens the editor in a window of its own. It has been run on macOS; the window library it uses, Hermes, also supports Windows and Linux. It needs the toolchain service for running programs, as above, and edits without it. Projects are folders under `Blazemoji` in your documents: each file as it is, with a `blazemoji.json` that names the project and its entry file. `Projects__Root` puts them somewhere else. A file or project you delete in the app, and a file another program changed that the app is about to write over, are not destroyed: they go to `.blazemoji/trash` in that folder. [docs/hosting.md](docs/hosting.md) has the details.

## Projects

A project is a set of files that are compiled together, a name, and one file marked as the entry: the file handed to the compiler. Other files join in when a file includes them with `📜`, by a path from the including file. The **Files** tab lists them as a tree, and each file has a menu to rename it, make it the entry, or delete it. A name with slashes puts a file in folders (`lib/greeter.🍇`).

In the web app projects are kept in the browser's local storage, so they are still there after a reload and are not shared between browsers. In the desktop app they are folders on disk. New projects start from a template: a folder under `Blazemoji.Core/Emojicode/Templates` with a `template.json` and the files.

A project runs either as a **Program**, which runs to the end and stops, or as a **Web server**, which keeps running until it is stopped or has had no request for ten minutes. A web server is told which port to listen on through the `PORT` environment variable. While it runs, the **Requests** tab sends it HTTP requests (method, path, headers, body) and shows the status, headers and body that come back.

## Help while typing

The editor has no language server. Three small Monaco providers hand the text and the cursor's position to plain C# (`Blazemoji.Core/Emojicode/Intelligence`), which answers from the keyword catalog, a list of emoji names, and the compiler's documentation report for each package the project imports.

- **Type a name to find an emoji.** `inbox` or `:inbox` offers 📥. Methods of the variables in scope come first, then keywords, then every other emoji. Tab accepts; Enter is always a new line.
- **Type the receiver and a dot to find a method.** Emojicode puts the method first (`📥 app 🔤/🔤 handler❗️`), so there is nothing to complete against when you start typing a call. `app.` lists the methods of `app`'s type, and accepting one rewrites it into `📥 app `. The editor works out a variable's type from the text: an initializer, a literal, a documented call, or a declared parameter.
- **Parameters appear once the receiver is there,** with the one you are on marked.
- **Hover** over a keyword, a type, a method or a variable to see what it is.
- **Problems show up while you type.** About half a second after a pause the project is compiled without linking, and errors are marked in the editor and counted on the Problems tab. Only Run brings that tab forward.

The provider code is TypeScript in `Blazemoji.Components/Scripts`. Its compiled JavaScript is committed, so building the app needs no Node; `scripts/build-js.sh` recompiles it after a change.

## Grapevine

Grapevine is an HTTP framework written in Emojicode. It has its own repository, so its package and its Todo sample are not committed here. One script builds them from a local checkout:

```bash
scripts/build-grapevine.sh
```

It reads the commit to build from `grapevine.pin`, takes that commit from `~/Code/grapevine` (or `GRAPEVINE_REPO`), builds the package in the toolchain container, and puts three things in place, all ignored by git: the package among the toolchain's packages, its documentation among the package documentation, and the Todo sample as the "Grapevine Todo API" project template. To move to a newer Grapevine, change the commit in `grapevine.pin` and run the script again. `scripts/test-in-docker.sh` and `scripts/e2e.sh` run it for you when the pin has changed.

Without it everything else works: `📦 grapevine 🏠` does not compile and the template is not offered.

## The toolchain service

Plain HTTP/1.1 and server-sent events, so that it can be reimplemented elsewhere.

| Request | Answer |
| --- | --- |
| `POST /compile` with `{ "files": { "main.🍇": "..." }, "entry": "main.🍇", "packages": [], "check": false }` | `{ "ok", "diagnostics": [], "buildId" }`. A failed build is still a 200. With `"check": true` nothing is linked or kept and there is no `buildId`: it is for diagnostics alone. |
| `POST /runs` with `{ "buildId", "env": {}, "http": false }` | `201` and `{ "runId" }`. With `"http": true` the program is run as a server: it is given a port in `PORT`, has no wall-clock limit, and is ended when it has had no request for the idle time. |
| `GET /runs/{id}/events` | An event stream of `stdout`, `stderr` and one final `exit`. Replays from the start, or from after `Last-Event-ID`. |
| `POST /runs/{id}/stdin` | Appends the body to the program's input; `?eof=true` ends it. |
| `DELETE /runs/{id}` | Stops the program. |
| Any method on `/runs/{id}/http/{path}` | Passes the request to a server program and returns its response. A response that comes from the service and not the program (no such run, not a server, ended, not listening yet, timed out) carries an `X-Toolchain-Proxy` header saying which. |
| `GET /packages`, `GET /packages/{name}/documentation.json` | The bundled packages and the compiler's documentation report for each. |

The compiler only produces an object file. The service links it itself against every bundled package, because the compiler's own link step fails when one package uses another.

Each run gets its own working directory and limits on wall-clock time, processor time, memory, file size and output. `Blazemoji.Toolchain.ContractTests` describes the contract from the outside: point `TOOLCHAIN_BASE_URL` at any implementation and run it.

## Running Tests

```bash
dotnet test --solution Blazemoji.sln
```

runs everything that can run on your machine. Tests that need the Emojicode compiler skip themselves anywhere other than x86_64 Linux (there they run, and need `g++` and `libtinfo5`). The contract tests and the browser tests skip unless they are told where a running service or app is.

```bash
scripts/test-in-docker.sh
```

runs the unit and compiler tests in a throwaway `linux/amd64` container with the real compiler, then starts the toolchain service there and runs the contract tests against it. It leaves nothing behind. This is the one to use day to day.

```bash
scripts/e2e.sh
```

builds both images, starts them with `docker compose`, checks that the toolchain container has no route out, and drives the app in a real browser (Playwright): running and stopping programs, live output, compiler errors as editor markers, projects with several files, and Grapevine's Todo sample answering requests from the Requests tab. The first run downloads Chromium.

```bash
scripts/desktop-smoke.sh
```

starts the desktop app, has it check itself from inside its own window, and closes it. `--published` tests a self-contained publish, and `--compile` also runs a program through the toolchain service.

```bash
docker build -f Blazemoji/Dockerfile --target test --output type=cacheonly --progress=plain .
```

runs the unit and compiler tests as part of an image build, which is what a build server would do. `--output type=cacheonly` stops Docker from keeping a 1.7 GB untagged image every time. Docker caches the stage, so a repeat run with unchanged sources prints nothing; add `--no-cache-filter test` to run the tests again.

## Emojicode
See the official [docs](https://www.emojicode.org/docs/) for more information on emojicode. The [language reference](https://www.emojicode.org/docs/reference/) will be very handy for writing emojicode.

## Contributing

Pull requests are welcome. For major changes, please open an issue first
to discuss what you would like to change.

Please make sure to update tests as appropriate.

## License

[MIT](https://choosealicense.com/licenses/mit/)
