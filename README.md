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
- Library of sample scripts with support for saving scripts in local storage
- More coming soon
  - Researching syntax highlighting in monaco for ☁️ and 🔤

## Prerequisites
- .NET 10 SDK
- Docker, for anything that compiles or runs Emojicode

The Emojicode compiler bundled in this repo is the x86_64 Linux build of 1.0 beta 2, so it runs in a `linux/amd64` container. On Apple Silicon, Docker Desktop emulates it.

## How it fits together

| Project | What it is |
| --- | --- |
| `Blazemoji` | The web app: editor, toolbox, output and problems. It holds no compiler. |
| `Blazemoji.Toolchain` | The toolchain contract (`IToolchain`) and `HttpToolchain`, its client. |
| `Blazemoji.Toolchain.Local` | Compiles and runs programs as local processes. Holds the compiler and stock packages. |
| `Blazemoji.Toolchain.Service` | A small HTTP service in front of the local toolchain. |

The web app reaches the toolchain service at `ToolchainClient:BaseUrl` (default `http://localhost:5290`). Any host that can make HTTP requests can use the same service; that setting is the only thing it needs.

## Run the app

```bash
docker compose up --build
```

Then open http://localhost:5080.

This starts two containers. `web` is published on this machine only. `toolchain` publishes no port and sits on an internal Docker network, so the programs it runs have no route to the internet or to your network.

The app compiles and runs whatever code it is given, with no sign-in, and every program shares the one toolchain container. Do not expose it beyond your own machine.

`dotnet run --project Blazemoji` starts the web app alone, which is fine for working on the UI. Run Code then needs a toolchain service to talk to; `dotnet run --project Blazemoji.Toolchain.Service` provides one where the compiler can execute: x86_64 Linux with `g++` and `libtinfo5` installed.

## The toolchain service

Plain HTTP/1.1 and server-sent events, so that it can be reimplemented elsewhere.

| Request | Answer |
| --- | --- |
| `POST /compile` with `{ "files": { "main.🍇": "..." }, "entry": "main.🍇", "packages": [] }` | `{ "ok", "diagnostics": [], "buildId" }`. A failed build is still a 200. |
| `POST /runs` with `{ "buildId", "env": {} }` | `201` and `{ "runId" }` |
| `GET /runs/{id}/events` | An event stream of `stdout`, `stderr` and one final `exit`. Replays from the start, or from after `Last-Event-ID`. |
| `POST /runs/{id}/stdin` | Appends the body to the program's input; `?eof=true` ends it. |
| `DELETE /runs/{id}` | Stops the program. |
| `GET /packages`, `GET /packages/{name}/documentation.json` | The stock packages and the compiler's documentation report for each. |

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

builds both images, starts them with `docker compose`, checks that the toolchain container has no route out, and drives the app in a real browser (Playwright): running and stopping programs, live output, compiler errors as editor markers, and the rest of the page. The first run downloads Chromium.

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
