# Phase 3: Projects and long-running programs

- **Date:** 2026-10-07
- **Branch:** `phase-3-projects`, from `phase-2-toolchain-service`
- **Status:** written and self-approved during the overnight run Tom authorised on 2026-10-06; awaiting his review after the fact

## Context

Phases 1 and 2 made one file compile and run reliably behind a small HTTP service. Grapevine is not one file: its Todo sample is three files that import a package, and it is a server that runs until stopped and is only useful if something can send it requests. Phase 3 adds projects, the Grapevine package, and a way to talk to a running program.

## Goal

Done when Grapevine's Todo sample is built from a project in Blazemoji, runs, and answers create, read, update and delete requests sent from a request panel in the page. A browser test does exactly that.

## Facts checked before writing this

| Fact | Consequence |
| --- | --- |
| The compiler's own link step lists package archives in an order that fails when one package depends on another (Grapevine imports `json`). Compiling with `-c` and linking with `c++ program.o -Wl,--start-group <every package archive> -Wl,--end-group -lm -lpthread` works for Grapevine and for plain programs. | The toolchain links programs itself. This also removes the compiler's shell-built link command and gives a real linker exit code. |
| Only the `s` package declares link hints (`m`, `pthread`). | The link command names those two libraries and nothing per package. |
| Grapevine's package builds with the compiler and the three runtime headers already in this repository (`emojicodec/include`). The Todo sample built against it answered `POST /todos` with `201` and `GET /todos` with the list, reading its port from `PORT`. | No new toolchain pieces are needed, and the service can tell a program which port to use. |
| `~/Code/grapevine` has no git remote. This repository is public on GitHub. | Grapevine's source, its built package and its Todo sample are **not committed here**. A script builds them from the local checkout at a pinned commit into git-ignored folders. See "Grapevine is built, not committed". |
| The compiler reports the file of a diagnostic as it resolved it. | The toolchain normalises it to the project-relative path, so the page can match a problem to a file. |

## The contract: what changes

Additions only. Everything in the Phase 2 contract still holds.

| Request | Success | Notes |
| --- | --- | --- |
| `POST /runs` `{ "buildId": "...", "env": { }, "http": true }` | `201` `{ "runId": "..." }` | With `http: true` the run is a server run: the service picks a free port and passes it to the program as `PORT` (overriding any `PORT` in `env`). The wall-clock limit does not apply; the run ends with reason `idle` when no request has been proxied to it for the idle time (default 10 minutes). |
| `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `HEAD`, `OPTIONS` `/runs/{id}/http/{path}` | The program's own response: status, headers and body | The path after `/http` and the query string are forwarded as they are. Request and response bodies are buffered, so streamed responses arrive whole. Hop-by-hop headers are not forwarded. |

Every response on the proxy route that does **not** come from the program carries the header `X-Toolchain-Proxy` with the reason, so a client can tell a program's `404` from the service's:

| Status | `X-Toolchain-Proxy` | When |
| --- | --- | --- |
| `404` | `unknown-run` | No such run. |
| `409` | `not-a-server` | The run was not started with `http: true`. |
| `409` | `ended` | The run has ended. |
| `413` | `too-large` | The request body is over the limit (default 1 MiB). |
| `502` | `not-listening` | Nothing accepted a connection on the program's port. Usual while it is starting. |
| `502` | `bad-response` | The program closed the connection or sent something that is not HTTP, or its response is over the limit (default 4 MiB). |
| `504` | `timed-out` | The program did not answer in time (default 30 s). |

`reason` in the `exit` event gains the value `idle`. Diagnostics name files by their path in the request's `files`.

## Design

### Toolchain library

- **Linking.** `CompileAsync` runs `emojicodec <entry> --json -c -o program.o -S <packages>` and then the linker directly (no shell): `c++ program.o -Wl,--start-group <packages>/*/lib*.a -Wl,--end-group -lm -lpthread -o program`. A linker failure is a failed build with the fixed message "The program could not be linked."; the linker's output goes to the log.
- **Diagnostic files.** Paths are made relative to the build directory, with forward slashes.
- **Server runs.** `RunRequest` gains `Server`. `IToolchainRun` gains `SendHttpAsync(ProgramRequest, CancellationToken)` returning a `ProgramResponse` whose `Outcome` is `Answered`, `NotAServer`, `Ended`, `NotListening`, `BadResponse`, `TooLarge` or `TimedOut`. `LocalToolchain` picks a free loopback port for a server run, sets `PORT`, and gives the run an idle deadline in place of the wall-clock one. Every `SendHttpAsync` call moves the deadline. `RunEndReason` gains `Idle`.
- **Limits for server runs.** The output cap, memory, file and open-file limits stay. The CPU limit has its own, larger setting (`ServerCpuSeconds`, default 300), because a server lives longer than a script.

### Service

`ANY /runs/{id}/http/{**path}` reads the request (method, path and query, headers, body up to the limit), calls `SendHttpAsync` on the run, and writes the result back as the table above describes. The service knows nothing about ports. `GET /packages` lists `grapevine` when its documentation is present.

### Grapevine is built, not committed

- `grapevine.pin` at the repository root holds one commit id. Bumping Grapevine is changing that line and running the script.
- `scripts/build-grapevine.sh` exports that commit from `${GRAPEVINE_REPO:-~/Code/grapevine}` with `git archive`, builds the package in the toolchain container, and writes:
  - `Blazemoji.Toolchain.Local/packages/grapevine/` (`libgrapevine.a`, `🏛`, and a `.commit` stamp),
  - `Blazemoji.Toolchain.Service/package-docs/grapevine/documentation.json`,
  - `Blazemoji/Emojicode/Templates/grapevine-todo/` (the Todo sample's three files and a `template.json`).
- All three are git-ignored. `scripts/test-in-docker.sh` and `scripts/e2e.sh` run the script first when the stamp does not match the pin. Images built by `docker compose` include whatever is on disk.
- Without them everything else works: the package is simply not offered, the template is not listed, and the tests that need Grapevine are skipped (they carry a `Requires=Grapevine` trait so the image's test stage can leave them out when the package is absent).
- To commit Grapevine here instead, delete three lines from `.gitignore`. That is Tom's decision because it publishes Grapevine.

### Projects in the web app

**Model** (plain classes, no UI types): `Project { Id, Name, Kind (Program or Server), Entry, Files }`, `ProjectFile { Path, Content }`. Paths are forward-slash relative paths that pass the same rules the toolchain enforces, and end in `.🍇`.

**State.** `ProjectState` (scoped, like `RunState`) owns the current project, the list of saved projects, and which file is open. Components call its methods; it raises `StateChanged`. Operations: load, create from a template, open, rename and delete a project; add, rename and delete a file; set the entry file; set the kind; replace a file's content. It persists through `IProjectStore`.

**Persistence.** `LocalStorageProjectStore` keeps an index (`blazemoji.projects`) and one entry per project (`blazemoji.project.<id>`) in the browser's local storage. The page loads projects after its first render, because local storage is only reachable then. A browser with nothing saved gets a "Hello World" project. The editor pushes content changes to the state after a 400 ms pause, and always before a run or a file switch. If saving fails (storage full or blocked) the Files panel shows a warning and the session carries on in memory.

**Templates.** `IProjectTemplates` reads folders under `Emojicode/Templates/`: a `template.json` (name, description, kind, entry) and the files. Committed: "Hello World" and "Two files" (an include). Generated when Grapevine is built: "Grapevine Todo API".

**The existing Library** (samples and saved snippets) stays. Clicking an entry still replaces the open file's content. Its "clear" action now removes only snippet keys; today it clears all of local storage, which would delete projects.

### UI

- **Sidebar** gains a first tab, **Files**: a project picker with New, Rename and Delete; the file tree (folders from paths, the entry file flagged); New file. Each file row has an always-visible menu: Rename, Set as entry, Delete. Deleting a project or a file asks first.
- **Editor.** One Monaco model per file, so each file keeps its own undo history and markers. A caption above the editor shows `project / path`.
- **Run** sends every file and the entry. A Server project starts a server run; the status reads "Running" with the idle rule in the tooltip.
- **Problems** show the file when the project has more than one, and clicking opens that file at the position.
- **Requests**, a third tab beside Output and Problems: method, path, headers (one `Name: value` per line), body, Send; then the response's status line, time taken, headers and body. The last ten exchanges of the session are listed and can be re-opened. With no server running, the tab shows an info alert and Send is disabled. Proxy outcomes other than `Answered` are shown as a fixed sentence each (for example "The program is not accepting connections yet.").

`RequestState` (scoped) owns the exchange history and sends through `RunState`, which owns the run.

## Testing

- **Toolchain, real compiler in Docker:** every existing test still passes with the new link step; a link failure is a failed build; diagnostics in an included file and in a subfolder name the right path; a server run gets `PORT`, answers through `SendHttpAsync`, ends `idle`, and stays alive while requests keep coming; the outcomes `NotListening`, `Ended`, `NotAServer`. With Grapevine built: the Todo sample compiles, runs and does a CRUD round trip.
- **Service, in memory:** the proxy route's mapping in both directions, each `X-Toolchain-Proxy` case, header filtering, the body limit.
- **Client:** `HttpToolchain.SendHttpAsync` against the in-memory service.
- **Contract tests:** a server program written with the stock `sockets` package (so they do not need Grapevine): `PORT` is set, a request is proxied and answered, a non-server run is `409`, an unknown run is `404`.
- **State:** `ProjectState` (every operation, entry reassignment, persistence calls, failed saves), `RequestState`, `RunState` for server runs, templates, the store's round trip, the Library's clear.
- **Components (bUnit):** the Files panel and the Requests panel.
- **Browser (Playwright):** create a project from "Two files", run it; projects survive a reload; a problem in a second file opens that file; the Grapevine Todo CRUD round trip from the request panel.

## Out of scope

- Uploading or downloading project files, drag and drop, open-file tabs.
- Streaming proxied responses, WebSockets, a browser preview of pages the program serves.
- A stdin box.
- Choosing packages in the UI: every bundled package is on the search path.
- Sharing projects between browsers.

## Risks

| Risk | Handling |
| --- | --- |
| The memory limit (1 GiB of address space) is too tight for Grapevine's 64 worker threads. | Tested with the real sample under the real limits; the limit is configuration. |
| Local storage is small (about 5 MB) and can be blocked. | Projects are text and small; a failed save is shown and does not lose the session. |
| A free port picked by the service is taken before the program binds it. | Rare; the program fails to start listening and requests report `not-listening`. Run again. |
| Docker disk on Tom's machine is nearly full. | The Grapevine build runs in the existing test image and writes 330 KB. The browser run needs the two app images rebuilt once; they are removed afterwards. |

## Decisions taken on Tom's behalf

1. Grapevine is built from the local checkout and kept out of this public repository.
2. Project kind (Program or Server) is an explicit setting, not detected.
3. The proxy buffers; it goes through the run abstraction, so the service never learns ports.
4. Server runs end after 10 idle minutes and have no wall-clock limit.
5. The toolchain links programs itself.
6. One Monaco model per file; no open-file tabs.
7. The Library stays as it is, apart from its clear action.

## Amendments during implementation

1. **Two more outcomes for a request.** `InvalidRequest` (the method, path or a header cannot be sent as HTTP; `400` with `invalid-request` on the wire) and, on the client only, `Unavailable` (the toolchain service itself could not be reached).
2. **Paths that could climb out are refused.** A request path with a `.` or `..` segment (plain or percent-encoded), a backslash or a control character is not sent. The client also checks that the address it resolved is still under the run's `/http/` route. Without this a path such as `/../../compile` typed into the request panel would have been a request to the toolchain service itself.
3. **The file name rule moved into the contract project** (`Blazemoji.Toolchain.SourceFileNames`), because the web app now applies it to project files as well.
4. **The page's columns are 3, 6 and 3 of twelve at every width.** The right-hand column was 2 of 12 below 1280 px, which is too narrow for a request form.
5. **JSON bodies are laid out without being re-serialised.** Serialising again would turn every emoji into a pair of escapes, in an editor for a language written in emoji.
6. **The Todo sample's own test file comes with the template.** Making `tests.🍇` the entry and running it runs Grapevine's in-memory tests, and a toolchain test does exactly that.
7. **Templates are tested for real:** every committed template is compiled, and the two-file one is run, by the Docker tests.
8. **The editor's first, empty model is left in place.** Disposing it through BlazorMonaco failed in the browser; it holds nothing and nothing refers to it.
9. **The per-program address-space limit went from 1 GiB to 4 GiB.** Grapevine's own tests, run from the sample project, were aborted by the emulator at 1 GiB (`rosetta error: mmap_anonymous_rw mmap failed`): the limit counts reserved address space, which 64 thread stacks and the emulator's tables fill quickly. Real memory use is still capped by the container's 2 GB limit.
