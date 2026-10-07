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

All runs share one container and one user. A program can read other runs' files, signal other runs, and exhaust shared limits. The service must not be exposed publicly as it stands.

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
