# Phase 0 Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move Blazemoji to .NET 10, MudBlazor 9.11.0 and BlazorMonaco 3.5.0 with no feature changes, and make "does it still work" answerable by one Docker command.

**Architecture:** One atomic upgrade of both projects, then a multi-stage `linux/amd64` Dockerfile whose `test` stage runs every terminating sample through the real Emojicode compiler. A Playwright script kept outside the repo captures the same screens before and after.

**Tech Stack:** .NET 10 SDK, Blazor Server, MudBlazor 9.11.0, BlazorMonaco 3.5.0, xunit.v3 on Microsoft.Testing.Platform, Shouldly, Docker (`linux/amd64`), Playwright (Node) for evidence only.

**Spec:** `docs/superpowers/specs/2026-10-06-phase-0-baseline-design.md`

Every edit in this plan was first tried in a throwaway copy outside the repo on 2026-10-06. The expected outputs below are what that copy produced.

## Global Constraints

- Branch `phase-0-baseline`. Never commit to `main`, never merge, never open a pull request.
- No co-author trailer in commit messages.
- Target framework `net10.0` for both projects. SDK pinned to `10.0.100` with `rollForward: latestFeature`.
- Package versions exactly: MudBlazor 9.11.0, BlazorMonaco 3.5.0, Blazored.LocalStorage 4.5.0, Microsoft.VisualStudio.Azure.Containers.Tools.Targets 1.23.0, xunit.v3 4.0.1, xunit.runner.visualstudio 4.0.0, Microsoft.NET.Test.Sdk 18.10.1, coverlet.collector 10.1.0, Shouldly 4.3.0. Remove System.Collections.Concurrent and Moq.
- Every Docker stage is `linux/amd64`.
- `Blazemoji/Services/Compiler/*` is not edited. Phase 1 owns the runner.
- Each file gets the smallest change that restores its current behaviour. No tidying, no restyling.
- Every file edited in this phase is saved without a UTF-8 byte-order mark.
- No em dashes in any text, comment or copy.
- `deploy.sh`, `Dockerfile.original` and `Blazemoji/Blazemoji.sln` are not touched.
- Dependency source is read from `~/Code/MudBlazor` and `~/Code/BlazorMonaco`, never the NuGet cache.

## Review Focus

1. **The published container must serve `_framework/blazor.server.js`.** In .NET 10 that script comes from a NuGet package the SDK references only when it sees `.razor` files at restore time. A Dockerfile that restores before copying sources silently drops it, and the page renders but never becomes interactive. Pinned by the `publish` stage guard in Task 3 and by the browser run.
2. **Saving a file and loading it back from the Library.** Blazored.LocalStorage 4.5.0 changed nullability on the calls behind this flow and nothing else covers it. Pinned by smoke step `08-save-and-reload`.
3. **Dismissing a dialog with Escape.** In MudBlazor 9 `IDialogReference.Result` yields a nullable `DialogResult`. Pinned by smoke step `06-save-dialog`, which closes with Escape, not Cancel.
4. **A fresh clone on Linux or macOS must be able to execute the compiler.** The binary is committed without its executable bit. Pinned by the Docker `test` stage in Task 2.
5. **Tests that pass because they did nothing.** A theory with no rows, or samples missing from the test output, would look green. Pinned by `Samples_are_copied_next_to_the_tests`, which runs on every platform.

---

### Task 1: Upgrade both projects to .NET 10 and current packages

The two projects share a target framework through a project reference, so they move together.

**Files:**
- Create: `global.json`
- Modify: `Blazemoji/Blazemoji.csproj`
- Modify: `Blazemoji/Layout/Theme.cs`
- Modify: `Blazemoji/Layout/MainLayout.razor`
- Modify: `Blazemoji/Pages/Home.razor`
- Modify: `Blazemoji/Components/Sidebar.razor`
- Modify: `Blazemoji/Components/Library.razor`
- Modify: `Blazemoji/Components/EmojiToolbox.razor`
- Modify: `Blazemoji/Components/Emoji.razor`
- Modify: `Blazemoji/Components/CopyToClipboard.razor`
- Modify: `Blazemoji/Components/KeycommandsDialog.razor`
- Modify: `Blazemoji/Components/SaveFileDialog.razor`
- Modify: `Blazemoji/Shared/Components/DestructiveDialog.razor`
- Modify: `Blazemoji/Components/EmojiPicker/EmojiPicker.razor`
- Modify: `Blazemoji/Components/EmojiPicker/EmojiPicker.razor.cs`
- Modify: `Blazemoji/Services/Library/LibraryService.cs`
- Modify: `Blazemoji.Test/Blazemoji.Test.csproj`
- Modify: `Blazemoji.Test/GlobalUsings.cs`
- Modify: `Blazemoji.Test/EmojiStringGeneratorTests.cs`
- Modify: `Blazemoji.Test/EmojiTypeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: a solution that builds on `net10.0`; the test command `dotnet test --solution Blazemoji.sln`; `EmojiPicker.OpenAsync()` (inherited) in place of `Open()`.

- [ ] **Step 1: Pin the SDK and the test runner**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

The `test` section is required: on the .NET 10 SDK, xunit.v3 refuses to run under the old VSTest target. Tom's other repos use the same setting.

- [ ] **Step 2: Retarget the web project**

In `Blazemoji/Blazemoji.csproj`, replace the first `PropertyGroup` and the `PackageReference` item group with:

```xml
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UserSecretsId>61e20a36-a065-4170-94c7-4feac9405451</UserSecretsId>
    <PublishAot>False</PublishAot>
    <DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>
  </PropertyGroup>
```

```xml
  <ItemGroup>
    <PackageReference Include="Blazored.LocalStorage" Version="4.5.0" />
    <PackageReference Include="BlazorMonaco" Version="3.5.0" />
    <PackageReference Include="Microsoft.VisualStudio.Azure.Containers.Tools.Targets" Version="1.23.0" />
    <PackageReference Include="MudBlazor" Version="9.11.0" />
  </ItemGroup>
```

`RequiresAspNetWebAssets` is deliberately not added yet. Task 3 adds it after watching its guard fail.

- [ ] **Step 3: Replace the test project file**

Write `Blazemoji.Test/Blazemoji.Test.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputType>Exe</OutputType>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
    <PackageReference Include="xunit.v3" Version="4.0.1" />
    <PackageReference Include="xunit.runner.visualstudio" Version="4.0.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="10.1.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Blazemoji\Blazemoji.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Build and confirm the expected failures**

Run: `dotnet build Blazemoji/Blazemoji.csproj 2>&1 | grep -E ': error ' | sed -E 's# \[[^]]*\]$##' | sort -u`

Expected: errors in exactly these places (the Razor ones show first; the C# ones appear once those are fixed):

- `EmojiPicker.razor.cs`: CS0534, `GetDefaultConverter()` not implemented
- `EmojiToolbox.razor`: RZ10001 twice, `MudList` and `MudListItem` need `T`
- `KeycommandsDialog.razor`, `SaveFileDialog.razor`, `DestructiveDialog.razor`: CS0246, `MudDialogInstance` not found
- `MainLayout.razor`: CS1061, `GetSystemPreference`
- `Theme.cs`: CS0118, `Palette`
- `Home.razor`: CS1955 (`Open` is not a method) and CS1061 (`IDialogService.Show`)

If a file outside this list fails, read the changed API in `~/Code/MudBlazor/src/MudBlazor` and apply the smallest equivalent change.

- [ ] **Step 5: Port the theme and layout**

`Blazemoji/Layout/Theme.cs`: change `Palette = new PaletteLight()` to `PaletteLight = new PaletteLight()`.

`Blazemoji/Layout/MainLayout.razor`: change

```razor
<MudThemeProvider @ref="_mudThemeProvider" @bind-IsDarkMode="@_isDarkMode" Theme="_theme"/>
<MudDialogProvider/>
```

to

```razor
<MudThemeProvider @ref="_mudThemeProvider" @bind-IsDarkMode="@_isDarkMode" Theme="_theme"/>
<MudPopoverProvider/>
<MudDialogProvider/>
```

and `await _mudThemeProvider.GetSystemPreference()` to `await _mudThemeProvider.GetSystemDarkModeAsync()`.

- [ ] **Step 6: Port the three dialogs**

In each of `Blazemoji/Components/KeycommandsDialog.razor`, `Blazemoji/Components/SaveFileDialog.razor` and `Blazemoji/Shared/Components/DestructiveDialog.razor`, change

```csharp
    [CascadingParameter] MudDialogInstance MudDialog { get; set; }
```

to

```csharp
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;
```

- [ ] **Step 7: Port the emoji picker**

`Blazemoji/Components/EmojiPicker/EmojiPicker.razor.cs`, full class body after the port:

```csharp
    public partial class EmojiPicker : MudPicker<string>
    {
        public EmojiPicker()
        {
            AdornmentIcon = Icons.Material.Outlined.EmojiEmotions;
            AdornmentAriaLabel = "Open Emoji Picker";
        }

        [Parameter]
        public EventCallback<string> OnEmojiPicked { get; set; }

        protected override IConverter<string?, string?> GetDefaultConverter() => new DefaultConverter<string>();

        protected async Task OnEmojiSelectedAsync(EmojicodeKeyword emoji)
        {
            await CloseAsync(PickerActions == null);
            await OnEmojiPicked.InvokeAsync(emoji.Emoji);
        }

        protected string ToolbarClassname =>
        new CssBuilder("mud-picker-timepicker-toolbar")
          .AddClass("mud-width-full")
          .AddClass($"mud-picker-timepicker-toolbar-landscape", Orientation == Orientation.Landscape && PickerVariant == PickerVariant.Static)
          .AddClass(ToolbarClass)
        .Build();

    }
```

The `using` lines and namespace stay as they are. What changed and why:

- The converter no longer goes through the base constructor. The old `OnGet` and `OnSet` were identity functions, so `DefaultConverter<string>` is equivalent.
- `MudPicker<T>` now has its own `ToolbarClass` parameter, so the computed property is renamed `ToolbarClassname` and folds the parameter in.
- `Submit()` and `Close()` are gone. `CloseAsync(submit)` does both; passing `PickerActions == null` keeps the old "submit only when there are no action buttons" rule.
- `OnEmojiPicked.InvokeAsync` is now awaited.

`Blazemoji/Components/EmojiPicker/EmojiPicker.razor`: change

```razor
        <MudPickerToolbar Class="@ToolbarClass" Style="@Style" DisableToolbar="@DisableToolbar" Orientation="@Orientation" PickerVariant="@PickerVariant" Color="@Color">
```

to

```razor
        <MudPickerToolbar Class="@ToolbarClassname" Style="@Style" ShowToolbar="@ShowToolbar" Orientation="@Orientation" PickerVariant="@PickerVariant" Color="@Color">
```

and `@onclick="(() => OnEmojiSelected(emoji))"` to `@onclick="(() => OnEmojiSelectedAsync(emoji))"`.

- [ ] **Step 8: Port the remaining components**

`Blazemoji/Components/EmojiToolbox.razor`:
- `<MudList Clickable="false" Style=` becomes `<MudList T="string" ReadOnly="true" Style=`
- `<MudListItem><h6>` becomes `<MudListItem T="string"><h6>`

`Blazemoji/Components/Sidebar.razor`: `PanelClass="pa-2"` becomes `TabPanelsClass="pa-2"`.

`Blazemoji/Components/CopyToClipboard.razor`: `<MudIconButton Title="Copy"` becomes `<MudIconButton title="Copy"`.

`Blazemoji/Components/Emoji.razor`: `<MudIconButton Title="Insert"` becomes `<MudIconButton title="Insert"`.

`Blazemoji/Pages/Home.razor`:
- `DisableUnderLine="true"` becomes `Underline="false"`
- `if(result.Canceled)` becomes `if (result is null || result.Canceled)`
- `Name = result.Data.ToString() ?? "Untitled.🍇",` becomes `Name = result.Data?.ToString() ?? "Untitled.🍇",`
- the body of `OpenEmojiPickerAsync` becomes the single line `await _emojiPicker.OpenAsync();`
- `_dialogService.Show<KeycommandsDialog>("Editor Keycommands", options);` becomes `await _dialogService.ShowAsync<KeycommandsDialog>("Editor Keycommands", options);`

`Blazemoji/Components/Library.razor`: `if(dialog.Canceled)` becomes `if (dialog is null || dialog.Canceled)`.

`Blazemoji/Services/Library/LibraryService.cs`: in `GetUserSavedFiles`, `Code = code,` becomes `Code = code ?? string.Empty,`.

The three null checks answer warnings the new package versions introduce (`DialogResult?` in MudBlazor 9, `string?` from Blazored.LocalStorage 4.5).

- [ ] **Step 9: Build clean**

Run:

```bash
dotnet build Blazemoji/Blazemoji.csproj --no-incremental 2>&1 \
  | grep -oE '[A-Za-z0-9_./]+\.(cs|razor)\([0-9,]+\): (warning|error) [A-Z]+[0-9]+' \
  | sed -E 's#.*/Blazemoji/Blazemoji/##; s#\([0-9,]+\)##' | sort -u | awk '{print $NF}' | sort | uniq -c
```

Expected: no errors, and only these warnings, all present before the upgrade:

```
   1 CS0414
   5 CS8618
   1 CS8625
```

Any `MUD0002` warning means a removed MudBlazor parameter is still in use. Its message names the replacement; apply it.

- [ ] **Step 10: Move the tests to xunit.v3 and Shouldly**

Write `Blazemoji.Test/GlobalUsings.cs`:

```csharp
global using Xunit;
global using Shouldly;
global using Blazemoji.Emojicode;
global using Blazemoji.Emojicode.Keywords;
```

`Blazemoji.Test/EmojiStringGeneratorTests.cs`:
- `Assert.Matches(regex, emojiString);` becomes `emojiString.ShouldMatch(regex.ToString());`
- `Assert.False(collision);` becomes `collision.ShouldBeFalse();`

`Blazemoji.Test/EmojiTypeTests.cs`:

```csharp
            emoji.Name.ShouldBe(expectedName);
            emoji.Emoji.ShouldBe(expectedEmoji);
            emoji.Keyword.ShouldBe(expectedKeyword);
```

replaces the three `Assert.Equal` lines.

- [ ] **Step 11: Run the tests**

Run: `dotnet test --solution Blazemoji.sln 2>&1 | tail -8`

Expected:

```
Test run summary: Passed!
  total: 17
  failed: 0
  succeeded: 17
  skipped: 0
```

- [ ] **Step 12: Strip byte-order marks from every file this task edited**

```bash
for f in Blazemoji/Layout/Theme.cs Blazemoji/Layout/MainLayout.razor Blazemoji/Pages/Home.razor \
         Blazemoji/Components/Sidebar.razor Blazemoji/Components/Library.razor \
         Blazemoji/Components/EmojiToolbox.razor Blazemoji/Components/Emoji.razor \
         Blazemoji/Components/CopyToClipboard.razor Blazemoji/Components/KeycommandsDialog.razor \
         Blazemoji/Components/SaveFileDialog.razor Blazemoji/Shared/Components/DestructiveDialog.razor \
         Blazemoji/Components/EmojiPicker/EmojiPicker.razor Blazemoji/Components/EmojiPicker/EmojiPicker.razor.cs \
         Blazemoji/Services/Library/LibraryService.cs \
         Blazemoji.Test/EmojiStringGeneratorTests.cs Blazemoji.Test/EmojiTypeTests.cs; do
  while [ "$(head -c 3 "$f" | xxd -p)" = "efbbbf" ]; do tail -c +4 "$f" > "$f.nobom" && mv "$f.nobom" "$f"; done
done
git diff --stat
```

`EmojiPicker.razor` starts with two marks, hence the loop. Rebuild and rerun Step 11 to confirm nothing changed.

- [ ] **Step 13: Commit**

```bash
git add global.json Blazemoji/Blazemoji.csproj Blazemoji.Test Blazemoji/Layout Blazemoji/Pages/Home.razor \
        Blazemoji/Components Blazemoji/Shared/Components/DestructiveDialog.razor Blazemoji/Services/Library/LibraryService.cs
git commit -m "Upgrade to .NET 10, MudBlazor 9.11 and BlazorMonaco 3.5"
```

---

### Task 2: Run the samples through the real compiler in Docker

**Files:**
- Create: `Blazemoji.Test/SampleProgramsTests.cs`
- Create: `docker/install-toolchain-deps.sh`
- Modify: `Blazemoji/Dockerfile` (rewritten; `build` and `test` stages in this task)
- Modify: `Blazemoji/emojicodec/emojicodec` (file mode only)

**Interfaces:**
- Consumes: `ICompilerService.CompileAndExecuteAsync(string code)` returning `EmojicodeResult` with `Error`, `Message`, `Result` (existing, unchanged).
- Produces: `docker build -f Blazemoji/Dockerfile --target test .` as the test command; `docker/install-toolchain-deps.sh` for any image that needs the compiler; the `build` stage that Task 3 extends.

- [ ] **Step 1: Write the sample tests**

Create `Blazemoji.Test/SampleProgramsTests.cs`:

```csharp
using System.Runtime.InteropServices;
using Blazemoji.Contracts.Models;
using Blazemoji.Services.Compiler;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blazemoji.Test
{
    [Trait("Category", "Toolchain")]
    public class SampleProgramsTests
    {
        private const string SkipReason = "The Emojicode compiler only runs on linux/amd64. Run the Docker test stage.";
        private const string NonTerminatingSample = "InfiniteLoop.🍇";

        private static readonly string SamplesDirectory = Path.Combine(AppContext.BaseDirectory, "Emojicode", "Samples");

        private static bool ToolchainAvailable =>
            OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

        public SampleProgramsTests()
        {
            // CompilerService resolves emojicodec, ./packages and its output file from the working directory.
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        }

        public static TheoryData<string> TerminatingSamples()
        {
            var samples = new TheoryData<string>();
            foreach (var path in Directory.EnumerateFiles(SamplesDirectory).Order())
            {
                var name = Path.GetFileName(path);
                if (name != NonTerminatingSample)
                    samples.Add(name);
            }

            return samples;
        }

        [Fact]
        public void Samples_are_copied_next_to_the_tests()
        {
            var shipped = Directory.EnumerateFiles(SamplesDirectory).Select(Path.GetFileName).ToList();

            shipped.Count.ShouldBeGreaterThanOrEqualTo(10);
            shipped.ShouldContain("HelloWorld.🍇");
            shipped.ShouldContain(NonTerminatingSample);
        }

        [Theory]
        [MemberData(nameof(TerminatingSamples))]
        public async Task Sample_compiles_and_runs_with_output(string sampleFile)
        {
            Assert.SkipUnless(ToolchainAvailable, SkipReason);

            var result = await RunSampleAsync(sampleFile);

            result.Error.ShouldBeFalse(result.Message);
            result.Result.ShouldNotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task HelloWorld_prints_its_greeting()
        {
            Assert.SkipUnless(ToolchainAvailable, SkipReason);

            var result = await RunSampleAsync("HelloWorld.🍇");

            result.Error.ShouldBeFalse(result.Message);
            result.Result.ShouldContain("Hello World!");
        }

        private static async Task<EmojicodeResult> RunSampleAsync(string sampleFile)
        {
            var code = await File.ReadAllTextAsync(Path.Combine(SamplesDirectory, sampleFile));
            var compiler = new CompilerService(NullLogger<CompilerService>.Instance);

            return await compiler.CompileAndExecuteAsync(code);
        }
    }
}
```

The samples reach the test output folder through the project reference: `Blazemoji.csproj` marks them, the compiler and the packages as `CopyToOutputDirectory`.

- [ ] **Step 2: Run on macOS and see the toolchain tests skip**

Run: `dotnet test --solution Blazemoji.sln 2>&1 | tail -8`

Expected:

```
Test run summary: Passed!
  total: 28
  failed: 0
  succeeded: 18
  skipped: 10
```

- [ ] **Step 3: Write the dependency installer**

Create `docker/install-toolchain-deps.sh` and make it executable (`chmod +x`):

```sh
#!/bin/sh
# Installs what the prebuilt Emojicode 1.0 beta 2 compiler needs on Ubuntu 24.04:
# a C++ toolchain, because emojicodec shells out to c++ to link, and libtinfo.so.5,
# which Ubuntu stopped packaging after 22.04 (jammy).
#
# The jammy release-pocket build is used because its URL never changes. Builds from
# jammy-updates are deleted from the pool whenever a newer one supersedes them.
set -eu

LIBTINFO5_URL="http://archive.ubuntu.com/ubuntu/pool/universe/n/ncurses/libtinfo5_6.3-2_amd64.deb"
LIBTINFO5_SHA256="d2597b5aec92a930cf549e1b429ad892595813e72ec7814685ea146a9fb715e5"

apt-get update
apt-get install -y --no-install-recommends g++ curl ca-certificates

curl -fsSL "$LIBTINFO5_URL" -o /tmp/libtinfo5.deb
echo "$LIBTINFO5_SHA256  /tmp/libtinfo5.deb" | sha256sum -c -
dpkg -i /tmp/libtinfo5.deb

rm -f /tmp/libtinfo5.deb
rm -rf /var/lib/apt/lists/*
```

The checksum matches the one in jammy's signed package index.

- [ ] **Step 4: Rewrite the Dockerfile with the build and test stages**

Write `Blazemoji/Dockerfile`:

```dockerfile
# check=skip=FromPlatformFlagConstDisallowed
# Emojicode ships x86_64 Linux binaries only, so every stage is linux/amd64.
# Build from the repository root:
#   docker build -f Blazemoji/Dockerfile --target test .     run the tests
#   docker build -f Blazemoji/Dockerfile -t blazemoji .      build the app image

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
COPY docker/install-toolchain-deps.sh /tmp/install-toolchain-deps.sh
RUN /tmp/install-toolchain-deps.sh
WORKDIR /src
COPY global.json Blazemoji.sln ./
COPY Blazemoji/Blazemoji.csproj Blazemoji/
COPY Blazemoji.Test/Blazemoji.Test.csproj Blazemoji.Test/
RUN dotnet restore Blazemoji.sln
COPY Blazemoji/ Blazemoji/
COPY Blazemoji.Test/ Blazemoji.Test/
RUN dotnet build Blazemoji.sln -c $BUILD_CONFIGURATION --no-restore

FROM build AS test
RUN dotnet test --solution Blazemoji.sln -c $BUILD_CONFIGURATION --no-build
```

- [ ] **Step 5: Run the test stage and watch the compiler tests fail**

Run: `docker build -f Blazemoji/Dockerfile --target test --progress=plain . 2>&1 | grep -E 'Permission denied|Test run summary|  (total|failed|succeeded|skipped):' | sed -E 's/^#[0-9]+ [0-9.]+ //' | sort | uniq -c`

Expected: the build fails, with ten `Permission denied` lines and

```
Test run summary: Failed!
  total: 28
  failed: 10
  succeeded: 18
  skipped: 0
```

The compiler is committed with mode `100644`, so Linux will not execute it.

- [ ] **Step 6: Make the compiler executable**

```bash
chmod +x Blazemoji/emojicodec/emojicodec
git update-index --chmod=+x Blazemoji/emojicodec/emojicodec
git ls-files -s Blazemoji/emojicodec/emojicodec
```

Expected: the last command prints a line starting `100755`.

- [ ] **Step 7: Run the test stage and see it pass**

Run: `docker build -f Blazemoji/Dockerfile --target test --progress=plain . 2>&1 | grep -E 'Test run summary|  (total|failed|succeeded|skipped):' | sed -E 's/^#[0-9]+ [0-9.]+ //'`

Expected:

```
Test run summary: Passed!
  total: 28
  failed: 0
  succeeded: 28
  skipped: 0
```

- [ ] **Step 8: Commit**

```bash
git add Blazemoji.Test/SampleProgramsTests.cs docker/install-toolchain-deps.sh Blazemoji/Dockerfile Blazemoji/emojicodec/emojicodec
git commit -m "Run the samples through the real compiler in a Docker test stage"
```

---

### Task 3: Build an app image that actually becomes interactive

**Files:**
- Modify: `Blazemoji/Dockerfile` (add `publish` and `final` stages)
- Modify: `Blazemoji/Blazemoji.csproj` (add `RequiresAspNetWebAssets`)

**Interfaces:**
- Consumes: the `build` stage and `docker/install-toolchain-deps.sh` from Task 2.
- Produces: `docker build -f Blazemoji/Dockerfile -t blazemoji .` giving an image that listens on port 8080.

- [ ] **Step 1: Add the publish and final stages, with a guard for the framework script**

Append to `Blazemoji/Dockerfile`:

```dockerfile

FROM build AS publish
RUN dotnet publish Blazemoji/Blazemoji.csproj -c $BUILD_CONFIGURATION --no-build -o /app/publish /p:UseAppHost=false
# Without this file the page renders but never becomes interactive.
RUN test -f /app/publish/wwwroot/_framework/blazor.server.js

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0 AS final
COPY docker/install-toolchain-deps.sh /tmp/install-toolchain-deps.sh
RUN /tmp/install-toolchain-deps.sh
# The runner still writes its compiled binary into the working directory, so the
# app user has to own /app. Phase 1 moves that output to a per-run temp directory.
USER $APP_UID
WORKDIR /app
COPY --from=publish --chown=$APP_UID /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Blazemoji.dll"]
```

- [ ] **Step 2: Build the image and watch the guard fail**

Run: `docker build -f Blazemoji/Dockerfile -t blazemoji . 2>&1 | tail -5`

Expected: the build fails at `RUN test -f /app/publish/wwwroot/_framework/blazor.server.js` with exit code 1.

Why: `Microsoft.NET.Sdk.Web` sets `RequiresAspNetWebAssets` only when the project's `Content` items include a `.razor` file. The Dockerfile restores when only the `.csproj` files have been copied, so the package that carries the Blazor scripts (`Microsoft.AspNetCore.App.Internal.Assets`) is never restored.

- [ ] **Step 3: State the requirement in the project file**

In `Blazemoji/Blazemoji.csproj`, add inside the first `PropertyGroup`, after `DockerDefaultTargetOS`:

```xml
    <!-- In .NET 10 the Blazor framework scripts come from a NuGet package that the SDK only
         references when it sees .razor files at restore time. The Dockerfile restores before it
         copies the sources, so the requirement is stated here. -->
    <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
```

- [ ] **Step 4: Build the image and see it pass**

Run: `docker build -f Blazemoji/Dockerfile -t blazemoji . 2>&1 | tail -3`

Expected: the build succeeds with no Dockerfile lint warnings.

- [ ] **Step 5: Check the container serves the script and can compile as the app user**

```bash
docker rm -f blazemoji >/dev/null 2>&1
docker run -d --rm --name blazemoji --platform linux/amd64 -p 127.0.0.1:5080:8080 blazemoji
until [ "$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5080/)" = "200" ]; do sleep 1; done
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5080/_framework/blazor.server.js
docker exec blazemoji sh -c 'id -un; printf "🏁 🍇\n  😀 🔤from the app image🔤❗️\n🍉\n" > /tmp/t.🍇 && ./emojicodec/emojicodec /tmp/t.🍇 -o /app/probe.o && ./probe.o && rm probe.o'
```

Expected: `200`, then `app`, then `from the app image`. Leave the container running for Task 5.

- [ ] **Step 6: Rerun the test stage**

Run the command from Task 2 Step 7. Expected: 28 passed, 0 skipped.

- [ ] **Step 7: Commit**

```bash
git add Blazemoji/Dockerfile Blazemoji/Blazemoji.csproj
git commit -m "Build an app image and keep the Blazor framework script in it"
```

---

### Task 4: README and dead configuration

**Files:**
- Modify: `README.md` (Prerequisites, Installation, Setup and Running Tests sections only)
- Modify: `Blazemoji/appsettings.json`

**Interfaces:**
- Consumes: the Docker commands from Tasks 2 and 3.
- Produces: nothing other tasks use.

- [ ] **Step 1: Confirm the connection string is unused**

Run: `grep -rnI "AzureServiceBus\|GetConnectionString" --include='*.cs' --include='*.razor' . | grep -v '/bin/\|/obj/'`

Expected: no output.

- [ ] **Step 2: Remove it**

Write `Blazemoji/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

This removes an Azure Service Bus connection string, including its shared access key, left over from the messaging-based compiler that commit `ac024c6` removed. Deleting the line does not remove it from git history or revoke the key; that is reported to Tom.

- [ ] **Step 3: Replace the stale README sections**

In `README.md`, replace everything from the `## Prerquisites` heading up to, but not including, the `## Emojicode` heading with:

````markdown
## Prerequisites
- .NET 10 SDK
- Docker, for anything that compiles or runs Emojicode

Emojicode's only usable release (1.0 beta 2) ships x86_64 Linux binaries, so the compiler runs in a `linux/amd64` container. On Apple Silicon, Docker Desktop emulates it.

## Run the app

```bash
docker build -f Blazemoji/Dockerfile -t blazemoji .
docker run --rm -p 5080:8080 blazemoji
```

Then open http://localhost:5080.

`dotnet run --project Blazemoji` also starts the app and is fine for working on the UI, but Run Code only works where the bundled compiler can execute: x86_64 Linux with `g++` and `libtinfo5` installed.

## Running Tests

```bash
docker build -f Blazemoji/Dockerfile --target test .
```

This builds the solution and runs every test, including the ones that compile and run the samples with the real compiler.

```bash
dotnet test --solution Blazemoji.sln
```

runs the same tests on your machine. The compiler tests skip themselves anywhere other than x86_64 Linux.

````

- [ ] **Step 4: Check the app still starts with the trimmed settings**

Run: `dotnet build Blazemoji/Blazemoji.csproj 2>&1 | grep -E 'Build succeeded|error'` then `dotnet test --solution Blazemoji.sln 2>&1 | tail -6`

Expected: `Build succeeded.` and 18 succeeded, 10 skipped.

- [ ] **Step 5: Commit**

```bash
git add README.md Blazemoji/appsettings.json
git commit -m "Describe the .NET 10 and Docker workflow; drop unused Service Bus setting"
```

---

### Task 5: Evidence

Nothing in this task is committed. `$EVIDENCE` is a directory outside the repository (the agent's scratchpad).

**Files:**
- Create: `$EVIDENCE/smoke/package.json`, `$EVIDENCE/smoke/smoke.mjs`

**Interfaces:**
- Consumes: the `blazemoji` image from Task 3.
- Produces: `$EVIDENCE/smoke/before/*.png`, `$EVIDENCE/smoke/after/*.png`, and the warning comparison, for the report to Tom.

- [ ] **Step 1: Set up the smoke script**

`$EVIDENCE/smoke/package.json`:

```json
{ "name": "blazemoji-smoke", "private": true, "type": "module", "dependencies": { "playwright": "1.56.1" } }
```

`$EVIDENCE/smoke/smoke.mjs`:

```javascript
// Drives Blazemoji in a headless browser and saves one screenshot per screen.
//   node smoke.mjs <base-url> <output-dir>
// Exits non-zero if a step fails or the page logs a console error.
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';

const [baseUrl, outDir] = process.argv.slice(2);
if (!baseUrl || !outDir) {
  console.error('usage: node smoke.mjs <base-url> <output-dir>');
  process.exit(2);
}
mkdirSync(outDir, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1600, height: 900 }, colorScheme: 'light' });

const problems = [];
page.on('console', (message) => {
  if (message.type() === 'error') problems.push(`console error: ${message.text()}`);
});
page.on('pageerror', (error) => problems.push(`page error: ${error.message}`));

// Popovers and dialogs fade in; wait so the screenshot shows the settled state.
const shot = async (name) => {
  await page.waitForTimeout(500);
  await page.screenshot({ path: join(outDir, `${name}.png`) });
};
const editorToolbar = page.locator('.mud-toolbar', { hasText: 'Emojicode Editor' });
const toolbarIconButtons = editorToolbar.locator('button.mud-icon-button:not([aria-label="Open Emoji Picker"])');
const editorText = page.locator('.monaco-editor .view-lines');
const output = page.locator('h4:has-text("Output") + pre');
const dialog = page.locator('.mud-dialog');

async function step(name, action) {
  try {
    await action();
    console.log(`ok    ${name}`);
  } catch (error) {
    problems.push(`${name}: ${error.message.split('\n')[0]}`);
    console.log(`FAIL  ${name}`);
    await shot(`FAILED-${name}`);
  }
}

await step('01-light', async () => {
  await page.goto(baseUrl);
  await editorText.getByText('Hello World!').waitFor({ timeout: 60_000 });
  await shot('01-light');
});

await step('02-dark', async () => {
  const themeToggle = page.locator('header button.mud-icon-button').first();
  await themeToggle.click();
  await page.locator('.monaco-editor.vs-dark').waitFor();
  await shot('02-dark');
  await page.locator('header button.mud-icon-button').first().click();
  await page.locator('.monaco-editor.vs-dark').waitFor({ state: 'detached' });
});

await step('03-emoji-picker', async () => {
  await page.getByLabel('Open Emoji Picker').click();
  const firstEmoji = page.locator('.mud-popover-open .emoji-box-hover').first();
  await firstEmoji.waitFor();
  await shot('03-emoji-picker');
  const emoji = (await firstEmoji.innerText()).trim();
  await firstEmoji.click();
  await page.locator('.mud-popover-open .emoji-box-hover').first().waitFor({ state: 'detached' });
  await editorText.getByText(emoji).first().waitFor();
  await shot('04-emoji-inserted');
});

await step('05-sample-run', async () => {
  await page.locator('.mud-tab', { hasText: 'Library' }).click();
  await page.locator('.mud-treeview-item', { hasText: 'HelloWorld' }).last().click();
  await page.getByRole('button', { name: 'Run Code' }).click();
  await output.getByText('Hello World!').waitFor({ timeout: 90_000 });
  await shot('05-sample-run');
});

await step('06-save-dialog', async () => {
  await toolbarIconButtons.nth(1).click();
  await dialog.getByText('Enter File Name').waitFor();
  await shot('06-save-dialog');
  await dialog.locator('input').first().click();
  await page.keyboard.press('Escape');
  await dialog.waitFor({ state: 'detached' });
});

await step('07-keycommands-dialog', async () => {
  await toolbarIconButtons.nth(0).click();
  const closeButton = dialog.locator('.mud-dialog-actions').getByRole('button', { name: 'Close' });
  await closeButton.waitFor();
  await shot('07-keycommands-dialog');
  await closeButton.click();
  await dialog.waitFor({ state: 'detached' });
});

await step('08-save-and-reload', async () => {
  await toolbarIconButtons.nth(1).click();
  await dialog.locator('input').first().fill('SmokeSaved');
  await dialog.getByRole('button', { name: 'Ok' }).click();
  await dialog.waitFor({ state: 'detached' });
  const savedFile = page.locator('.mud-treeview-item', { hasText: 'SmokeSaved.🍇' }).last();
  await savedFile.waitFor();
  await page.locator('.mud-treeview-item', { hasText: 'Fizzbuzz' }).last().click();
  await editorText.getByText('Hello World!').waitFor({ state: 'detached' });
  await savedFile.click();
  await editorText.getByText('Hello World!').waitFor();
  await shot('08-save-and-reload');
});

await step('09-toolbox', async () => {
  await page.locator('.mud-tab', { hasText: 'Toolbox' }).click();
  await page.locator('.emoji-box-hover').first().waitFor();
  await shot('09-toolbox');
});

await browser.close();

if (problems.length > 0) {
  console.error(`\n${problems.length} problem(s):`);
  for (const problem of problems) console.error(`  ${problem}`);
  process.exit(1);
}
console.log('\nsmoke run clean');
```

Install: `cd "$EVIDENCE/smoke" && npm install --no-audit --no-fund && npx playwright install chromium`

- [ ] **Step 2: Capture the before screens from `main`**

Skip this step if `$EVIDENCE/smoke/before/` already holds nine screenshots from the planning session.

```bash
rm -rf "$EVIDENCE/before-src" && mkdir -p "$EVIDENCE/before-src"
git archive main | tar -x -C "$EVIDENCE/before-src"
( cd "$EVIDENCE/before-src" && docker build --platform linux/amd64 -f Blazemoji/Dockerfile -t blazemoji-before --progress=plain . > build.log 2>&1 )
docker run -d --rm --name blazemoji-before --platform linux/amd64 --user root --entrypoint sh \
  -p 127.0.0.1:5081:8080 blazemoji-before -c 'chmod +x emojicodec/emojicodec && exec dotnet Blazemoji.dll'
until [ "$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5081/)" = "200" ]; do sleep 1; done
( cd "$EVIDENCE/smoke" && node smoke.mjs http://127.0.0.1:5081 "$EVIDENCE/smoke/before" )
docker rm -f blazemoji-before && docker image rm blazemoji-before
```

The `chmod` at start-up works around the missing executable bit on `main`, so the before image can run a sample at all.

Expected: `smoke run clean`.

- [ ] **Step 3: Capture the after screens from the branch**

With the `blazemoji` container from Task 3 Step 5 still running on port 5080 (rebuild and restart it if any file changed since):

```bash
( cd "$EVIDENCE/smoke" && rm -rf after && node smoke.mjs http://127.0.0.1:5080 "$EVIDENCE/smoke/after" )
docker rm -f blazemoji
```

Expected: `smoke run clean`, nine screenshots.

- [ ] **Step 4: Compare warnings before and after**

```bash
norm() { grep -oE '[A-Za-z0-9_./]+\.(cs|razor)\([0-9,]+\): warning [A-Z]+[0-9]+' "$1" | sed -E 's#^/src/##; s#^Blazemoji/##; s#\([0-9,]+\)##' | sort -u; }
docker build -f Blazemoji/Dockerfile --target build --no-cache-filter build --progress=plain . > "$EVIDENCE/after-build.log" 2>&1
norm "$EVIDENCE/before-src/build.log" > "$EVIDENCE/warn-before.txt"
norm "$EVIDENCE/after-build.log" > "$EVIDENCE/warn-after.txt"
echo "only after:"; comm -13 "$EVIDENCE/warn-before.txt" "$EVIDENCE/warn-after.txt"
echo "only before:"; comm -23 "$EVIDENCE/warn-before.txt" "$EVIDENCE/warn-after.txt"
```

Expected: nothing under "only after" for the web project. The test project contributes none.

- [ ] **Step 5: Look at every before and after pair**

Open each pair and list every visible difference for Tom. Differences are reported, not restyled away.

- [ ] **Step 6: Final full run and push**

```bash
docker build -f Blazemoji/Dockerfile --target test --progress=plain . 2>&1 | grep -E 'Test run summary|  (total|failed|succeeded|skipped):' | sed -E 's/^#[0-9]+ [0-9.]+ //'
dotnet test --solution Blazemoji.sln 2>&1 | tail -6
git status --short
git push -u origin phase-0-baseline
```

Expected: 28 passed and 0 skipped in Docker; 18 passed and 10 skipped on macOS; a clean tree apart from the untracked `.cerberus/`.

If the weekday 9am to 5pm EST pre-push hook blocks the push, do not bypass it (the repo is public). Report the unpushed branch.
