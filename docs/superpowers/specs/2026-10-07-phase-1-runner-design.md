# Phase 1: Fix the runner

- **Date:** 2026-10-07
- **Branch:** `phase-1-runner`, from `phase-0-baseline`
- **Status:** written and self-approved during the overnight run Tom authorised on 2026-10-06; awaiting his review after the fact

## Context

Phase 0 left the runner as it was. It decides success by whether stderr is empty, writes every compile to one shared `temp.o`, reads stdout only after exit, and shows nothing until the program ends. Phase 1 replaces it. Phase 2 will move compile and run behind an HTTP service, so the replacement is shaped like that service's contract from the start.

## Goal

Fix runner bugs 1 to 4, add a Stop button and a configurable timeout, and show compiler diagnostics as editor markers and a problems list.

Done when:

1. A build that emits the compiler's "Run-time type information" warning is reported as a success, with the warning shown as a problem.
2. Two browser sessions compiling and running at once each get their own result.
3. A program that prints more than 1 MB completes.
4. Output appears line by line while the program is still running.
5. Stop ends a running program, and a program that outlives the timeout is ended.
6. A compile error shows a squiggle at the right place in the editor and an entry in the problems list.

On criterion 1: the brief says "the warning-emitting Grapevine package builds green". Building Grapevine itself needs package mode and a search path, which arrive in Phase 3. Phase 1 proves the same behaviour with a small program that makes the compiler print that exact warning.

## Facts checked before writing this

Probed on 2026-10-06 and 2026-10-07 in the `linux/amd64` app image.

| Fact | Consequence |
| --- | --- |
| With `--json`, diagnostics go to stdout as a JSON array, stderr stays empty, and the exit code is 1 on error and 0 otherwise. Warnings are in the array with `"type":"warning"`. | Always compile with `--json`. Success is the exit code. |
| The RTTI warning has `line: 0`, `character: 0`, `file: ""`. | Some diagnostics have no location. They go in the problems list without an editor marker. |
| `character` is 1-based and counts Unicode code points. `😀 nope` reports 5 for `nope`; a name at the start of a line reports 1. | Monaco columns are 1-based UTF-16 units, so positions are converted. |
| A parse error can report a line past the end of the file with `character: 0`. | Positions are clamped to the document. |
| The compiler stops at the first semantic error. | The problems list is usually one error long. |
| `😀` writes through `std::cout` with no flush. Through a pipe, `one` then a 1.5 s sleep then `two` arrives all at once at exit; under `stdbuf -oL` the first line arrives after 30 ms. | Programs are started under `stdbuf -oL -eL` when it is available. |
| Compiling from an empty working directory with `-S <packages>` works, and the compiler leaves `<name>.o` beside the output. | Each build gets its own directory; nothing needs the app folder to be writable. |
| `InfiniteLoop.🍇` writes about 2 GB in 5 seconds. | Output needs a cap, and the UI must not re-send the whole output on every update. |
| `🚪🐇💻 3❗️` exits with code 3. | Exit codes can be tested. |
| Mythetech.Framework 0.3.1 targets `net11.0` only. | No Framework message bus in this phase. State classes follow the same pattern without it. |

## Design

### 1. A toolchain library with a contract Phase 2 can put on the wire

New project `Blazemoji.Toolchain` (plain `Microsoft.NET.Sdk`, `net10.0`, no Blazor or MudBlazor reference). It holds the contract and the process-based implementation.

```csharp
public interface IToolchain
{
    Task<CompileResult> CompileAsync(CompileRequest request, CancellationToken cancellationToken = default);
    Task<IToolchainRun> StartRunAsync(RunRequest request, CancellationToken cancellationToken = default);
    Task ReleaseBuildAsync(string buildId);
}

public interface IToolchainRun : IAsyncDisposable
{
    string RunId { get; }
    IAsyncEnumerable<RunEvent> ReadEventsAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}

public sealed record CompileRequest(IReadOnlyDictionary<string, string> Files, string Entry);
public sealed record CompileResult(bool Ok, IReadOnlyList<Diagnostic> Diagnostics, string? BuildId);
public sealed record Diagnostic(DiagnosticSeverity Severity, string File, int Line, int Character, string Message);
public enum DiagnosticSeverity { Error, Warning }

public sealed record RunRequest(string BuildId, IReadOnlyDictionary<string, string>? Environment = null, TimeSpan? Timeout = null);

public abstract record RunEvent;
public sealed record StdoutEvent(string Text) : RunEvent;
public sealed record StderrEvent(string Text) : RunEvent;
public sealed record ExitEvent(int? ExitCode, RunEndReason Reason, TimeSpan Duration) : RunEvent;
public enum RunEndReason { Exited, Stopped, TimedOut, OutputLimit, FailedToStart }
```

This is the Phase 2 HTTP contract in C# form: `POST /compile` is `CompileAsync`, `POST /runs` is `StartRunAsync`, the SSE stream is `ReadEventsAsync`, `DELETE /runs/{id}` is `StopAsync`. `Files` is already a map so that Phase 3 can send several files without a contract change; the UI sends one file in this phase.

`ToolchainOptions`, bound from the `Toolchain` configuration section, with working defaults in the class:

| Option | Default | Meaning |
| --- | --- | --- |
| `CompilerPath` | `<app>/emojicodec/emojicodec` | The compiler binary. |
| `PackagesPath` | `<app>/packages` | Passed to the compiler with `-S`. |
| `WorkRoot` | `<temp>/blazemoji` | Parent of every build and run directory. |
| `CompileTimeout` | 60 s | Wall clock limit for one compile. |
| `RunTimeout` | 30 s | Wall clock limit for one run. This is the configurable timeout. |
| `MaxOutputBytes` | 4 MiB | Combined stdout and stderr before the run is ended. |
| `BuildLifetime` | 10 min | Unreleased builds older than this are deleted on the next compile. |

"Configurable" here means configuration (`appsettings.json` or an environment variable such as `Toolchain__RunTimeout`), not a control in the UI. A per-run control can be added once long-running server programs arrive in Phase 3.

### 2. `LocalToolchain`

**Compile.** Create `<WorkRoot>/builds/<buildId>/`, write the request's files into it, run `emojicodec <entry> --json -o <dir>/program -S <PackagesPath>` with that directory as the working directory, read stdout and stderr concurrently, and parse stdout as diagnostics. `Ok` is `exit code == 0`. On failure the directory is deleted and `BuildId` is null. File names are validated: relative, no `..`, no rooted paths, inside the build directory.

If stdout is not valid JSON, or the compiler cannot be started, the result is `Ok = false` with one error diagnostic carrying a fixed message; the detail is logged, not shown.

**Run.** Create `<WorkRoot>/runs/<runId>/` as the program's working directory. Start `stdbuf -oL -eL <build>/program` when `stdbuf` is on the path, otherwise the program directly. Read both pipes concurrently in chunks and publish each chunk as an event on a bounded `Channel<RunEvent>`, so a slow reader slows the program instead of growing memory. A chunk never ends in half a surrogate pair.

The run ends, with exactly one `ExitEvent` as the last event, when the process exits, `StopAsync` is called, `RunTimeout` passes, or `MaxOutputBytes` is exceeded. Ending a run kills the whole process tree. Disposing the run stops it if needed and deletes its directory.

CPU and memory limits and network isolation are Phase 2, where each run gets a container.

### 3. The web app

**Removed:** `CompilerService`, `ICompilerService`, `CodeRunner`, `ICodeRunner`, `EmojicodeProcessFactory`, `CompiledEmojicodeFile`, `EmojicodeResult`.

**`RunState`** (a State class, scoped per circuit). It owns everything about the current run and is the only thing that talks to `IToolchain`.

- State: `Status` (Idle, Compiling, Running), the last `MaxLines` (5,000) output lines each tagged stdout or stderr with a running number, the total line count, the current diagnostics, and a summary of the last run (exit code, reason, duration).
- Methods: `RunAsync(string code)` and `StopAsync()`. `RunAsync` compiles, publishes diagnostics, starts the run, and consumes its events until the exit event, then releases the build. It is awaited by the click handler; nothing is fire-and-forget.
- `StateChanged` is raised at most about 20 times a second while output is flowing, with a trailing notification so the last lines of a burst are never left unshown.
- Failures are logged and shown as a fixed message, never as `exception.Message`.
- Disposing the state (circuit end) stops any run.

**Components.**

- `OutputPanel` renders from `RunState`: a tab for Output and a tab for Problems with a count. Output lines are keyed, so each update sends only the new lines. stderr lines are styled as errors. A status line shows running, exit code and duration, or why the run was ended, and says when earlier lines were dropped.
- `ProblemsPanel` lists diagnostics with severity, message and position. Choosing one moves the editor to it.
- The toolbar gets a Stop button beside Run. It is always present and is disabled when nothing is running, so the toolbar does not shift.
- `EmojiCodeEditor` gains `SetMarkersAsync` (Monaco model markers, owner `emojicodec`) and `RevealPositionAsync`. Markers are replaced on every compile and cleared when a file is loaded.

New UI code follows the house rules: icons through a `BlazemojiIcons` class, scoped `.razor.css` instead of inline styles, `EventCallback` parameters, no layout shift.

**Position mapping** lives in the web project as a pure function: compiler line and code-point character to a Monaco range. The marker starts at the reported position and runs to the next whitespace or the end of the line. Diagnostics without a location get no marker.

### 4. Container

The app user no longer needs to own `/app`. The `final` stage goes back to a root-owned application folder; work happens under the temp directory.

## Testing

- **Toolchain, real compiler (Docker, skipped elsewhere):** warning build is `Ok`; compile error returns diagnostics and no build; exit code and output; more than 1 MB of output completes; first output arrives well before exit; eight concurrent compile-and-run calls each get their own output; Stop; timeout; output limit on `InfiniteLoop`; a crash reports stderr and a non-zero or signal exit; directories are gone after release and dispose; every shipped sample runs.
- **Unit, any platform:** diagnostics parsing (the probe outputs above are the cases); file name validation; position mapping including emoji before the error, a line past the end, and `character: 0`; `RunState` against a scripted toolchain (status transitions, line tail and numbering, notification throttling with trailing flush, stop, failure message).
- **bUnit:** `OutputPanel` and `ProblemsPanel` render what the state holds and raise their callbacks.
- **Browser (Playwright, new `Blazemoji.E2E` project):** run a sample; output appears before the program ends; Stop on `InfiniteLoop`; an error shows a marker and a problem; two browser contexts running different programs at once; plus the Phase 0 flows (theme, picker, dialogs, save and reload). The project runs against a base URL and skips when none is given. A script builds the image, starts it, runs the browser tests and stops it.

NSubstitute is added now for `IToolchain`. bUnit is added now.

## Out of scope

- Multi-file projects, includes and third-party packages in the UI (Phase 3). The contract already accepts several files.
- stdin, environment variables in the UI, the HTTP service, containers per run (Phase 2).
- Diagnostics while typing (Phase 4).
- Reworking existing components that this phase does not touch.

## Risks

| Risk | Handling |
| --- | --- |
| `stdbuf` relies on the program using C stdio through a dynamically linked libc. | Verified for Emojicode binaries. If `stdbuf` is missing the program still runs, with output arriving late; a test asserts streaming in the image. |
| Killing under emulation leaves a child behind. | Kill the process tree and test that the run directory can be deleted afterwards. |
| A flood of output saturates the circuit. | Output cap in the toolchain, bounded channel, line tail and throttled notifications in the state, keyed lines in the component. |
| Browser tests are slow or flaky under emulation. | They run on the host browser against the container, with generous timeouts on compile steps only. |

## Decisions taken on Tom's behalf

1. A new `Blazemoji.Toolchain` project now, instead of rewriting the service inside the web project and moving it in Phase 2.
2. Timeout is configuration, not UI.
3. The output panel keeps the last 5,000 lines.
4. Default output cap 4 MiB and run timeout 30 s.
5. Plain State classes without Mythetech.Framework, because the Framework is `net11.0` only.

## Amendments during implementation

1. **Events are pulled, not pumped.** Section 2 says each chunk is published on a bounded channel. Instead `ReadEventsAsync` reads both pipes itself and yields as the caller enumerates. There is no background task to leave unawaited, and a slow reader slows the program through its pipes. Consequence for Phase 2: the HTTP service needs its own per-run pump and replay buffer, which it needs anyway because a client can connect after output has started.
2. **An Emojicode panic goes to stdout.** A program that unwraps an empty optional prints `🤯 Program panicked: ...` on stdout, not stderr, and exits with 134. The crash test pins that instead of expecting stderr.
3. **Run mechanics are tested with shell scripts.** A script placed where a compiled program would be lets streaming, stop, timeout, output limit, environment and cleanup be tested on macOS as well as in Docker. Tests against real Emojicode binaries cover only what depends on them.
4. **Notifications are trailing-edge.** The first output schedules one announcement 50 ms later. There is no immediate announcement. A burst followed by silence is still shown, which was the requirement.
5. **Stop also cancels a compile in progress.**
6. **Two page-load races that predate this phase were fixed**, because the new browser tests exposed them. `App.razor` loaded Blazor before Monaco, and even in the right order Monaco defines its global a little after its script runs. BlazorMonaco silently skips creating the editor when that global is missing, so about one cold page load in four ended with no editor and a dead circuit. Blazor is now started from the Monaco loader's ready callback.
7. **Docker hygiene.** Every `docker build --target test` left an untagged 1.75 GB image, which filled the Docker disk during this phase. Tests now normally run through `scripts/test-in-docker.sh`, which copies the sources into a throwaway container and leaves nothing behind. The Dockerfile test stage remains, documented with `--output type=cacheonly`, and now fails on a skipped test or after five minutes instead of hanging. The Docker stages build the test project, not the solution, so the browser test project stays out of the image.
8. **A heavy-allocation test runs alone.** Under amd64 emulation, the existing million-key test stalled itself and every process-starting test for about 20 seconds when run beside them. It is in its own non-parallel collection.
9. **Changes after the independent review.**
   - *A run no longer depends on its pipes closing.* A program can leave a child behind that keeps the pipes open (Emojicode can run shell commands). The run now ends a quarter of a second after the program itself has gone, whatever still holds the pipes, and Stop, the time limit and the output cap work in that situation. Where `setsid` exists the program gets its own process group and the whole group is ended with the run.
   - *The compiler counts characters on line 1 from zero* and on every later line from one. Diagnostics are normalised to one-based on all lines when they are parsed. The "Facts" table above was written from probes on line 2 and missed this.
   - *File names are restricted to characters that mean nothing to a shell.* The compiler builds its link command from the entry file's name and runs it through a shell, so a name such as `a;touch x;.🍇` would have been a command.
   - *Closing a session between two events no longer logs a false failure.*
   - *A compiler that exits with 0 without producing a program is a failed build.* The compiler does not check its linker's result.
   - Smaller ones: each toolchain instance works in its own folder under the work root; a senseless time limit is replaced instead of throwing after the program has started; diagnostics are cleared when a run starts and when a file is loaded; a Stop that lands while the program is starting is honoured; the page re-renders only when the run status changes; the dropped-lines note shares the status row; Blazor still starts if Monaco fails to load.
10. **A frozen test process under emulation.** Twice in about thirty Docker test runs the .NET test process froze completely: every thread parked, no child processes, no test started, unresponsive even to the diagnostics tools. That is below .NET's own time limit, so the Docker test commands now run under a 15 minute `timeout` and fail instead of hanging. The cause was not found; it has only been seen under Rosetta on Apple Silicon.

