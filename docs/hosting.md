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
| `ILayoutStore` (optional) | Where the workspace's layout is kept between sessions: where its two dividers are, and whether the sidebar is hidden. Without one the layout lasts as long as the page. | `LocalStorageLayoutStore`: local storage. | `FileLayoutStore`: one file beside the projects, registered by `AddBlazemojiProjectsOnDisk`. |
| `IExternalLinks` (optional) | Opening the links in the title bar. Without one they are ordinary links that open in a new tab. | None. | `HermesExternalLinks`: the system's browser, since a link followed in the app's own window would take the editor away. |
| `"Projects": { "Root": "..." }` in configuration (optional) | With `AddBlazemojiProjectsOnDisk`, the folder projects are kept under. Defaults to `Blazemoji` in the user's documents. A leading `~/` is the user's home. | Not used. | The default. |
| `"ProjectTemplates": { "Path": "..." }` in configuration (optional) | The folder of project templates. Defaults to `Emojicode/Templates` beside the app. | The default. | The default. |
| `"Samples": { "Path": "..." }` in configuration (optional) | The folder of sample programs in the Library tab. Defaults to `Emojicode/Samples` beside the app. | The default. | The default. |

The stores, and anything else a host puts behind `IProjectStore`, `ILibraryService` or `ILayoutStore`, report storage that cannot be used as a `ProjectStoreException`. For a project the editor then says that changes are not being saved and keeps them in memory. For a file saved to the library it says that the file could not be saved. A layout that cannot be kept is logged and nothing is said: the dividers still move, and are back at the default next time.

The templates and samples are files of `Blazemoji.Core` (`Emojicode/Templates`, `Emojicode/Samples`). The build copies them beside any app that references the library, so every host offers the same ones without keeping copies.

`AddBlazemojiEditor` keeps whatever was registered before it is called. Two uses:

- **State lifetime.** It registers the state classes (`RunState`, `ProjectState`, `RequestState`, `LayoutState`, `LocalStorageFiles`) as scoped, which on a server is one set per circuit. A host with one user registers them as singletons first, as the desktop host does.
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

4. The scripts the components import themselves need nothing from the page: `_content/Blazemoji.Components/js/emojicodeLanguage.js` (the editor's help), `_content/Blazemoji.Components/js/clipboard.js` (the Copy buttons), `_content/Blazemoji.Components/js/splitView.js` (settling a divider where it was let go) and `_content/Blazemoji.Components/js/programInput.js` (sending a line typed for a running program) are loaded as modules when first used.
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

That is the whole editor: the Files, Toolbox and Library tabs in a sidebar, the editor with its help beside it, and the Output, Problems and Requests tabs under the editor. The two dividers between them can be dragged, and the sidebar can be hidden (`SplitView`, around MudBlazor's split panel). Where they are left is the layout an `ILayoutStore` keeps.

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
    layout.json           where the workspace's dividers are, and whether the sidebar is hidden
    trash/                see below
```

- **The folder is also the user's.** It can hold other things, be under version control, and be edited elsewhere. A project's files are the text files in it that the compiler would take by name (`SourceFileNames.IsSafe`). Left alone and left out: anything hidden (`.git`), anything that is a link to somewhere else, anything that is not UTF-8 text or holds a NUL character, any file over 1 MiB, and anything that cannot be read.
- **Every such text file is part of the project,** whatever it is for, and goes to the toolchain service with each compile. A project folder is not the place for unrelated text.
- **The disk is read when a project is opened.** A file added or changed by something else is there the next time the project is opened, not while it is open. A whole project folder added by something else is listed the next time the app starts.
- **A save writes what changed,** one save at a time in the order they were asked for. Each file is written beside its place and moved into it, so none is left half written. A file that was renamed is moved, and stays the same file.
- **What the editor did not put there is never thrown away.** A file that leaves a project, a file that something else put or changed where the editor is about to write, a snippet that is replaced or cleared, and a project that is deleted all go to `.blazemoji/trash`, into a folder named for the moment and for where they came from. Nothing empties it. The editor's own earlier text is simply written over, as in any editor.
- **A disk may not tell `Main` from `main`.** The editor will not make two files whose names differ only in the case of their letters or in how an accent is put on, nor one whose name starts with a dot, nor one called `blazemoji.json` beside the project's own. Renaming a file only in the case of its letters works.
- **A folder is a project when it has a `blazemoji.json`.** Renaming a project in the app renames its folder, and a folder renamed by hand is found again by what its description says. Of two folders with the same description, as when one was copied, the one whose name sorts first keeps the identity and the other becomes a project of its own the next time the app starts. The `Snippets` folder is never a project and no project is given it.
- **A link is never written through.** A folder or file in a project that is a link to somewhere else is left out, and a save that would go through one is refused, which shows as changes not being saved.

## The desktop host

`Blazemoji.Desktop` is a [Hermes](https://github.com/Mythetech/Hermes) window around `<Workspace />`. The program it builds is named `Blazemoji`, after the app, which is the name packaging looks for. It targets .NET 10 with `Mythetech.Hermes.Blazor` pinned at 1.2.0, the last release built for .NET 10, so it builds with the repository's own SDK.

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
scripts/desktop-smoke.sh --app <path>   # a copy that is already built, such as the one inside an unpacked release
```

With `HERMES_SMOKE_TEST=1` the app runs the checks in `Blazemoji.Desktop/Scripts/smoke.ts` once its page is up (the editor loads and shows the project, the style sheets and font arrive, the editor has the page's colours, a key and the toolbox each type an emoji pair, completion answers, dark mode reaches the editor, a dragged divider is heard and the sidebar settles where it was let go). It adds what only its own side can see (the project is a folder on disk, the samples, the templates and the window's icon came with the app, and the layout the drag left is in the projects folder), prints the outcome and closes.

A smoke run types into the editor and what is typed is saved, so it never uses the folder the app is otherwise set to keep projects in, whatever `Projects__Root` says. It makes a temporary folder and removes it, or uses the one named by `BLAZEMOJI_SMOKE_PROJECTS` and leaves it.

It reports in the words of Hermes's smoke protocol: one `HERMES_SMOKE_CHECK_PASS:` or `HERMES_SMOKE_CHECK_FAIL:` line for each check, then `HERMES_SMOKE_RESULT: PASSED (n checks)` or `FAILED (...)`, the same as JSON at `HERMES_SMOKE_TEST_RESULT` when that is set, and exit code 0 only for a pass. Hermes does this itself from 1.4.1; on 1.2.0 the host does it (`Blazemoji.Desktop/Smoke`), so that what judges another Hermes app's smoke run can judge this one. `HERMES_SMOKE_TEST_TIMEOUT` (seconds, 60 by default) is how long the page has to report before the run is failed.

### Things to know

- **Nothing is kept in the web view's own storage.** Projects and snippets are on disk, where other tools can reach them and where they do not depend on how a web view keeps its data.
- **Hermes copies each library's static files flat as well as in their folders.** A publish holds Monaco twice: once under `_content/BlazorMonaco/lib/...`, where the page asks for it, and once more with every file at the top of `_content/BlazorMonaco/`. The copies are about 15 MB and do no harm.
- **No lock may be contended on the window's thread.** Hermes's synchronization context runs a posted callback inline when the thread is free, which leaves a `SemaphoreSlim` taken with nobody holding it if a waiter finishes without really awaiting. `Workspace` uses `OnePassAtATime` for that reason, and the stores on disk take turns by each waiting for the one before, with no lock. A new lock in a component must only be taken by callers that go on to await the web view.
- **The icon is three files in `wwwroot`**, cut from the drawings under `art/` by `scripts/build-icons.sh`: `logo.ico` for Windows, `logo.png` for Linux and `logo.icns` for macOS, where it is drawn on the rounded square that system expects. Hermes 1.2.0 gives the window itself an icon on Windows only. On macOS and Linux a window's icon comes from the packed app, so a copy started with `dotnet run` has the system's plain one.
- **The page has Blazor's error bar** (`#blazor-error-ui` in `index.html`, styled by `wwwroot/app.css`). Without it a component that throws would leave the window as it was, with nothing said.

## What is not here yet

- The desktop app does not start the toolchain service. It has to be running already.
- A project is one of the folders under the projects root. A folder somewhere else cannot be opened, and a folder without a `blazemoji.json` is not adopted.
- Changes made to a project's files by something else are seen when the project is next opened, not as they happen. Two copies of the app on one projects folder would each take the other's saves for someone else's changes and move them to the trash.
- What was typed in the last moment before the window is closed can be lost: text reaches the project when typing pauses.
- The desktop app has only been run on macOS. On Windows some names it accepts cannot be files (`aux.🍇`, a name ending in a dot), and a save would fail there.
- The desktop app's packed builds are not signed. The **Publish Desktop** workflow, run by hand, packs it for Windows, macOS and Linux with Velopack and drafts a GitHub release; signing is in the workflow and waits for the keys. The app calls Velopack at start-up so that an installed copy works, but nothing checks for updates.
- Copying uses the browser's clipboard API from inside a click. It is accepted in the desktop web view on macOS; Windows and Linux have not been tried.
- The libraries are referenced as projects, not published as packages.
