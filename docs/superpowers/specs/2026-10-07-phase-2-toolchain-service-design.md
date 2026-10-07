# Phase 2: Headless toolchain service

- **Date:** 2026-10-07
- **Branch:** `phase-2-toolchain-service`, from `phase-1-runner`
- **Status:** written and self-approved during the overnight run Tom authorised on 2026-10-06; awaiting his review after the fact

## Context

Phase 1 put compile and run behind `IToolchain` inside the web app's process. Emojicode only runs on x86_64 Linux, and the plan is two hosts (a web playground and a Hermes desktop app) that share one way of building and running code. Phase 2 moves the toolchain into its own HTTP service and container, and makes the web app a client of it. The contract is small and plain HTTP/1.1 with server-sent events, so that Grapevine can implement the same contract later.

## Goal

Done when:

1. The web app compiles, runs, streams output and stops programs entirely through the service, with no compiler in the web image.
2. The contract has black-box tests that run against a base URL and know nothing about the implementation, so a future Grapevine implementation can be pointed at them.
3. Each run has its own directory, CPU, memory and wall-clock limits, and no outbound network.
4. Package documentation for the stock packages is served for Phase 4.

## Facts checked before writing this

| Fact | Consequence |
| --- | --- |
| `prlimit --cpu --as --fsize --nofile -- program` works on Emojicode binaries under emulation; a busy loop under `--cpu=2` is killed at 2.0 s. | Per-run CPU and memory limits can be applied without containers per run. |
| `unshare --net` and `unshare --user --net` are refused inside an unprivileged container. | A run cannot drop its own network. Outbound network is removed for the whole service container by attaching it only to a Docker `internal` network. |
| The compiler cannot produce `documentation.json` from an interface file (`🏛`); it needs package source, and the release ships no source. | Documentation for the stock packages is generated once from the Emojicode source at tag `v1.0-beta.2` and committed. |
| `🆕🔡▶️👂🏼❗️` reads a line from stdin. | stdin can be tested with a real program. |
| The Phase 1 run stream is pulled by a single reader. | The service owns a pump and a replay log per run, so a client can connect late or reconnect. |

## The contract

JSON is camelCase UTF-8. Errors are `application/problem+json` with a fixed `title`; no internal detail.

| Request | Success | Notes |
| --- | --- | --- |
| `POST /compile` `{ "files": { "main.🍇": "..." }, "entry": "main.🍇", "packages": [] }` | `200` `{ "ok": true, "diagnostics": [], "buildId": "..." }` | A failed build is still `200` with `ok: false` and no `buildId`. `400` for a malformed request, unsafe file name, entry not in files, or unknown package. `packages` may be omitted. |
| `POST /runs` `{ "buildId": "...", "env": { } }` | `201` `{ "runId": "..." }` | `404` for an unknown build. `429` when the service is at its concurrent run limit. |
| `GET /runs/{id}/events` | `200` `text/event-stream` | Events `stdout`, `stderr`, `exit`, each with an `id:` that increases by one. Replays from the start, or from after `Last-Event-ID`. The stream ends after `exit`. `404` for an unknown run. |
| `POST /runs/{id}/stdin` body is raw text | `204` | Appended to the program's stdin. `?eof=true` closes stdin after writing. `409` if the run has ended. |
| `DELETE /runs/{id}` | `204` | Stops the run. Stopping an ended run is still `204`. `404` for an unknown run. |
| `GET /packages` | `200` `[ "s", "files", ... ]` | Names that `packages` accepts. |
| `GET /packages/{name}/documentation.json` | `200`, the compiler's `-r` report | `404` for an unknown package. Cacheable. |
| `GET /health` | `200` | For container health checks. |

Event payloads:

```
id: 1
event: stdout
data: {"text":"one\n"}

id: 2
event: exit
data: {"exitCode":0,"reason":"exited","durationMs":1520}
```

`reason` is one of `exited`, `stopped`, `timedOut`, `outputLimit`, `failedToStart`. Diagnostics are `{ "severity": "error" | "warning", "file", "line", "character", "message" }` with the compiler's own position convention (1-based line, 1-based code-point character, 0 for none).

A run and its event log are kept for a short time after `exit` (default 2 minutes) so a slow client can still read them, then removed. A build is removed when its lifetime passes (default 10 minutes); a client does not have to release it.

## Design

### Projects

| Project | Holds | References |
| --- | --- | --- |
| `Blazemoji.Toolchain` | The contract (`IToolchain`, records) and `HttpToolchain`, the client. | nothing of ours |
| `Blazemoji.Toolchain.Local` | `LocalToolchain`, `LocalRun` and the process code, moved from `Blazemoji.Toolchain`. Gains stdin and resource limits. | `Blazemoji.Toolchain` |
| `Blazemoji.Toolchain.Service` | The ASP.NET host: endpoints, run registry and pump, SSE, package documentation. The compiler binary and packages move here from the web project. | `Blazemoji.Toolchain.Local` |
| `Blazemoji` (web) | Unchanged UI. `IToolchain` is now `HttpToolchain`. No compiler, no process code. | `Blazemoji.Toolchain` |
| `Blazemoji.Toolchain.ContractTests` | Black-box HTTP tests. No project references at all. | none |

`IToolchainRun` gains `WriteInputAsync(string text, bool endOfInput)`.

### Service internals

- **Run registry and pump.** `POST /runs` starts the process and hands the run to a hosted service through a typed channel. The hosted service consumes each run's events into that run's log, with bounded concurrency (`MaxConcurrentRuns`, default 8). Every pump task is awaited by the host; nothing is fire-and-forget. A run beyond the limit is refused with `429`.
- **Event log.** Append-only, numbered from 1, bounded by the output cap already enforced by the toolchain. Readers can start from any number and wait for more.
- **Expiry.** The same hosted service removes ended runs after their retention time and disposes them.
- **Resource limits.** Each program is started as `prlimit --cpu=<s> --as=<bytes> --fsize=<bytes> --nofile=<n> -- stdbuf -oL -eL <program>` when `prlimit` is present. Defaults: 20 CPU-seconds, 1 GiB address space, 16 MiB file size, 256 open files, alongside the existing 30 s wall clock and 4 MiB output cap. A process-count limit is deliberately left out: it is counted per user, and every run shares the service's user.
- **Package documentation.** JSON files generated ahead of time and shipped in the image under `package-docs/<name>/documentation.json`. A script regenerates them from the Emojicode source.

### Web app

`HttpToolchain` implements `IToolchain` over `HttpClient`, reading the event stream incrementally. `ToolchainClientOptions.BaseUrl` (section `ToolchainClient`) defaults to `http://localhost:5290`; this is the seam the Hermes host will use. If the service cannot be reached, the user sees a fixed message.

### Containers

- `toolchain` image: `aspnet:10.0` plus `g++`, `libtinfo5` and the compiler. `linux/amd64` only.
- `web` image: plain `aspnet:10.0`. It no longer needs the compiler, so it also builds natively for arm64.
- `docker-compose.yml`: `web` on the default network and on an `internal` network; `toolchain` only on the internal network, with no published ports. That is how runs get no outbound network.
- `scripts/e2e.sh` and `scripts/test-in-docker.sh` are updated to match.

## What this does not protect against

All runs share one container and one user. A program can read other runs' files, signal other runs, and exhaust shared limits. It can also call the service's own API on the loopback address, and leave a process behind that outlives its run if that process starts a session of its own. The service must not be exposed publicly as it stands.

**Proposal, not built:** for a public playground, run each build-and-run in its own short-lived sandbox. Azure Container Apps dynamic sessions fit: Hyper-V isolated session pools with a custom container, per-session network egress off by default, a session identifier per request, and idle cooldown. The custom container would be this `toolchain` image; the web app would call the session pool's endpoint with a session id per browser session, and the contract above would not change. Cost and cold-start time need measuring before committing; a pool with a few ready instances is the usual answer. The alternative of one Docker container per run on a single VM (gVisor or Kata runtime) is cheaper to start with and worse to operate.

## Testing

- **Contract tests** (`Blazemoji.Toolchain.ContractTests`): every row of the contract table, the SSE framing, replay with `Last-Event-ID`, late connection, stop, stdin with a real program, `429` at the limit, unknown ids, malformed bodies. They read `TOOLCHAIN_BASE_URL` and skip without it. `scripts/test-in-docker.sh` starts the service inside the test container and runs them against it.
- **Service tests** (any platform): the same HTTP surface hosted in memory over a scripted toolchain, for protocol behaviour that does not need the compiler (event numbering, replay, expiry, concurrency limit, problem responses).
- **Client tests**: `HttpToolchain` against the in-memory service: event parsing, chunk boundaries in the middle of an event, cancellation, service unavailable.
- **Local toolchain**: existing tests move with the code; new ones for stdin and for each resource limit (a busy loop hits the CPU limit; a program that allocates without bound hits the memory limit).
- **Isolation check**: with the compose stack up, a process in the `toolchain` container cannot reach an outside address, and the web app can reach the service.
- **Browser tests**: unchanged, now against the two-container stack.

## Out of scope

- A stdin box in the UI, package selection in the UI, multi-file projects (Phase 3).
- The `/runs/{id}/http/*` proxy (Phase 3).
- Authentication between web and service; they share a private network.
- Per-run containers (proposed above).

## Risks

| Risk | Handling |
| --- | --- |
| `RLIMIT_AS` behaves differently under emulation than on real x86_64. | Tested where it runs today; the limit is configuration and can be raised or disabled. |
| Server-sent events through proxies get buffered. | The service sets `Cache-Control: no-cache` and `X-Accel-Buffering: no` and flushes after every event. |
| Docker disk on Tom's machine is nearly full. | Two small images that share layers with what is already cached; no repeated image builds; clean up what this work creates. |
| The contract drifts from what Grapevine can implement. | Plain JSON, no chunk extensions, no HTTP/2 features, SSE only. The contract tests use nothing but an HTTP client. |

## Decisions taken on Tom's behalf

1. Three toolchain projects (contract and client, local implementation, service) instead of one.
2. Builds expire by lifetime; there is no release endpoint, to keep the contract small.
3. Stock package documentation is generated once and committed, with a script to regenerate it.
4. No stdin UI in this phase.
5. No outbound network is achieved with Docker networking, not inside the service.
6. `GET /packages` and `GET /health` are additions to the brief's endpoint list.

## Amendments during implementation

1. **No separate plan document.** The spec fixes the contract and the project layout; the work was done as nine tasks recorded in the session ledger and in the commit history.
2. **Both images are `linux/amd64`.** The web image no longer needs the compiler and could be built natively for arm64, but Docker's disk on Tom's machine was nearly full and a second base image would not fit. It is a one-line change in the Dockerfile.
3. **A host asks "does this build exist" through `IBuildStore`,** a second small interface the local toolchain implements, so that `POST /runs` can answer 404 without starting anything.
4. **The web app ends each program's input straight away,** by sending an empty `stdin?eof=true`, because the page has no input box yet. A program that reads input therefore sees the end of it, as it did in Phase 1.
5. **JSON escapes.** Emoji outside the Basic Multilingual Plane are written as `\uD83D\uDE00`-style escapes in responses and events. System.Text.Json offers no way to write them raw. Any JSON parser reads them back, and the contract tests compare parsed values.
6. **Routes are relative on the client.** The service can sit under a path (`http://gateway/toolchain/`), and the client keeps that path.
7. **File names and positions follow the Phase 1 review fixes:** names are restricted to characters that mean nothing to a shell, and diagnostics count characters from one on every line.
8. **Known operational risk under emulation.** On Apple Silicon the service runs under Rosetta, where a .NET process has twice been seen to freeze completely during test runs. If the toolchain container ever stops answering on a Mac, restart it. This has not been seen, and is not expected, on real x86_64.
9. **Contract tests found nothing to fix.** All 32 passed on their first run against the service, which also means they were never watched failing against it; the in-memory service tests cover the same behaviour independently.

## Changes after the independent review

The review found no critical defects and six important ones. All six are fixed on this branch.

1. **`scripts/test-in-docker.sh` reported success when the unit tests failed.** Its inner script had no `set -e`, and once the contract tests were added after the unit tests the exit status was theirs alone.
2. **Finished runs were kept without any ceiling.** Forty runs that each hit the output cap held 650 MB within two seconds. Finished runs are now kept up to a count (`MaxRetainedRuns`, 32) and an approximate amount of memory (`MaxRetainedBytes`, 64 MiB), oldest dropped first, the newest always kept. Compiles are limited to `MaxConcurrentCompiles` (4) at once; one beyond that is `429`. Request bodies over `MaxRequestBodyBytes` (4 MiB) are `413`.
3. **The compose file set no limits on the toolchain container.** A program can start processes of its own, each with a fresh per-process allowance, so `prlimit` alone is not a ceiling. The container now has limits on processes (1024), memory (2 GB) and CPUs (4), a read-only file system with an in-memory `/tmp`, no capabilities and `no-new-privileges`.
4. **The client waited for ever, and Stop could throw into the page.** The client now gives up on a request after `ToolchainClient:RequestTimeout` (2 minutes, longer than the slowest compile); an open event stream is not subject to it. Stop gives up after 10 seconds and never throws.
5. **A program ended by the CPU limit was reported as having exited with an odd code.** It is now reported as `timedOut`, the same as the wall-clock limit, which is what the same endless loop produced in Phase 1. The CPU limit is set as a soft limit with a hard one a second later, because only the soft limit ends a program with a signal that says why.
6. **The contract tests had gaps.** They now own their test programs (no file linked from another project), run one class at a time so that they need only one free run slot, omit `env` when there is none, and assert content types for real (three assertions had been skipped when the header was missing). New tests: `429` at the run limit and the slot coming back, `413` for an oversized body, `400` for a body that is not JSON.

The end-to-end script's isolation check now first proves the toolchain container answers its own health check, so a container that is not running can no longer pass as "no route out". It also reports whether public names resolve from inside it.

Additions to the contract table: `POST /compile` can answer `429`; any request can answer `413`.

### The freeze under emulation, looked at properly

Amendment 8 recorded that a .NET process had twice frozen under amd64 emulation. With more tests it became frequent (four test runs in five), so it was investigated instead of worked around:

- In a frozen process every thread, including the runtime's own signal, timer and socket threads, is parked in the kernel on a priority-inheritance lock of its own. That is not something .NET does; it is the emulator stopping every thread, and never starting them again. `SIGTERM` is ignored and exited child processes are never collected.
- It happens within about five seconds of start, never later. Runs that get past that point finish.
- The tests that start processes, run on their own, did not freeze in five runs out of five. The same tests run beside the component and web tests froze four times in five. So the trigger is a process that is compiling a great deal of its own code while it also starts other processes.
- Serialising process starts, replacing `vfork` with `fork`, turning off write-xor-execute, tiered compilation, concurrent collection and signal-based suspension, and a much larger first-generation budget were each tried and each still froze.
- The emulator was also seen to abort the process outright (exit 133) with an assertion about another process's `/proc` entry.

What was done: every test that starts a process lives in `Blazemoji.Test.Toolchain`, and the Docker script and the image's test stage run that namespace on its own, apart from the rest. A run that froze or was aborted is tried again, up to three times; a test that fails is never retried. None of this is needed on real x86_64.

What it means for the service: it compiles far less of its own code than a test run does, and it never froze in the browser test runs, but the same emulator runs it on an Apple Silicon Mac. If Run Code ever stops answering there, restart the toolchain container.
