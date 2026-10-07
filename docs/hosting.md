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
services.AddBlazemojiEditor();                // catalog, code intelligence, templates, state

// What only the host can supply.
services.AddScoped<IProjectStore, YourProjectStore>();
services.AddTransient<ILibraryService, YourLibraryService>();
```

| The host supplies | For | The web host's version |
| --- | --- | --- |
| `IProjectStore` | Where projects are kept between sessions. | `LocalStorageProjectStore`: the browser's local storage. A desktop host would write files. |
| `ILibraryService` | The sample programs and saved snippets in the Library tab. | `LibraryService`: samples from `Emojicode/Samples`, snippets in local storage. |
| `ProjectTemplateOptions.Path` (optional) | The folder of project templates. Defaults to `Emojicode/Templates` beside the app. | The default. The folders are content of the host project. |

`AddBlazemojiEditor` registers the state classes (`RunState`, `ProjectState`, `RequestState`) as scoped: one set per circuit on a server, one per window on a desktop.

## In the page shell

1. MudBlazor's providers (`MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider`) and its style sheet and script.
2. The style sheets `_content/Blazemoji.Components/blazemoji.css` and the host's own `<HostAssembly>.styles.css`, which pulls in the components' scoped styles.
3. Monaco's scripts, in this order, before Blazor's:

   ```html
   <script src="_content/BlazorMonaco/jsInterop.js"></script>
   <script src="_content/BlazorMonaco/lib/monaco-editor/min/vs/loader.js"></script>
   <script src="_content/BlazorMonaco/lib/monaco-editor/min/vs/editor/editor.main.js"></script>
   ```

4. Blazor started from Monaco's ready callback, not automatically. Monaco defines itself a moment after its script loads, and an editor created before that silently does nothing:

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
- The libraries are referenced as projects, not published as packages.
