# Phase 0: Baseline

- **Date:** 2026-10-06
- **Branch:** `phase-0-baseline`, from `main` at `fb0a3d8`
- **Status:** awaiting Tom's review

## Context

Blazemoji is being overhauled in six phases so that Grapevine-scale Emojicode can be written, built, run and understood in it. Phase 0 adds no features and leaves the runner as it is. It moves the app onto current platforms and makes "does it still work" something a command can answer, so that Phases 1 to 5 start from a known-good base.

## Goal

Upgrade to .NET 10, current MudBlazor and current BlazorMonaco, and fix what breaks.

Done when:

1. The solution builds on .NET 10 with no errors.
2. The tests pass in Docker on `linux/amd64`, including new tests that compile and run every terminating sample with the real compiler.
3. The app runs from its container and a sample runs end to end in the browser, shown with screenshots.

## Facts checked before writing this

All checked on 2026-10-06 on Tom's Mac (Docker Desktop, arm64 host).

| Fact | Consequence |
| --- | --- |
| `mcr.microsoft.com/dotnet/sdk:10.0` for `linux/amd64` builds and runs a console app under emulation in 13 s. | Running `dotnet test` in an amd64 container is workable. |
| The .NET 10 images are Ubuntu 24.04, which has no `libtinfo5` or `libncurses5` package. | The current Dockerfile's `apt-get install libncurses5` will fail. |
| `ldd` shows the compiler needs only `libtinfo.so.5` beyond a standard C++ runtime. | Install just jammy's `libtinfo5` deb, plus `g++` for linking. |
| `Blazemoji/emojicodec/emojicodec` is committed with mode `100644`. | On a Linux or macOS checkout it is not executable, so a container built today cannot compile anything. |
| The bundled compiler and `packages/*.a` are byte-identical to the Emojicode 1.0 beta 2 release. | No toolchain change is needed. |
| The compiler finds packages in `./packages` relative to the working directory. | The existing runner keeps working as long as the working directory is the app folder. Tests must respect that. |
| On the stock toolchain all 10 samples compile; 9 exit 0; `InfiniteLoop` runs until killed. | This is the "samples still run" baseline. |
| MudBlazor 9.11.0, BlazorMonaco 3.5.0 and Blazored.LocalStorage 4.5.0 all support `net10.0`. | No package blocks the upgrade. |
| In MudBlazor 9, `MudPicker<T>` still exposes `PickerContent` and `Render` for derived pickers. | `EmojiPicker` can be ported in place. |

## Decisions

### 1. Target versions

| Package | From | To |
| --- | --- | --- |
| Target framework (both projects) | `net8.0` | `net10.0` |
| MudBlazor | 6.11.1 | 9.11.0 |
| BlazorMonaco | 3.1.0 | 3.5.0 |
| Blazored.LocalStorage | 4.4.0 | 4.5.0 |
| Microsoft.VisualStudio.Azure.Containers.Tools.Targets | 1.19.5 | 1.23.0 |
| System.Collections.Concurrent | 4.3.0 | removed, it is part of the framework |
| xunit | 2.4.2 | replaced by xunit.v3 4.0.1 |
| xunit.runner.visualstudio | 2.4.5 | 4.0.0 |
| Microsoft.NET.Test.Sdk | 17.5.0 | 18.10.1 |
| coverlet.collector | 3.2.0 | 10.1.0 |
| Moq | 4.20.69 | removed, no test uses it |
| Shouldly | none | 4.3.0 |

A `global.json` pins the SDK to `10.0.100` with `rollForward: latestFeature`. Without it this machine picks the .NET 11 RC SDK.

.NET 10 rather than .NET 11: the brief says 10, it is the current LTS, and 11 is still a release candidate. Tom's other repos are on `net11.0`; a `net10.0` library can be consumed from a `net11.0` host, so this does not block the Hermes host later.

NSubstitute is added in the phase that writes the first mock, not here.

### 2. MudBlazor 6 to 9 in one hop

Go straight to 9.11.0 and fix compile errors, reading the source in `~/Code/MudBlazor` for each changed API. The alternative, stepping through 7 and 8, means fixing some call sites twice in an app of about 1,500 lines.

Each file gets the smallest change that restores its current behaviour. Changes already identified:

| File | Change |
| --- | --- |
| `Layout/Theme.cs` | `Palette` becomes `PaletteLight`. |
| `Layout/MainLayout.razor` | Add `MudPopoverProvider`. `GetSystemPreference` becomes `GetSystemDarkModeAsync`. |
| `KeycommandsDialog`, `SaveFileDialog`, `DestructiveDialog` | `MudDialogInstance` becomes `IMudDialogInstance`. |
| `Pages/Home.razor` | Synchronous `IDialogService.Show` is gone, use `ShowAsync`. `DisableUnderLine` becomes `Underline="false"`. |
| `Components/EmojiPicker/*` | Converter moves from the constructor to a `GetDefaultConverter()` override. `DisableToolbar` becomes `ShowToolbar`. `Submit`/`Close` become their async forms. |
| `Components/EmojiToolbox.razor` | `MudList` is now `MudList<T>`, and `Clickable` becomes `ReadOnly`. |
| `Components/Library.razor` | `MudTreeView` and `MudTreeViewItem` parameter changes. |
| `Emoji.razor`, `CopyToClipboard.razor` | `MudIconButton` no longer has a `Title` parameter. Confirm the value still reaches the button as a `title` attribute. |

The compiler is the authority on the full list; this table is the expected shape, not a limit.

Files edited in this phase are saved without a byte-order mark. Nothing else about them is tidied.

### 3. Docker

Rewrite `Blazemoji/Dockerfile` as one multi-stage file, every stage `linux/amd64`:

| Stage | Base | Purpose |
| --- | --- | --- |
| `build` | `sdk:10.0` plus toolchain dependencies | Restore and build the solution. |
| `test` | `build` | `dotnet test`. Built with `docker build --target test`. |
| `publish` | `build` | `dotnet publish` of the web app. |
| `final` | `aspnet:10.0` plus toolchain dependencies | The runnable app. This is the default target. |

Toolchain dependencies are `g++` and jammy's `libtinfo5` deb, fetched from the Ubuntu archive by exact URL and verified against a SHA-256 written in the repo. One small script installs them, used by both the `build` and `final` stages so the two cannot drift.

The alternative was a script that bind-mounts the repo into a stock SDK container. Rejected: it mixes arm64 and amd64 `bin`/`obj` output in the working tree and is slower under emulation.

The build context moves to the repo root so the test project is included. The exact commands go in the README.

### 4. Compiler executable bit

Set `Blazemoji/emojicodec/emojicodec` to mode `100755` in git. This is the one fix in Phase 0 that is not caused by the upgrade, and it is here because the container cannot compile without it.

### 5. Tests

Move the test project to xunit.v3 and Shouldly, converting the three existing tests' assertions.

Add `SampleProgramsTests`, which runs each sample through the existing `ICompilerService` with the real compiler:

- `HelloWorld` must succeed and print `Hello World!`.
- The other eight terminating samples must succeed and produce output.
- `InfiniteLoop` is left out. Its current behaviour is a 30 s timeout, which Phase 1 replaces.

These tests need `linux/amd64`. They carry a `Toolchain` trait and skip themselves on any other platform, so `dotnet test` on macOS stays green and the Docker `test` stage runs everything.

They exercise the runner exactly as it is today. They are the regression net for Phase 1, which rewrites that runner.

### 6. README

Replace the Prerequisites and Setup sections, which still describe the Aspire, RabbitMQ and `Blazemoji.Compiler` layout removed in `ac024c6`, with the .NET 10 and Docker commands that now work. Nothing else in the README changes.

## Verification

Reported to Tom with output and screenshots:

1. `docker build --target test` passes, with the test summary showing the sample tests ran and were not skipped.
2. `dotnet test` on macOS passes, with the sample tests reported as skipped.
3. The `final` image runs, and a Playwright script against it (kept out of the repo for now; the Playwright test project arrives in Phase 1) captures:
   - the page in light and dark mode,
   - the toolbox and the emoji picker open, and an emoji inserted,
   - a sample loaded from the Library and run, with its output shown,
   - the save dialog and the key commands dialog.
4. The same screens captured before any upgrade edit (this branch at its first commit is `main` plus this document), side by side with the new ones, to catch visual regressions from the three-major MudBlazor jump.
5. The build's warning list, with any warning the upgrade introduced either fixed or explained.

## Out of scope

- The five runner bugs. `CompilerService` logic is untouched; Phase 1 owns it.
- Bringing existing code in line with the Mythetech patterns (State classes, icon constants, scoped CSS in place of inline styles, hiding `exception.Message`). New code follows them; existing code is brought across as later phases rewrite it.
- Mythetech.Framework as a dependency. To be raised in the Phase 1 spec, where state and messaging first matter.
- `deploy.sh` and `Dockerfile.original`. Left as they are. Note that `deploy.sh` installs `libncurses5`, which will not work on an Ubuntu 24.04 host.
- The duplicate `Blazemoji/Blazemoji.sln`, the unused Bootstrap and Open Iconic assets, and repo-wide BOM removal.
- CI. There is no workflow today and this phase does not add one.

## Risks

| Risk | Handling |
| --- | --- |
| `EmojiPicker` derives from `MudPicker<string>` and leans on its internals, which changed across three majors. | Port in place first. If the popover behaviour cannot be restored that way, stop and bring Tom a proposal to rebuild it on `MudPopover` with the same public surface (`OpenAsync`, `OnEmojiPicked`). |
| Visual drift from MudBlazor 9 defaults (spacing, typography, elevation). | Before and after screenshots. Differences are listed for Tom, not silently restyled. |
| Breakage the compiler cannot see: the framework and BlazorMonaco script tags in `App.razor`, static asset paths, and JS interop between .NET 8 and 10. | The browser run in Verification step 3 is what catches these. A page that loads without console errors is part of the evidence. |
| .NET under amd64 emulation is slow, and Docker Desktop has 6 GB of memory. | The probe built and ran in 13 s. If the full test stage proves flaky, report it with the failure before changing approach. |
| The jammy `libtinfo5` deb URL moves. | Checksum pinning makes a change fail loudly at build time, not at run time. |

## Branching

Work happens on `phase-0-baseline` in the main checkout and is pushed when done. No merge and no pull request. Phase 1 branches from `phase-0-baseline` unless it has been merged to `main` by then.
