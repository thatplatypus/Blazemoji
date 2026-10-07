# Phase 5: A shared component library

- **Date:** 2026-10-07
- **Branch:** `phase-5-component-library`, from `phase-4-code-intelligence`
- **Status:** written and self-approved during the overnight run Tom authorised on 2026-10-06; awaiting his review after the fact

## Context

Everything the editor is made of lives in the web project: the components, the state classes, the keyword catalog, the code intelligence. A second host, the planned Hermes desktop app, could not use any of it without referencing a web application. This phase moves it into libraries, following the Mythetech layering: Core for what needs no UI, Components for the UI and its state, and the host for what needs the platform.

## Goal

Done when:

1. the editor, the Monarch setup, the keyword catalog, the toolbox, the keybindings and the completion live outside the web project;
2. the web app looks and behaves exactly as before, shown by the same unit, compiler, contract and browser tests passing unchanged;
3. a short document says what a desktop host has to do to use the library, with the toolchain service's address as the one setting it must supply.

No desktop host is built.

## Design

### Projects

| Project | SDK | Holds | References |
| --- | --- | --- | --- |
| `Blazemoji.Core` | `Microsoft.NET.Sdk` | The keyword catalog and emoji constants, keybindings, diagnostic positions, the project model and file tree, project templates read from disk, and all of the code intelligence. No Blazor, no MudBlazor. | `Blazemoji.Toolchain`, GEmojiSharp |
| `Blazemoji.Components` | `Microsoft.NET.Sdk.Razor` | Every component except the app shell, the state classes (`RunState`, `ProjectState`, `RequestState`), the interfaces a host implements (`IProjectStore`, `ILibraryService`), the icons, the Monaco provider module and its typed wrapper, and the styles the components need. | `Blazemoji.Core`, `Blazemoji.Toolchain`, MudBlazor, BlazorMonaco |
| `Blazemoji` (web host) | `Microsoft.NET.Sdk.Web` | `Program.cs`, the app shell (`App`, `Routes`, `MainLayout`, the theme, the error page), a one-line page that shows the workspace, and the two things that need a browser: projects and snippets kept in local storage. The sample and template files stay here as data. | `Blazemoji.Components` |

Namespaces do not change (`Blazemoji.Emojicode`, `Blazemoji.Shared.State`, `Blazemoji.Components` and so on), so the move is a move: no `using` changes in the code that is moved or in the tests.

### What changes besides where files are

- **`Workspace`.** The page's markup and code become a `Workspace` component in the library. The web app's `Home` page is `@page "/"` and `<Workspace />`.
- **One registration call.** `services.AddBlazemojiEditor()` registers the keyword catalog, the code intelligence, the state classes and the provider wrapper. The host adds its own `IProjectStore`, `ILibraryService` and `IProjectTemplates` options, and the toolchain client.
- **Static assets move with the components.** The provider module is served from `_content/Blazemoji.Components/js/`. The handful of global style rules the components rely on move from the host's `app.css` to `_content/Blazemoji.Components/blazemoji.css`, which the host links.

### The seam for a desktop host

`docs/hosting.md` lists what a host supplies:

1. `AddMudServices()`, `AddBlazemojiEditor()`, and `AddToolchainClient(configuration)`. **`ToolchainClient:BaseUrl` is the seam:** the web app points it at the toolchain container; a desktop app points it at a toolchain service it starts or reaches.
2. An `IProjectStore` (the web host's keeps projects in local storage; a desktop host would use files) and an `ILibraryService`.
3. Where the project templates are, through `ProjectTemplateOptions.Path`.
4. In its page shell: the MudBlazor providers, the Monaco scripts in the order `App.razor` has them, Blazor started from Monaco's ready callback, and the two style sheets.
5. `<Workspace />` wherever the editor should be.

## Testing

Nothing new is being built, so the test is that nothing changed:

- every existing unit and component test passes without edits beyond project references;
- the compiler and contract tests pass in the Linux container;
- the browser tests pass against the running app;
- a new test resolves every service the workspace needs from a container built with only the calls `docs/hosting.md` lists, so the document cannot drift from the code.

## Out of scope

- The desktop host.
- A file-based `IProjectStore`.
- Publishing the libraries as packages.
- Moving the app shell, the theme or the samples.

## Decisions taken on Tom's behalf

1. Two libraries (Core and Components) where the brief says "a Razor class library", to follow the Mythetech layering.
2. Namespaces are kept as they are.
3. Local storage implementations stay in the web host.
