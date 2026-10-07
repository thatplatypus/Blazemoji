# Hosting the Blazemoji editor

The editor is two libraries and a host.

| Project | What it is |
| --- | --- |
| `Blazemoji.Core` | What Blazemoji knows about Emojicode and about projects, with no UI: the keyword catalog, the code intelligence, the project model and templates. |
| `Blazemoji.Components` | The editor as Razor components, the state behind them, and the interfaces a host implements. |
| `Blazemoji` | The web host: a Blazor Server app that shows the editor and keeps projects in the browser. |

A second host, such as a desktop app, references `Blazemoji.Components` and does what the web host does in `Blazemoji/Program.cs` and `Blazemoji/Components/App.razor`. This page lists it.

## The one setting: where the toolchain service is

The editor never compiles or runs anything itself. It talks to the toolchain service over HTTP, and the address is one setting:

```json
{ "ToolchainClient": { "BaseUrl": "http://localhost:5290" } }
```

The web host's containers set it to `http://toolchain:8080`. A desktop host points it at a toolchain service it starts itself (the `toolchain` image, or `Blazemoji.Toolchain.Service` on x86_64 Linux) or at one running somewhere else. Nothing else in the editor knows where code is built. `ToolchainClient:RequestTimeout` (two minutes by default) is how long one request may take.

## Services to register

```csharp
// The same for every host.
services.AddMudServices();
services.AddToolchainClient(configuration);   // reads ToolchainClient:BaseUrl
services.AddBlazemojiEditor(configuration);   // catalog, code intelligence, templates, state

// What only the host can supply.
services.AddScoped<IProjectStore, YourProjectStore>();
services.AddTransient<ILibraryService, YourLibraryService>();
```

The types are in `MudBlazor.Services`, `Blazemoji.Toolchain.Http`, `Blazemoji` (for `AddBlazemojiEditor`), `Blazemoji.Services.Projects` and `Blazemoji.Services.Library`.

| The host supplies | For | The web host's version |
| --- | --- | --- |
| `IProjectStore` | Where projects are kept between sessions. | `LocalStorageProjectStore`: the browser's local storage. A desktop host would write files. |
| `ILibraryService` | The sample programs and saved snippets in the Library tab. | `LibraryService`: samples from `Emojicode/Samples`, snippets in local storage. |
| `"ProjectTemplates": { "Path": "..." }` in configuration (optional) | The folder of project templates. Defaults to `Emojicode/Templates` beside the app. | The default. The folders are content of the host project. |

`AddBlazemojiEditor` keeps whatever was registered before it is called. Two uses:

- **State lifetime.** It registers the state classes (`RunState`, `ProjectState`, `RequestState`, `LocalStorageFiles`) as scoped, which on a server is one set per circuit. A desktop host with one user can register them as singletons first, and its own `IProjectStore` to match.
- **Templates from somewhere other than a folder.** Register your own `IProjectTemplates` first.

The web host also raises the size of a message from the browser to 4 MiB (`AddHubOptions` in `Program.cs`), because the editor hands over a file's whole text. That limit belongs to Blazor Server; a Blazor Hybrid host has none.

## In the page shell

1. MudBlazor's providers (`MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider`) and its style sheet and script. The theme is the host's: the web host's is `Blazemoji/Layout/Theme.cs`, and `MainLayout.razor` switches Monaco between its light and dark themes when MudBlazor's dark mode changes.
2. The style sheets `_content/Blazemoji.Components/blazemoji.css` and the host's own `<HostAssembly>.styles.css`, which pulls in the components' scoped styles. `blazemoji.css` gives the editor a height of 83% of the window (`.editor`), which suits the web host's layout; a host with a different layout overrides it.

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

   A Blazor Hybrid host has its own start-up script in place of `blazor.server.js`; the rule is the same.

## On a page

```razor
<Workspace />
```

That is the whole editor: the Files, Toolbox and Library tabs, the editor with its help, and the Output, Problems and Requests tabs.

## What is not here yet

- No desktop host has been built.
- There is no file-based `IProjectStore`.
- The sample programs and project templates are files of the web host. A second host needs its own copies or a shared content project.
- `ILibraryService` and the Library tab still speak of "local storage", which is where the web host keeps snippets. A desktop host implements the same methods over files, and the wording wants changing when one exists.
- Copying uses the browser's clipboard API. Whether a desktop WebView allows it has not been tried.
- The libraries are referenced as projects, not published as packages.
