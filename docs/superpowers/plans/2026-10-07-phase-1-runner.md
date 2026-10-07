# Phase 1 Runner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the runner with a toolchain library that decides success by exit code, isolates every build and run in its own directory, streams output live, and can be stopped; show compiler diagnostics as editor markers and a problems list.

**Architecture:** A new plain class library, `Blazemoji.Toolchain`, holds the contract (`IToolchain`) and a process-based `LocalToolchain`. The web app talks to it only through a scoped `RunState`, and components render from that state. The contract mirrors the HTTP service Phase 2 will add.

**Tech Stack:** .NET 10, `System.Threading.Channels`, `System.Text.Json`, MudBlazor 9.11.0, BlazorMonaco 3.5.0, xunit.v3, Shouldly, NSubstitute 6.2.0, bUnit 2.11.3, Microsoft.Playwright 1.63.0.

**Spec:** `docs/superpowers/specs/2026-10-07-phase-1-runner-design.md`

**Plan format, by ruling:** Tom delegated the choice of plan on 2026-10-06 and is not reviewing before execution. The planner is also the executor, in the same session, under TDD. This plan therefore fixes every interface, file, test case and command, and gives code where a value or a subtle behaviour must be exact, but it does not pre-write every method body. Cost if wrong: an executor with no context would need the spec open beside it.

## Global Constraints

- Branch `phase-1-runner`, stacked on `phase-0-baseline`. Never commit to `main`, never merge to `main`, never open a pull request. No co-author trailer.
- `Blazemoji.Toolchain` references no Blazor, MudBlazor or web project.
- Compile always uses `--json`. Success is `exit code == 0`. Never infer failure from stderr.
- Nothing is written to the app folder at run time. All work happens under `ToolchainOptions.WorkRoot`.
- No fire-and-forget. No `async void`. No `.Result`, `.Wait()`, `Task.Run` in components. No `ConfigureAwait(false)`. No `Channel<object>`.
- Never show `exception.Message` to the user. Log it; show a fixed message.
- New UI: icons through `BlazemojiIcons`, scoped `.razor.css`, no inline styles, `EventCallback` parameters, no layout shift when run state changes.
- Events follow `{Subject}Changed`. No public `Notify` method on a State class.
- No em dashes in any text, comment or copy. Files saved without a byte-order mark.
- Real compiler tests carry `[Trait("Category", "Toolchain")]` and call `Assert.SkipUnless(ToolchainFixture.Available, ...)`.
- Test commands: `dotnet test --solution Blazemoji.sln` (macOS) and `docker build -f Blazemoji/Dockerfile --target test .` (real compiler).

## Review Focus

1. **A burst of output followed by silence.** A server prints two lines in the same millisecond and then waits for requests. Both lines must appear. Pinned by `RunState` trailing-flush test and the toolchain streaming test.
2. **Emoji before the error on the same line.** The marker must land on the offending token, not two columns short per emoji. Pinned by position mapping tests.
3. **Closing the browser tab mid-run.** The process must die and its directories must go. Pinned by `RunState` dispose test and toolchain dispose test.
4. **A program that never prints a newline, or prints megabytes with none.** Memory and the line tail must stay bounded. Pinned by the output assembler test for an over-long line.
5. **Clicking Run twice, or Run while running.** The second request must not start a second process or corrupt the first run's state. Pinned by a `RunState` test.

---

### Task 1: Toolchain project, contract, diagnostics parsing

**Files:**
- Create: `Blazemoji.Toolchain/Blazemoji.Toolchain.csproj`
- Create: `Blazemoji.Toolchain/IToolchain.cs` (both interfaces)
- Create: `Blazemoji.Toolchain/Contracts.cs` (`CompileRequest`, `CompileResult`, `Diagnostic`, `DiagnosticSeverity`, `RunRequest`, `RunEvent` and its three subtypes, `RunEndReason`)
- Create: `Blazemoji.Toolchain/ToolchainOptions.cs`
- Create: `Blazemoji.Toolchain/DiagnosticsParser.cs`
- Create: `Blazemoji.Toolchain/SourceFileNames.cs`
- Create: `Blazemoji.Test/Toolchain/DiagnosticsParserTests.cs`, `Blazemoji.Test/Toolchain/SourceFileNamesTests.cs`
- Modify: `Blazemoji.sln`, `Blazemoji.Test/Blazemoji.Test.csproj` (project reference, NSubstitute 6.2.0, bunit 2.11.3), `Blazemoji/Dockerfile` (copy the new csproj before restore, copy its sources)

**Interfaces:**
- Produces: the contract exactly as written in the spec, section 1.
- Produces: `static IReadOnlyList<Diagnostic> DiagnosticsParser.Parse(string compilerStdout)`; throws `FormatException` when the text is not a JSON array of diagnostics.
- Produces: `static bool SourceFileNames.IsSafe(string name)`.
- Produces: `ToolchainOptions` with `SectionName = "Toolchain"` and the defaults in the spec table.

- [ ] **Step 1: tests first.** `DiagnosticsParserTests`, one test per case, using these exact compiler outputs:
  - `[]` gives an empty list.
  - `[{"type":"error","line":2,"character":5,"file":"a.🍇","message":"Variable \"nope\" not defined."}]` gives one `Error` at 2:5 in `a.🍇` with the unescaped message.
  - `[{"type":"warning","line":0,"character":0,"file":"","message":"Run-time type information for multiprotocols, callables and type values is not available yet. Casts and other reflection may not behave as expected with these types."}]` gives one `Warning` at 0:0 with an empty file.
  - the two-error output from an unbalanced block (`line 3 character 1` then `line 4 character 0`) gives two errors in order.
  - an unknown `type` value is treated as `Error`.
  - `not json`, `{}` and an empty string each throw `FormatException`.
- [ ] **Step 2:** `SourceFileNamesTests`: `main.🍇` and `lib/util.🍇` are safe; empty, whitespace, `/etc/passwd`, `../x.🍇`, `a/../../x.🍇`, `a\..\x.🍇`, a name containing a NUL, and `C:\x.🍇` are not.
- [ ] **Step 3:** run `dotnet test --solution Blazemoji.sln`. Expected: build fails, the types do not exist.
- [ ] **Step 4:** create the project, the contract, the options, the parser and the validator. Run again. Expected: all pass, the 10 toolchain tests still skip on macOS.
- [ ] **Step 5:** `docker build -f Blazemoji/Dockerfile --target test .` Expected: pass, 0 skipped.
- [ ] **Step 6:** commit "Add the toolchain contract and diagnostics parser".

---

### Task 2: Compile in an isolated build directory

**Files:**
- Create: `Blazemoji.Toolchain/LocalToolchain.cs`
- Create: `Blazemoji.Toolchain/ProcessRunner.cs` (starts a process, reads both pipes concurrently, enforces a timeout; internal)
- Create: `Blazemoji.Test/Toolchain/ToolchainFixture.cs`, `Blazemoji.Test/Toolchain/Programs.cs`, `Blazemoji.Test/Toolchain/LocalToolchainCompileTests.cs`

**Interfaces:**
- Consumes: Task 1's contract, parser, validator, options.
- Produces: `LocalToolchain(IOptions<ToolchainOptions> options, ILogger<LocalToolchain> logger) : IToolchain, IAsyncDisposable`. `StartRunAsync` throws `NotImplementedException` until Task 3.
- Produces for tests: `ToolchainFixture.Available` (Linux x64), `ToolchainFixture.Create(Action<ToolchainOptions>? configure = null)` returning a toolchain rooted in a fresh temp `WorkRoot` with `CompilerPath` and `PackagesPath` under `AppContext.BaseDirectory`, and `ToolchainFixture.WorkRootOf(toolchain)`.
- Produces for tests: `Programs` constants: `Hello`, `UndefinedVariable` (error at 2:5), `RttiWarning`, `PrintsMarker(string marker)`, and for Task 3 `ExitsWith(int code)`, `SlowTwoLines`, `OneMegabyte`, `Crashes`, `Forever`.

`RttiWarning` (a list of escaping closures makes the compiler emit the warning; confirm the exact text compiles during the red step and adjust only the program, never the assertion):

```
🏁 🍇
  🆕🍨🐚🍇🔢➡️🔢🍉🍆❗️ ➡️ 🖍🆕 fs
  🐻 fs 🍇🎍🥡 a 🔢 ➡️ 🔢
    ↩️ a ➕ 1
  🍉❗️
  😀 🔤stored🔤❗️
🍉
```

- [ ] **Step 1: tests first** (`LocalToolchainCompileTests`, all Toolchain-trait):
  - `Compile_of_a_valid_program_is_ok_and_returns_a_build_id`: build directory exists and contains `program`.
  - `Compile_with_a_compiler_warning_is_still_ok`: `RttiWarning` gives `Ok`, a non-null build id, and exactly one `Warning` whose message starts "Run-time type information".
  - `Compile_error_returns_the_diagnostic_and_no_build`: `Ok` false, `BuildId` null, one `Error` at line 2 character 5, and no directory left under `<WorkRoot>/builds`.
  - `Compile_rejects_unsafe_file_names`: a file named `../evil.🍇` gives `Ok` false with one error diagnostic and writes nothing outside `WorkRoot`.
  - `Compile_rejects_an_entry_that_is_not_among_the_files`.
  - `Concurrent_compiles_get_separate_builds`: eight parallel compiles of `PrintsMarker(i)` give eight distinct build ids, all `Ok`.
  - `ReleaseBuild_deletes_the_build_directory`, and releasing an unknown id does nothing.
  - `Stale_builds_are_swept_on_the_next_compile`: with `BuildLifetime = TimeSpan.Zero`, a second compile removes the first build's directory.
  - `Compile_times_out`: with `CompileTimeout` of 1 ms the result is `Ok` false with a fixed-message error.
- [ ] **Step 2:** run the Docker test stage. Expected: these fail (type missing or `NotImplementedException`).
- [ ] **Step 3:** implement. Compiler arguments, in order: `<entry> --json -o <buildDir>/program -S <PackagesPath>`; working directory is the build directory; UTF-8 for both pipes. Build ids and run ids are `Guid.NewGuid().ToString("N")`.
- [ ] **Step 4:** Docker test stage passes with 0 skipped; macOS run passes with the new tests skipped.
- [ ] **Step 5:** commit "Compile in an isolated build directory and report by exit code".

---

### Task 3: Run with live events, stop, timeout and an output cap

**Files:**
- Create: `Blazemoji.Toolchain/LocalRun.cs` (`IToolchainRun`)
- Create: `Blazemoji.Toolchain/SurrogateSafeReader.cs` (reads a `StreamReader` in chunks, never ending a chunk on a high surrogate)
- Modify: `Blazemoji.Toolchain/LocalToolchain.cs`
- Create: `Blazemoji.Test/Toolchain/LocalToolchainRunTests.cs`, `Blazemoji.Test/Toolchain/SurrogateSafeReaderTests.cs`
- Modify: `Blazemoji.Test/SampleProgramsTests.cs` (rewritten against `IToolchain`; `InfiniteLoop` now included and expected to end by output limit)

**Interfaces:**
- Consumes: Task 2's `LocalToolchain`, fixture and programs.
- Produces: `LocalToolchain.StartRunAsync`. Event guarantees: zero or more `StdoutEvent`/`StderrEvent`, then exactly one `ExitEvent`, then the stream completes. `ReadEventsAsync` supports one reader.
- Produces for tests: `static Task<(string Stdout, string Stderr, ExitEvent Exit)> RunToEndAsync(this IToolchainRun run)` in the test project.

- [ ] **Step 1: tests first.**
  - `SurrogateSafeReaderTests` (any platform): a stream of `😀` repeated, read with a buffer size that would split pairs, yields chunks whose concatenation equals the input and none of which ends in a high surrogate; an empty stream yields nothing.
  - `Run_reports_output_and_exit_code_zero` (`Hello`).
  - `Run_reports_a_non_zero_exit_code` (`ExitsWith(3)` gives `ExitCode == 3`, reason `Exited`).
  - `Run_with_more_than_a_megabyte_of_output_completes` (`OneMegabyte`: 20,000 lines of 64 characters; stdout length at least 1,000,000; reason `Exited`).
  - `Output_arrives_while_the_program_is_still_running` (`SlowTwoLines` prints `one`, sleeps 1.5 s, prints `two`): the first `StdoutEvent` is received at least 1 s before the `ExitEvent`.
  - `Stop_ends_a_running_program` (`Forever`: a loop that sleeps): `StopAsync` leads to an `ExitEvent` with reason `Stopped` within 5 s.
  - `Run_is_ended_at_the_timeout`: `RunRequest.Timeout` of 500 ms on `Forever` gives reason `TimedOut`.
  - `Run_is_ended_at_the_output_limit`: `MaxOutputBytes = 65_536` on the `InfiniteLoop` sample gives reason `OutputLimit`, and total text received is under 2 x the limit.
  - `A_crash_is_reported_with_stderr_and_a_failing_exit` (`Crashes` unwraps an empty optional): stderr is not empty, reason `Exited`, exit code is not 0.
  - `Concurrent_runs_each_get_their_own_output`: eight parallel compile-and-run calls of `PrintsMarker(i)`, each stdout equals its own marker line.
  - `Disposing_a_run_removes_its_directory_and_stops_the_process`.
  - `Run_of_an_unknown_build_fails_to_start` (reason `FailedToStart`, no exception).
  - `Environment_variables_reach_the_program` (a program that prints `🌳🐇💻 🔤BLAZEMOJI_TEST🔤❗️`).
- [ ] **Step 2:** Docker test stage. Expected: the new tests fail.
- [ ] **Step 3:** implement. `stdbuf` is resolved once by searching `PATH`. Channel capacity 256, `BoundedChannelFullMode.Wait`, single reader. Output bytes are counted as UTF-8 bytes of the text published. Ending a run: `Process.Kill(entireProcessTree: true)`, guarded for an already-exited process.
- [ ] **Step 4:** rewrite `SampleProgramsTests` to use the fixture. Docker test stage passes with 0 skipped; macOS passes.
- [ ] **Step 5:** commit "Run programs with live events, stop, timeout and an output cap".

---

### Task 4: Map compiler positions to editor ranges

**Files:**
- Create: `Blazemoji/Emojicode/EditorRange.cs`, `Blazemoji/Emojicode/DiagnosticPositions.cs`
- Create: `Blazemoji.Test/DiagnosticPositionsTests.cs`
- Modify: `Blazemoji/Blazemoji.csproj` (reference `Blazemoji.Toolchain`)

**Interfaces:**
- Produces: `readonly record struct EditorRange(int StartLine, int StartColumn, int EndLine, int EndColumn)` (1-based, UTF-16 columns).
- Produces: `static EditorRange? DiagnosticPositions.ToEditorRange(Diagnostic diagnostic, string source)`; null when `Line < 1`.

- [ ] **Step 1: tests first.**
  - `  😀 nope❗️` on line 2, character 5: start column 6 (the emoji is two UTF-16 units), end column 10 (`nope`).
  - `nope` at line start, character 1: columns 1 to 5.
  - two emoji before the name (`😀😀 nope`, character 4): start column 6.
  - a variation-selector emoji before the name (`▶️ nope`: two code points, two UTF-16 units; character 4): start column 4.
  - character 0 on a real line: range covers column 1 to 2.
  - line past the end of the file: clamped to the last line, column 1.
  - character past the end of the line: clamped to the end of the line, at least one column wide.
  - line 0: null.
  - CRLF source: the carriage return is not part of the range.
- [ ] **Step 2:** run; expected fail. Implement; run; pass.
- [ ] **Step 3:** commit "Map compiler positions to editor ranges".

---

### Task 5: `RunState`

**Files:**
- Create: `Blazemoji/Shared/State/RunState.cs`, `Blazemoji/Shared/State/OutputLine.cs`, `Blazemoji/Shared/State/RunStatus.cs`, `Blazemoji/Shared/State/RunSummary.cs`, `Blazemoji/Shared/State/OutputAssembler.cs`
- Create: `Blazemoji.Test/State/RunStateTests.cs`, `Blazemoji.Test/State/OutputAssemblerTests.cs`, `Blazemoji.Test/State/ScriptedRun.cs`

**Interfaces:**
- Consumes: `IToolchain`.
- Produces:

```csharp
public enum RunStatus { Idle, Compiling, Running }
public enum OutputStream { Stdout, Stderr, System }
public sealed record OutputLine(long Number, OutputStream Stream, string Text);
public sealed record RunSummary(int? ExitCode, RunEndReason Reason, TimeSpan Duration);

public sealed class RunState : IAsyncDisposable
{
    public const int MaxLines = 5000;
    public const int MaxLineLength = 4000;
    public RunState(IToolchain toolchain, ILogger<RunState> logger, TimeProvider timeProvider);
    public RunStatus Status { get; }
    public IReadOnlyList<OutputLine> Lines { get; }
    public long TotalLines { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public string DiagnosticsSource { get; }      // the code the diagnostics refer to
    public RunSummary? LastRun { get; }
    public event Action? StateChanged;
    public event Action? DiagnosticsChanged;
    public Task RunAsync(string code);
    public Task StopAsync();
}
```

- `OutputAssembler` turns text chunks into completed lines: splits on `\n`, drops a trailing `\r`, keeps the unfinished remainder, and cuts a line that passes `MaxLineLength` so that it is emitted in pieces.
- Notification rule: after an event, raise `StateChanged` if at least 50 ms have passed since the last one; otherwise wait for either the next event or the remainder of the 50 ms, whichever comes first, and raise it then. Always raise on status change and on exit.
- A second `RunAsync` while not `Idle` returns immediately.
- `System` lines carry fixed messages: "The run could not be started.", "Stopped.", "Stopped after reaching the time limit.", "Stopped after reaching the output limit.".

- [ ] **Step 1: tests first.** `OutputAssemblerTests`: single chunk with two lines; a line split across chunks; `\r\n`; no trailing newline until `Flush`; an over-long line is emitted in `MaxLineLength` pieces; empty chunk. `RunStateTests` with NSubstitute for `IToolchain` and `ScriptedRun` (channel-backed `IToolchainRun`):
  - compile error: status returns to `Idle`, diagnostics set, `StartRunAsync` never called, `DiagnosticsChanged` raised once.
  - successful run: status goes `Compiling`, `Running`, `Idle`; lines numbered from 1; `LastRun` holds the exit event's values; `ReleaseBuildAsync` called once with the build id.
  - stderr chunk produces `Stderr` lines.
  - more than `MaxLines` lines: `Lines.Count == MaxLines`, `TotalLines` is the true count, first kept line number is `TotalLines - MaxLines + 1`.
  - burst then silence: two chunks 1 ms apart, then nothing; with a `FakeTimeProvider` advanced by 50 ms, `StateChanged` has fired and both lines are visible, before any further event.
  - `StopAsync` calls the run's `StopAsync`; with no run it does nothing.
  - `RunAsync` while running does not call `CompileAsync` again.
  - toolchain throws: status returns to `Idle`, one `System` line with the fixed message, the exception text appears nowhere in `Lines`.
  - `DisposeAsync` during a run stops and disposes it.
  - each run starts with empty lines and no summary.
- [ ] **Step 2:** run; expected fail. Implement; run; pass.
- [ ] **Step 3:** commit "Add RunState to own compile, run and output".

`FakeTimeProvider` comes from `Microsoft.Extensions.TimeProvider.Testing` (add the current stable version to the test project).

---

### Task 6: Wire the UI and remove the old runner

**Files:**
- Create: `Blazemoji/Emojicode/BlazemojiIcons.cs`
- Modify: `Blazemoji/Components/OutputPanel.razor`; create `OutputPanel.razor.css`
- Create: `Blazemoji/Components/ProblemsPanel.razor`, `ProblemsPanel.razor.css`
- Modify: `Blazemoji/Components/EmojiCodeEditor.razor`, `Blazemoji/Pages/Home.razor`, `Blazemoji/Program.cs`, `Blazemoji/Dockerfile`, `Blazemoji/_Imports.razor`
- Delete: `Blazemoji/Services/Compiler/*`
- Create: `Blazemoji.Test/Components/OutputPanelTests.cs`, `Blazemoji.Test/Components/ProblemsPanelTests.cs`

**Interfaces:**
- Consumes: `RunState`, `DiagnosticPositions`.
- Produces: `EmojiCodeEditor.SetMarkersAsync(IReadOnlyList<Diagnostic> diagnostics, string source)`, `ClearMarkersAsync()`, `RevealAsync(int line, int column)`.
- Produces: `ProblemsPanel` parameter `EventCallback<Diagnostic> ProblemSelected`.
- Produces DOM hooks the browser tests rely on: `data-testid="output-line"`, `data-testid="run-status"`, `data-testid="problem"`, `data-testid="stop-button"`, `data-testid="run-button"`, and the tab labels "Output" and "Problems".

- [ ] **Step 1: bUnit tests first.** `OutputPanel` shows one element per line in order, marks stderr lines with the error class, shows "Running" while running, shows "Exited with code 3" after such a run, and shows the dropped-lines note when `TotalLines > Lines.Count`. `ProblemsPanel` shows one row per diagnostic with "line:character", shows an empty-state message when there are none, and raises `ProblemSelected` with the clicked diagnostic.
- [ ] **Step 2:** run; expected fail. Implement the components.
- [ ] **Step 3:** wire `Home.razor`: Run calls `RunState.RunAsync(code)`; Stop calls `RunState.StopAsync()` and is disabled unless running; on `DiagnosticsChanged` the page applies markers and, if there are errors, selects the Problems tab; choosing a problem reveals it. Loading a file clears markers. Remove the overlay spinner.
- [ ] **Step 4:** `Program.cs`: bind `ToolchainOptions`, register `IToolchain` as singleton `LocalToolchain`, `RunState` as scoped, `TimeProvider.System` as singleton. Delete the old services and their registrations.
- [ ] **Step 5:** Dockerfile `final` stage: drop `--chown` and the comment about `/app` ownership.
- [ ] **Step 6:** macOS tests pass; Docker test stage passes; build has no new warnings.
- [ ] **Step 7:** commit "Stream output, show problems, add Stop; remove the old runner".

---

### Task 7: Browser tests

**Files:**
- Create: `Blazemoji.E2E/Blazemoji.E2E.csproj`, `Blazemoji.E2E/BrowserFixture.cs`, `Blazemoji.E2E/EditorPage.cs`, `Blazemoji.E2E/RunnerFlowsTests.cs`, `Blazemoji.E2E/ShellFlowsTests.cs`
- Create: `scripts/e2e.sh`
- Modify: `Blazemoji.sln`, `Blazemoji/Dockerfile` (the new csproj must be present for restore; the `test` stage runs `--project Blazemoji.Test/Blazemoji.Test.csproj` so browser tests are not counted there), `README.md` (one paragraph on the browser tests)

**Interfaces:**
- Consumes: the `data-testid` hooks from Task 6; environment variable `BLAZEMOJI_BASE_URL`.
- Produces: `scripts/e2e.sh`, which builds the image, starts it on a free local port, runs `dotnet test --project Blazemoji.E2E` with the base URL, and removes the container even on failure.

Tests skip when `BLAZEMOJI_BASE_URL` is not set. `BrowserFixture` installs Chromium through `Microsoft.Playwright.Program.Main(["install", "chromium"])` once.

- [ ] **Step 1: tests first**, then run `scripts/e2e.sh` and watch each fail for the right reason before the fix it needs (most pass once Task 6 is in; a failing test here is a finding about Task 6 and is fixed there, test first).
  - `Running_a_sample_shows_its_output_and_exit_status`.
  - `Output_appears_before_the_program_ends`: type the slow program; after Run, "one" is visible while the Stop button is enabled and "two" is not yet visible.
  - `Stop_ends_an_endless_program`: load `InfiniteLoop`, Run, Stop; the status says stopped and Run is enabled again within 10 s.
  - `A_compile_error_shows_a_marker_and_a_problem`: a squiggle element exists in the editor on the right line, the Problems tab shows the message, and clicking it moves the cursor there.
  - `A_warning_build_still_runs`: the RTTI program runs, prints, and the Problems tab lists the warning.
  - `Two_sessions_running_at_once_keep_their_own_output`: two browser contexts, different marker programs, started together.
  - Phase 0 flows: theme toggle, emoji picker insert, save dialog closed with Escape, key commands dialog, save and reload from the Library.
- [ ] **Step 2:** commit "Add browser tests for the runner and shell flows".

---

### Task 8: Evidence and push

- [ ] Docker test stage, macOS tests, `scripts/e2e.sh`: record the three summaries.
- [ ] Screenshots through the browser tests (`EditorPage.ScreenshotAsync`) of: output mid-run with Stop enabled, a stopped run, the 1 MB run finished, a compile error with squiggle and Problems tab, the warning build.
- [ ] Update the report page.
- [ ] `git push -u origin phase-1-runner`. If the weekday 9am to 5pm EST hook blocks it, do not bypass; report it.
