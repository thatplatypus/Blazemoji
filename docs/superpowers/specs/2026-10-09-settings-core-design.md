# Settings core

- **Date:** 2026-10-09
- **Branch:** `settings-core`, from `editor-tabs` as it stood on 2026-10-09. It was to wait for that branch to be merged; Tom said the same day that it was only waiting to be pushed and to branch from it
- **Status:** read by Tom on 2026-10-09 ("Seems good"). Being built from `docs/superpowers/plans/2026-10-09-settings-core.md`
- **Part 1 of 3.** Part 2 is the desktop app's update check over GitHub Releases. Part 3 is the rest of Monaco's options. Each has a spec of its own

## Context

Blazemoji has no settings. The editor's options are fixed in `EmojiCodeEditor.razor`, and the only thing kept about how someone uses the app is the workspace's layout. Two things now need settings: the desktop app's update check, whose choices belong in a settings panel, and the editor, which is to offer Monaco's options the way Horizon does.

Tom's other apps get settings from Mythetech.Framework: a class for each section with an attribute on each property, a panel drawn from the attributes, and a store for each platform. Blazemoji cannot take that as it is, for three reasons found on 2026-10-09:

1. Its provider, its section objects, its store and its message bus are one per process. That suits a desktop app. The website is a Blazor Server app with many visitors in one process, where one visitor's settings would be everyone's.
2. A change to the Framework is released for .NET 11. Blazemoji is on .NET 10 with Hermes pinned at 1.2.0.
3. Blazemoji needs two things the Framework's settings do not have: a way to say a setting belongs to one host, and (in part 3) a way to say which Monaco option a setting is.

So the settings code is brought over from the Framework and owned here, without the message bus (Tom, 2026-10-09: "I'm thinking we bring it over"). This part builds the machinery and proves it with five editor options on both hosts.

"Settings" in this spec means what a person chooses in the app. What whoever runs the app sets (`ToolchainClient:BaseUrl`, `Projects:Root`) is configuration, and is not touched.

## Goal

Done when:

1. a section of settings is one class and a setting is one attributed property on it: adding a property is all it takes for the setting to be shown in the panel, kept between visits, and readable by the code that acts on it;
2. on the website each visitor has their own settings, kept in their browser, and on the desktop there is one set, kept in a file beside the layout;
3. a Settings button in the title bar opens the panel on both hosts, and a change takes effect at once, with no reload and no Save button;
4. five editor options (font size, word wrap, minimap, line numbers, showing whitespace) are settings, are applied to an open editor as they change, and are already in place when the editor first appears on a later visit;
5. a section or a single setting can be marked as belonging to one host, and is then not shown, read or kept on the other;
6. someone who never opens the panel sees the editor as it is today, with the one exception given under "The first section";
7. Blazemoji still references neither Mythetech.Framework nor a message bus.

## Design

### Where things live

The layout is the pattern: a model and a store interface in Core, a state class in Components, a store in each host (`ILayoutStore`, `LayoutState`, `LocalStorageLayoutStore`, `FileLayoutStore`).

| Project | New | Changed |
| --- | --- | --- |
| `Blazemoji.Core` | `Shared/Models/Settings/`: `SettingsBase`, `SettingAttribute`, `SettingHosts`. `Services/Settings/`: `SettingsOptions`, `SettingProperties` (the attributed properties of a section type, read once), `SettingsJson` (settings as the text a store keeps), `ISettingsStore`, `FileSettingsStore`. | |
| `Blazemoji.Components` | `Shared/State/SettingsState`. `Settings/EditorSettings` and `Settings/EditorOptionValues`. `Components/Settings/`: `SettingsButton`, `SettingsDialog`, `SettingsSectionView`, `SettingRow`, and one editor for each kind of value. | `BlazemojiEditorRegistration`, `AppShell`, `EmojiCodeEditor`, `BlazemojiIcons` |
| `Blazemoji` (website) | `Services/Settings/LocalStorageSettingsStore` | `Program.cs` |
| `Blazemoji.Desktop` | | `Program.cs`, the smoke run |

Nothing here needs Blazor to be tested except the components themselves: reading and writing the kept text, the limits on a value and the host filter are plain C# in Core.

### A section and a setting

Brought over from the Framework with the same names, so that a section written for Horizon reads the same here.

```csharp
public abstract class SettingsBase
{
    public abstract string SettingsId { get; }      // the key it is kept under: "editor"
    public abstract string DisplayName { get; }     // "Editor"
    public abstract string Icon { get; }            // from BlazemojiIcons
    public virtual int Order => 50;
    public virtual SettingHosts Hosts => SettingHosts.All;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingAttribute : Attribute
{
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Group { get; set; }
    public int Order { get; set; } = 100;
    public double Min { get; set; } = double.NaN;
    public double Max { get; set; } = double.NaN;
    public double Step { get; set; } = 1;
    public string? Options { get; set; }            // "none,boundary,selection": a choice, not free text
    public bool Hide { get; set; }                  // kept, but not shown in the panel
    public SettingHosts Hosts { get; set; } = SettingHosts.All;
}

[Flags]
public enum SettingHosts { None = 0, Web = 1, Desktop = 2, All = Web | Desktop }
```

A setting is a property of type `bool`, `int`, `double` or `string`, written like this:

```csharp
[Setting(Label = "Font size", Group = "Text", Order = 1, Min = 8, Max = 32)]
public int FontSize { get; private set; } = 14;
```

- **The value a new instance has is the default.** There is nowhere else a default is written.
- **The setter is private.** Only `SettingsState` changes a setting. It assigns through the property by reflection, which a private setter allows. A component cannot change one by mistake, and the compiler says so.
- **`Hosts` on the section hides the whole section from the other host; `Hosts` on a setting hides that one.** What is hidden keeps its default there, and is not shown, read from the store or written to it.
- **A section's `Icon` comes from `BlazemojiIcons`,** which is why the sections themselves are in Components and only their base class is in Core.

### The state

`SettingsState` is the one place settings are held and changed. It is registered like the other state classes: scoped by `AddBlazemojiEditor`, so one per visitor on the website, and a singleton in the desktop host, which registers it first.

```csharp
public sealed class SettingsState(IOptions<SettingsOptions> options, ILogger<SettingsState> logger, ISettingsStore? store = null)
{
    public IReadOnlyList<SettingsBase> Sections { get; }       // this host's, in the panel's order
    public T Get<T>() where T : SettingsBase;

    public event Action<SettingsBase>? SettingsChanged;        // the section that changed

    public bool SaveFailed { get; }                            // the last save did not succeed

    public Task LoadAsync();
    public Task SetAsync(SettingsBase section, string property, object? value);
    public Task RestoreDefaultsAsync(SettingsBase section);
}
```

- **It makes the sections itself,** one instance of each type named in `SettingsOptions`, for itself alone. A section is not registered for injection: whoever wants one asks the state (`Get<EditorSettings>()`), so there is no second way to reach a setting. `Get` for a section this host does not show gives it at its defaults; for a type that was never named it throws.
- **`LoadAsync` reads the store once.** A call while the read is under way gets the same task, and a call after it gets a finished one. With no store it finishes at once. What was kept is put onto the sections, and `SettingsChanged` is raised once for each section that came out different. It may only be called where the browser can be reached (after the first render, or from something the user did), as with the other stores; nothing calls it while the page is prerendered.
- **`SetAsync` waits for the load first,** so a change made before the kept settings have arrived lands on top of them and is not written over. It then checks the value, and if it differs from the current one assigns it, raises `SettingsChanged` and saves.
- **What `SetAsync` accepts is decided by the state, not by the control that sent it.** A number is brought within `Min` and `Max` when both are given; a `double` that is not a number is refused; a string with `Options` must be one of them; any string over 1,000 characters is refused. A refused value changes nothing. A property that is not a setting of that section, or not one this host has, is a mistake in the caller and throws. The check itself is one function in Core (`SettingProperties`), which reading the kept text uses too, so a value cannot get in from the store that could not get in from the panel.
- **`RestoreDefaultsAsync` puts every setting of the section that this host shows back to its default,** with one `SettingsChanged` and one save.
- **A store that fails never stops a setting from changing.** A read that fails with a `ProjectStoreException` is logged and the defaults stand. A save that fails with one is logged and sets `SaveFailed`, which the panel shows; the change still holds until the page or the app is closed. Any other exception is a fault in the store and reaches whoever called.
- **It takes no lock.** Everything it does happens on the visitor's circuit or the window's thread. Saves leave in the order the changes were made, and each holds every setting, so the last to leave is what is kept.

`SettingsOptions` holds the host (`Host`, `SettingHosts.Web` unless the host says otherwise) and the section types (`Add<T>()`). `AddBlazemojiEditor` adds `EditorSettings`; a host adds its own the same way, which is how part 2's update settings will arrive.

### Keeping settings

```csharp
public interface ISettingsStore
{
    Task<string?> LoadAsync();          // null when nothing has been kept
    Task SaveAsync(string settings);
}
```

Both methods throw `ProjectStoreException` when the storage cannot be used, as the other stores do. A store keeps one piece of text and knows nothing about what is in it. `SettingsJson` in Core makes that text from the sections and reads it back into the values to give them, the same for every store. The state does the assigning:

```json
{
  "editor": { "fontSize": 16, "wordWrap": true }
}
```

- **Only what differs from its default is kept.** Someone who has never changed a setting has nothing kept, so a default that is changed in a later version reaches everyone who left it alone.
- **Reading forgives.** Text that is not such an object is no settings. A section or setting this version does not know is passed over. A value of the wrong kind, or one `SetAsync` would refuse, leaves the default; a number out of range is brought within it. Reading never throws for what the text says.
- **What is passed over is not kept on the next save.** A setting from a newer version is dropped by an older one.

| Host | Store | Where |
| --- | --- | --- |
| Website | `LocalStorageSettingsStore`, scoped | The browser's local storage, key `blazemoji.settings`. The Library lists and clears only keys that do not start with `blazemoji.`, so its "Clear Saved Files" leaves settings alone. |
| Desktop | `FileSettingsStore`, a singleton registered by `AddBlazemojiProjectsOnDisk` | `<Projects:Root>/.blazemoji/settings.json`, beside `layout.json`, written through `ProjectsFolder` in its turn like the layout. A smoke run already has a projects folder of its own, so it never reads or writes the user's settings. |

### The panel

A Settings button in `AppShell`'s title bar, before the dark mode button, opens a MudBlazor dialog, as the app's other dialogs are opened.

- **`SettingsButton`** is an icon button with a tooltip (`BlazemojiIcons.Settings`, `data-testid="settings-button"`). On a click it awaits `LoadAsync` and then shows the dialog. A host that draws its own shell places the button itself.
- **`SettingsDialog`** shows one section at a time. With more than one section they are chosen with the app's own `SegmentedTabs`; with one there is nothing to choose and no tabs. It has a close button and no Save or Cancel. When `SaveFailed` is set it shows a `MudAlert` with `Severity.Warning` and fixed words: "Your settings could not be saved, so changes last only until Blazemoji is closed."
- **`SettingsSectionView`** draws a section: its settings in `Order`, under their `Group` headings in the order each group first appears, and at the end a "Restore defaults" button that is disabled while everything is at its default.
- **`SettingRow`** draws one setting: its label and description on one side and its control on the other, stacked when the dialog is narrow.

| Kind of value | Control |
| --- | --- |
| `bool` | `MudSwitch` |
| `int`, `double` | `MudNumericField` with the setting's `Min`, `Max` and `Step` |
| `string` with `Options` | `MudSelect` |
| `string` | `MudTextField` |

A control is handed the value and hands back a new one; `SettingRow` passes it to `SetAsync`. No control assigns to a section. A text or number setting changes when its field is left or Enter is pressed, not at each key.

Loops are keyed by `SettingsId` and property name. Nothing appears on hover, and nothing in a row changes size when its value does. The look follows the app's theme and its other dialogs; no new kind of control or card is made for it.

### The first section

`EditorSettings` (`SettingsId` "editor", shown as "Editor"):

| Setting | Type | Default | Limits | Group | Monaco option |
| --- | --- | --- | --- | --- | --- |
| Font size | `int` | 14 | 8 to 32 | Text | `fontSize` |
| Word wrap | `bool` | off | | Text | `wordWrap`: `on` or `off` |
| Minimap | `bool` | on | | Display | `minimap.enabled` |
| Line numbers | `bool` | on | | Display | `lineNumbers`: `on` or `off` |
| Show whitespace | `string` | `selection` | `none`, `boundary`, `selection`, `trailing`, `all` | Display | `renderWhitespace` |

Every default is what Monaco does today when Blazemoji says nothing, except one: **Monaco's own font size is 12 on macOS and 14 everywhere else.** A setting has one default, and it is 14, as in Horizon. Someone on a Mac who never opens the panel will see the text two points larger than now. That is the exception goal 6 names.

`EditorOptionValues` is a small record holding these five values as Monaco wants them, made from an `EditorSettings` by a plain function and turned into BlazorMonaco's `StandaloneEditorConstructionOptions` and `EditorUpdateOptions`. Two of them are equal when they would make the editor look the same, which is how the editor knows whether there is anything to do. Part 3 replaces it with a mapping that reads the Monaco option from each setting; it is kept small for that reason.

### The editor

`EmojiCodeEditor` takes its options from the state in the way it already takes its colours from the page:

1. **When it is made,** `EditorConstructionOptions` adds the five values to the options it sets today and remembers them as applied.
2. **Before it is shown,** `InitEditor` waits for `LoadAsync`, for two seconds at most, and brings the editor into line. So on a later visit the editor first appears with the visitor's settings, and a store that is slow or broken delays it by two seconds and no more. Settings that arrive after that are applied by step 3.
3. **When a setting changes,** its handler for `SettingsChanged` asks for a redraw if the section is `EditorSettings`, and does nothing else. After the redraw, `OnAfterRenderAsync` compares the values now wanted with those applied and, if they differ, remembers the new ones and then calls `UpdateOptions` once. Remembering before the call is what keeps a second redraw during the call from sending them again.

It subscribes in `OnInitialized` and unsubscribes in `Dispose`. No lock is added; `_typing` stays the only one.

### What a host does

```csharp
// The website
builder.Services.AddScoped<ISettingsStore, LocalStorageSettingsStore>();

// The desktop app, beside the other state classes and before AddBlazemojiEditor
builder.Services.AddSingleton<SettingsState>();
builder.Services.Configure<SettingsOptions>(settings => settings.Host = SettingHosts.Desktop);
// FileSettingsStore comes with AddBlazemojiProjectsOnDisk
```

A host that registers no `ISettingsStore` still has settings; they last as long as the page.

`docs/hosting.md` gains a row for `ISettingsStore` and for the host, `SettingsState` in the list of state classes, `settings.json` in the picture of the projects folder, and a short section on adding a setting. Its heading "The one setting: where the toolchain service is" becomes "The one piece of configuration", so that the page uses "settings" in one sense.

### What is different from the Framework's

Brought over from `Mythetech.Framework` as it was at `5ae0402` (0.21.2): `Infrastructure/Settings` and `Components/Settings`.

| The Framework | Here | Why |
| --- | --- | --- |
| One provider, and each section a singleton that anything can inject | One `SettingsState` for each visitor or window, which makes its own sections | The website has many visitors in one process |
| A change is published on the message bus, turned into a typed message by reflection, and saved by a consumer | `SetAsync` assigns, raises `SettingsChanged` and saves | No bus |
| A control assigns to the section; the panel keeps a snapshot and commits or reverts | A control hands a value to the state; it applies at once; a section can be put back to its defaults | Components do not change the model, and the editor shows the change as it is made |
| A ranged number is a slider | A number field | On Blazor Server a drag is answered by the server about two round trips late and writes over where the pointer has got to |
| Every value kept, one document for each section | Only what was changed, one document | A default can change later, and one read fetches everything |
| Every section on one scrolling page with a scroll-spy script, and a search box | One section at a time. Search comes in part 3 | No script, and five settings need no search |
| Sections found by scanning assemblies | Sections named in `SettingsOptions` | A handful, in known places |
| No host filter | `Hosts` on a section and on a setting | Desktop-only settings arrive in part 2 |
| Enum settings, a custom editor for a setting or for a type, content before and after a section, feature flag sections | Left out until something needs them | Part 2 will need content at the end of a section |

## Testing

In Core, with xUnit:

- `SettingsJson`: what was changed comes back the same; nothing is written for a section at its defaults; text that is not an object, a value of the wrong kind, an unknown section and an unknown setting are each passed over; a number out of range is brought within it; a string that is not among the options, or is over the limit, leaves the default.
- The host filter, on a section and on a setting, for both hosts.
- `FileSettingsStore` against a temporary folder: nothing kept gives null, what is saved is read back, and the file is where `layout.json` is.

In Components, with xUnit and a store made for the test:

- `SettingsState`: the kept values are applied and announced; a second `LoadAsync` does not read again; a change is assigned, announced and saved, in that order; a value equal to the current one does nothing; a change made while the store is still answering lands on top of what it answers, and is saved; a read that fails leaves the defaults and the state usable; a save that fails sets `SaveFailed` and the next that succeeds clears it; each refusal listed under "The state"; restoring defaults; a setting for the other host is not applied from the store and not written to it.
- Two scopes built from the website's registrations have two states, and a change in one is not in the other. This is why the code was brought over, so a test holds it.
- `HostingTests` resolves `SettingsState` from a container built with only what `docs/hosting.md` lists.

In Components, with bUnit and a section made for the test that has one setting of each kind:

- The dialog shows the settings in order under their groups, leaves out one that is hidden and one that is for the other host, and has no tabs for a single section.
- Changing each control reaches the state with the right value, and the control shows what the state then holds (a number typed out of range shows the limit it was brought to).
- "Restore defaults" is disabled at the defaults and enabled after a change. The warning shows when a save has failed.
- `EmojiCodeEditor`: a change to an editor setting makes one `UpdateOptions` call; a change to another section makes none; a redraw while the call is unanswered does not send it again (the call is set up and left without a result, then completed); with a store that does not answer, the editor is shown after two seconds, on a clock the test moves.

In the browser, with Playwright, at 1920 by 1080 and at 1024 by 768, and against the container stack before it is trusted:

- Opening the panel, changing the font size and seeing the editor's text change size without a reload; the same for the minimap going away.
- After a reload the editor's text is the chosen size when it first becomes visible.
- A second browser context has the defaults.
- "Clear Saved Files" in the Library leaves the settings as they were.
- After the dialog is closed the Settings button's tooltip is not left open over the title bar. A MudBlazor dialog gives focus back to the button that opened it, and a tooltip shows on focus.
- Once by hand with `scratchpad/smoke/slow-proxy.mjs` at 250 ms each way: typing a font size and flipping two switches quickly end as they were left.

On the desktop, one more check in the smoke run: a setting changed through the panel is in `settings.json` in the run's own projects folder, and the editor has it.

No test here asserts what a MudBlazor control does once drawn.

## Out of scope

- The update check and its settings (part 2).
- The rest of Monaco's options, the mapping from a setting to its Monaco option, and search in the panel (part 3).
- Enum settings, custom editors, and content before or after a section.
- A mark on a setting that has been changed, and putting a single setting back to its default.
- Dark mode as a setting. Its button stays as it is and is still not kept.
- Keeping settings in step between two open tabs, two browsers or two machines; importing and exporting them.
- A message bus.
- Any change to Mythetech.Framework.

## Known limits

To be added to "What is not here yet" in `docs/hosting.md`:

- Two tabs of the website share one browser's storage and each holds its own copy of the settings. The tab that changes a setting last writes its whole set, so a change made in the other tab since it loaded is lost.
- A setting kept by a newer version is dropped when an older version next saves.

## Decisions taken on Tom's behalf

1. The Framework's names are kept (`SettingsBase`, `[Setting]`, `SettingsId`, `Label`, `Group` and the rest), so a section can move between Horizon and Blazemoji by changing a namespace and its setters.
2. A change applies at once. There is no Save or Cancel.
3. Numbers are number fields, not sliders.
4. One section at a time, chosen with `SegmentedTabs`. No scroll-spy.
5. Only changed values are kept, in one document.
6. On the desktop the file is under the projects root beside `layout.json`, not in the system's folder for application data. Changing `Projects:Root` therefore changes which settings are in force.
7. Font size defaults to 14 on every system, which enlarges the text on macOS for anyone who leaves it alone.
8. Settings have private setters.
9. The first five options are the ones in the table.
10. The editor waits two seconds at most for kept settings before it shows.

Taken while it was being built, each for Tom to overturn:

11. Emoji in a text setting are kept as `\u` escapes in the file and in local storage, and read back as typed. .NET's JSON writer escapes every character outside the basic plane whatever it is told, and writing the JSON by hand to avoid that was judged the worse choice.
12. The state has a second event, `SaveFailedChanged`, beside `SaveFailed`, because a save ends after the change is announced and the panel needs telling.
13. A load that ends badly, a handler that throws while a load is announced, or a load that is cancelled never leaves the state unable to change a setting for the rest of the session.
14. The editor stops waiting for settings when it is disposed.
15. In the browser, the two tests that drive the panel's controls (the font size, and the minimap with "Restore defaults") run at 1920 by 1080 and at 1024 by 768. The four about storage, a second visitor and focus run at the size the other browser tests use, 1600 by 900: at 1024 wide the sidebar's tabs fold into a menu, and nothing those four prove depends on the window's size.
