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

## Amendments during implementation

1. **The saved-snippet list is now per session.** `LocalStorageFiles` was registered as one instance for the whole app, so on a server every visitor shared one list of snippet names and code. It is registered per session with the other state. This is the one behaviour change in the phase.
2. **The status words for HTTP codes are a small table in `RequestState`.** The Requests panel used ASP.NET's `ReasonPhrases`, which a component library that must also load in a desktop app cannot reference.
3. **The keybindings stay with the components,** not in Core: they are written in terms of Monaco's key codes.
4. **`IProjectTemplates` and its file reader are in Core;** `IProjectStore` and `ILibraryService` are in Components, since only a host implements them.
5. **The Dockerfile's new `COPY` lines have not been through an image build.** Docker's disk had no room for one. The projects build and pass their tests in the Linux container, where the sources are copied in whole, and the published output was checked for the library's static files.

## Changes after the independent review

A fresh reviewer found the move faithful and the web app intact, and the third goal only partly met: a host built from `docs/hosting.md` alone would have had a broken Copy button and visible styling differences, and the test that was said to pin the document did not read it.

1. **Copy is a module of the library** (`clipboard.ts`, behind `ClipboardInterop`). It was a function in an inline script on the web host's page.
2. **The library's markup uses no Bootstrap class.** The spacing classes are MudBlazor's; `text-nowrap` and `text-md-center` are in the library's style sheet.
3. **`AddBlazemojiEditor` brings what it needs** (options, logging), **keeps what a host registered first,** and reads `ProjectTemplates` from configuration when given it. Registering first is also how a single-user host makes the state classes singletons.
4. **The hosting tests read the document.** They check that the registrations it lists are the ones they make, that every library file it names exists, that every tab renders, and that the two scripts load from the library with nothing else defined. The sentence under Testing that says the document "cannot drift from the code" claimed more than the first version of the test did. What the tests still cannot check is the page shell: the order of the scripts and how Blazor is started are only exercised by the browser tests against the web host.
5. **The Dockerfile's steps were replayed on macOS by the reviewer** (restore from the seven project files alone, build, publish) and succeeded. A real image build is still to be done.

Left as they are: the status-word table covers 34 codes where ASP.NET's covers 63, and words two of them differently; `ILibraryService` and the Library tab speak of "local storage"; namespaces now span assemblies.
