# Hosting the Blazemoji editor

The editor is two libraries, and a host puts it on screen. There are two hosts.

| Project | What it is |
| --- | --- |
| `Blazemoji.Core` | What Blazemoji knows about Emojicode and about projects, with no UI: the keyword catalog, the code intelligence, the project model, keeping projects on disk, and the sample programs and project templates themselves. |
| `Blazemoji.Components` | The editor as Razor components, the state behind them, and the title bar and theme every host shows it in. |
| `Blazemoji` | The web host: a Blazor Server app that shows the editor and keeps projects in the browser. |
| `Blazemoji.Desktop` | The desktop host: the editor in a window of its own, with projects kept as folders on disk. |

A host references `Blazemoji.Components` and does what the two hosts do in their `Program.cs` and page shell (`Blazemoji/Components/App.razor`, `Blazemoji.Desktop/wwwroot/index.html`). This page lists it, and then says what is particular to the desktop host.

## The one setting: where the toolchain service is

The editor never compiles or runs anything itself. It talks to the toolchain service over HTTP, and the address is one setting:

```json
{ "ToolchainClient": { "BaseUrl": "http://localhost:5290" } }
```

The web host's containers set it to `http://toolchain:8080`. The desktop host leaves it at the default, `http://localhost:5290`, which is where `scripts/dev-toolchain.sh` puts the service, and can be pointed at one running somewhere else. Nothing else in the editor knows where code is built. `ToolchainClient:RequestTimeout` (two minutes by default) is how long one request may take.

## Services to register

```csharp
// The same for every host.
services.AddMudServices();
services.AddToolchainClient(configuration);   // reads ToolchainClient:BaseUrl
services.AddBlazemojiEditor(configuration);   // catalog, code intelligence, templates, samples, state

// What only the host can supply.
services.AddScoped<IProjectStore, YourProjectStore>();
services.AddTransient<ILibraryService, YourLibraryService>();
```

The types are in `MudBlazor.Services`, `Blazemoji.Toolchain.Http`, `Blazemoji` (for `AddBlazemojiEditor`), `Blazemoji.Services.Projects` and `Blazemoji.Services.Library`.

A host with a disk of its own makes one call in place of the last two lines, before `AddBlazemojiEditor`:

```csharp
services.AddBlazemojiProjectsOnDisk(configuration);   // reads Projects:Root
```

| The host supplies | For | The web host | The desktop host |
| --- | --- | --- | --- |
| `IProjectStore` | Where projects are kept between sessions. | `LocalStorageProjectStore`: the browser's local storage. | `FileProjectStore`: a folder for each project. |
| `ILibraryService` | What is saved from the editor as a file of its own, listed in the Library tab. | `LibraryService`: local storage. | `FileLibraryService`: a `Snippets` folder beside the projects. |
| `IExternalLinks` (optional) | Opening the links in the title bar. Without one they are ordinary links that open in a new tab. | None. | `HermesExternalLinks`: the system's browser, since a link followed in the app's own window would take the editor away. |
| `"Projects": { "Root": "..." }` in configuration (optional) | With `AddBlazemojiProjectsOnDisk`, the folder projects are kept under. Defaults to `Blazemoji` in the user's documents. A leading `~/` is the user's home. | Not used. | The default. |
| `"ProjectTemplates": { "Path": "..." }` in configuration (optional) | The folder of project templates. Defaults to `Emojicode/Templates` beside the app. | The default. | The default. |
| `"Samples": { "Path": "..." }` in configuration (optional) | The folder of sample programs in the Library tab. Defaults to `Emojicode/Samples` beside the app. | The default. | The default. |

Both stores, and anything else a host puts behind `IProjectStore` or `ILibraryService`, report storage that cannot be used as a `ProjectStoreException`. The editor then says that changes are not being saved and keeps them in memory.

The templates and samples are files of `Blazemoji.Core` (`Emojicode/Templates`, `Emojicode/Samples`). The build copies them beside any app that references the library, so every host offers the same ones without keeping copies.

`AddBlazemojiEditor` keeps whatever was registered before it is called. Two uses:

- **State lifetime.** It registers the state classes (`RunState`, `ProjectState`, `RequestState`, `LocalStorageFiles`) as scoped, which on a server is one set per circuit. A desktop host with one user can register them as singletons first, and its own `IProjectStore` to match.
- **Templates or samples from somewhere other than a folder.** Register your own `IProjectTemplates` or `ISamples` first.

The web host also raises the size of a message from the browser to 4 MiB (`AddHubOptions` in `Program.cs`), because the editor hands over a file's whole text. That limit belongs to Blazor Server; a Blazor Hybrid host has none.

## In the page shell

1. MudBlazor's style sheet and script, and around the editor either `AppShell` or a shell of the host's own.

   `AppShell` (`Blazemoji.Layout`, in the components library) is what both hosts use: the title bar, Blazemoji's theme (`Blazemoji.Layout.Theme`), MudBlazor's four providers, and the dark mode button. A host's layout is `<AppShell>@Body</AppShell>`.

   A host that draws its own shell renders `MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider` and `MudSnackbarProvider` itself, with whatever theme it likes. The editor has no colours of its own. It reads the palette MudBlazor writes into the page (surface, text, primary and so on) and makes its Monaco theme from that, so it matches whatever theme the host has. To have it read them again when the page goes dark or light, cascade a `bool` named `DarkMode` around the editor, as `AppShell` does; a host whose colours never change needs nothing.
2. The style sheets `_content/Blazemoji.Components/blazemoji.css` and the host's own `<HostAssembly>.styles.css`, which pulls in the components' scoped styles. `blazemoji.css` makes the workspace fill the window below the host's title bar, with a gap above and below it (`--workspace-gap` on `.workspace`), and the editor and both side panels take their height from that. It works the bar's height out from MudBlazor's own `--mud-appbar-height`; a host whose bar is another height, or that has none, sets `--blazemoji-chrome-height` to what it does have above the workspace.

   The web host links Bootstrap 5.1 as well, ahead of these. The components do not use its classes, but a few of them draw plain headings, paragraphs and `<pre>` blocks, which Bootstrap's reset styles. Without Bootstrap those take MudBlazor's and the browser's defaults: the same content, slightly different spacing.
3. Monaco's scripts, in this order, before Blazor's:

   ```html
   <script src="_content/BlazorMonaco/jsInterop.js"></script>
   <script src="_content/BlazorMonaco/lib/monaco-editor/min/vs/loader.js"></script>
   <script src="_content/BlazorMonaco/lib/monaco-editor/min/vs/editor/editor.main.js"></script>
   ```

4. The scripts the components import themselves need nothing from the page: `_content/Blazemoji.Components/js/emojicodeLanguage.js` (the editor's help) and `_content/Blazemoji.Components/js/clipboard.js` (the Copy buttons) are loaded as modules when first used.
5. Blazor started from Monaco's ready callback, not automatically. Monaco defines itself a moment after its script loads, and an editor created before that silently does nothing:

   ```html
   <script src="_framework/blazor.server.js" autostart="false"></script>
   <script>require(['vs/editor/editor.main'], () => Blazor.start(), () => Blazor.start());</script>
   ```

   A Blazor Hybrid host has its own start-up script in place of `blazor.server.js` (`_framework/blazor.webview.js` in the desktop host); the rule is the same.

## On a page

```razor
<Workspace />
```

That is the whole editor: the Files, Toolbox and Library tabs, the editor with its help, and the Output, Problems and Requests tabs.

## Projects on disk

`FileProjectStore` keeps each project as a folder under one root, named after the project:

```
~/Documents/Blazemoji/
  Hello World/
    blazemoji.json        what the project is called, how it runs, where it starts
    main.🍇
    lib/greeter.🍇
  Snippets/               what was saved from the editor as a file of its own
  .blazemoji/
    state.json            which project was open last
    trash/                see below
```

- **The folder is also the user's.** It can hold other things, be under version control, and be edited elsewhere. A project's files are the text files in it that the compiler would take by name (`SourceFileNames.IsSafe`). Anything hidden (`.git`), anything that is not UTF-8 text, and any file over 1 MiB is left alone and left out.
- **The disk is read when a project is opened.** A file added or changed by something else is there the next time the project is opened, not while it is open. A whole project folder added by something else is listed the next time the app starts.
- **A save writes what changed.** Each file is written beside itself and moved into place, so none is left half written.
- **Nothing is destroyed.** A file that leaves a project, a file that something else changed and that the editor is about to write over, a snippet that is replaced or cleared, and a project that is deleted all go to `.blazemoji/trash`, into a folder named for the moment and for where they came from. Nothing empties it.
- **A folder is a project when it has a `blazemoji.json`.** Renaming a project in the app renames its folder. A folder that was copied becomes a project of its own the next time the app starts.

## The desktop host

`Blazemoji.Desktop` is a [Hermes](https://github.com/Mythetech/Hermes) window around `<Workspace />`. It targets .NET 10 with `Mythetech.Hermes.Blazor` pinned at 1.2.0, the last release built for .NET 10, so it builds with the repository's own SDK.

```sh
scripts/dev-toolchain.sh                    # the compiler, in its container, on 127.0.0.1:5290
dotnet run --project Blazemoji.Desktop
```

Settings are read from environment variables and the command line, in .NET's usual spelling: `ToolchainClient__BaseUrl=http://somewhere:5290`, `Projects__Root=~/Code/emoji`, or `--Projects:Root=...`. `DOTNET_ENVIRONMENT=Development`, which `dotnet run` sets from the launch profile, turns on the web view's developer tools.

Without a toolchain service the app still opens and edits. Running a program then reports "The toolchain service could not be reached." in the Problems tab.

### Its test

Nothing outside the window can see into it, so the app checks itself from inside:

```sh
scripts/desktop-smoke.sh                # the build `dotnet run` would start
scripts/desktop-smoke.sh --published    # a self-contained single-file publish, as a release is built
scripts/desktop-smoke.sh --compile      # also compile and run a program, through the toolchain service
```

With `HERMES_SMOKE_TEST=1` the app runs the checks in `Blazemoji.Desktop/Scripts/smoke.ts` once its page is up (the editor loads and shows the project, the style sheets and font arrive, the editor has the page's colours, a key and the toolbox each type an emoji pair, completion answers, dark mode reaches the editor), adds that the project is a folder on disk, prints the outcome and closes. It keeps that run's projects in a temporary folder unless `Projects__Root` says where.

It reports in the words of Hermes's smoke protocol: one `HERMES_SMOKE_CHECK_PASS:` or `HERMES_SMOKE_CHECK_FAIL:` line for each check, then `HERMES_SMOKE_RESULT: PASSED (n checks)` or `FAILED (...)`, the same as JSON at `HERMES_SMOKE_TEST_RESULT` when that is set, and exit code 0 only for a pass. Hermes does this itself from 1.3.0; on 1.2.0 the host does it (`Blazemoji.Desktop/Smoke`), so that what judges another Hermes app's smoke run can judge this one. `HERMES_SMOKE_TEST_TIMEOUT` (seconds, 60 by default) is how long the page has to report before the run is failed.

### Things to know

- **Web view storage is not used.** Hermes 1.2.0 gives every Hermes app on Windows the same web view data folder, so anything kept in the page's local storage there would be shared between apps. Projects and snippets are on disk instead.
- **Hermes copies each library's static files flat as well as in their folders.** A publish holds Monaco twice: once under `_content/BlazorMonaco/lib/...`, where the page asks for it, and once more with every file at the top of `_content/BlazorMonaco/`. The copies are about 15 MB and do no harm.
- **No lock may be contended on the window's thread.** Hermes's synchronization context runs a posted callback inline when the thread is free, which leaves a `SemaphoreSlim` taken with nobody holding it if a waiter finishes without really awaiting. `Workspace` uses `OnePassAtATime` for that reason. A new lock in a component must only be taken by callers that go on to await the web view.

## What is not here yet

- The desktop app does not start the toolchain service. It has to be running already.
- A project is one of the folders under the projects root. A folder somewhere else cannot be opened, and a folder without a `blazemoji.json` is not adopted.
- Changes made to a project's files by something else are seen when the project is next opened, not as they happen.
- The desktop app is not packaged, signed or given an icon, and nothing builds it but `dotnet`.
- Copying uses the browser's clipboard API from inside a click. It is accepted in the desktop web view on macOS; Windows and Linux have not been tried.
- The libraries are referenced as projects, not published as packages.
