# Settings Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Blazemoji settings of its own: a section is a class, a setting is an attributed property, each visitor or window has its own set, and five editor options prove it on both hosts.

**Architecture:** The layout is the pattern (`ILayoutStore`, `LayoutState`, `LocalStorageLayoutStore`, `FileLayoutStore`). The model, the check on a value, the kept text and the store interface are plain C# in `Blazemoji.Core`. `SettingsState` in `Blazemoji.Components` holds one set of sections for a session, and is the only thing that changes a setting. Each host supplies a store. The panel is drawn from the attributes, and the editor brings itself into line after a redraw, as it already does for its colours.

**Tech Stack:** .NET 10, Blazor (Server for the website, Hermes 1.2.0 for the desktop), MudBlazor 9.11.0, BlazorMonaco 3.5.0, System.Text.Json, xUnit v3 on Microsoft.Testing.Platform, Shouldly, NSubstitute, bUnit 2.11.3, Playwright.

**Spec:** `docs/superpowers/specs/2026-10-09-settings-core-design.md`. Read it before starting a task: this plan argues from it.

## Global Constraints

- The branch is `settings-core`, made from `editor-tabs` as it stood on 2026-10-09 (`9d795d7`), on Tom's word that it was only waiting to be pushed. This plan was written against those files.
- No reference to Mythetech.Framework and no message bus. Every project stays on `net10.0`.
- Only `SettingsState` changes a setting. A setting's setter is `private`, and settings are declared on the section class itself, not on a base class of it.
- A setting is a `bool`, an `int`, a `double` or a `string`. Nothing else.
- A string setting holds at most 1,000 characters (`SettingProperty.LongestText`).
- A store reports storage that cannot be used as `ProjectStoreException`, and nothing else.
- `SettingsState.LoadAsync` is only called after the first render or from something the user did. Never from `OnInitialized` or `OnInitializedAsync`, which also run while the page is prerendered.
- No lock is added anywhere. Hermes 1.2.0 leaves a contended `SemaphoreSlim` taken with nobody holding it (see "Things to know" in `docs/hosting.md`).
- No async call is left unawaited, with one exception: `InvokeAsync(StateHasChanged);` as a plain statement in a `void` handler of a state's event.
- Components: events are `EventCallback`, required parameters have `[EditorRequired]`, loops have `@key`, styles are classes or a `.razor.css` file and never `style="..."`, icons come from `BlazemojiIcons`.
- Words shown to a person are fixed. `exception.Message` is never shown.
- No em dashes in code, comments or text. Comments say why, not what.
- Commit messages are one plain sentence about what the app now does, as in `git log`. No co-author trailer.
- Tests: xUnit with Shouldly, NSubstitute for interfaces, bUnit for components. A new test must fail before the code is written, and no `await` in a new test may wait on something that only finishes once the code exists: keep the task and assert on `IsCompleted` first.
- No test asserts what a MudBlazor or BlazorMonaco component does once drawn. Asserting that one is there, or what this app handed it, is fine.

## Review Focus

What the spec implies and its own test list does not exercise, most likely first. Each has a test in the task that owns the code.

1. **A settings file edited by hand into something that is not settings, including text nested hundreds deep.** The app starts with the defaults and says nothing. (Task 2)
2. **The browser's storage switched off or full, as in a private window.** The panel still opens, a change still takes effect, and the panel says it could not be saved. (Tasks 4 and 6)
3. **A number field emptied and left.** The setting stays within its limits; it is never 0 or blank. (Task 7)
4. **A setting changed before the editor has finished starting,** by someone quick or on a slow line. The editor has it when it appears. (Task 8)
5. **Text with emoji or accents in a text setting.** It comes back as it was typed, and is readable in the file. (Task 2)

## How to run things

```bash
# One test class, on your own machine (seconds):
dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.SettingsJsonTests

# Everything, with the real compiler, in a container (the one to trust before a commit that ends a task):
scripts/test-in-docker.sh

# The browser tests, against the two containers:
scripts/e2e.sh

# The desktop app checking itself:
scripts/desktop-smoke.sh
```

`--filter-class` takes the full name. Do not give `--filter-class` and `--filter-method` in one run: they are joined with "and" and silently match nothing. Docker on this Mac has a small disk: use the scripts, and never run `docker system prune` or `docker builder prune`.

## Files

| File | New or changed | What it is for |
| --- | --- | --- |
| `Blazemoji.Core/Shared/Models/Settings/SettingHosts.cs` | new | Which hosts a section or setting belongs to |
| `Blazemoji.Core/Shared/Models/Settings/SettingAttribute.cs` | new | What is said about one setting |
| `Blazemoji.Core/Shared/Models/Settings/SettingsBase.cs` | new | What every section has |
| `Blazemoji.Core/Services/Settings/SettingProperties.cs` | new | The settings of a section type, their defaults, and the one check on a value |
| `Blazemoji.Core/Services/Settings/SettingsJson.cs` | new | Settings as the text a store keeps |
| `Blazemoji.Core/Services/Settings/SettingsOptions.cs` | new | The host and the section types |
| `Blazemoji.Core/Services/Settings/ISettingsStore.cs` | new | Where the text is kept |
| `Blazemoji.Core/Services/Settings/FileSettingsStore.cs` | new | The desktop's store |
| `Blazemoji.Components/Shared/State/SettingsState.cs` | new | The state |
| `Blazemoji.Components/Settings/EditorSettings.cs` | new | The first section |
| `Blazemoji.Components/Settings/EditorOptionValues.cs` | new | The five values as Monaco wants them |
| `Blazemoji.Components/Components/Settings/*.razor` | new | `SettingsButton`, `SettingsDialog`, `SettingsSectionView`, `SettingRow`, four editors |
| `Blazemoji.Components/BlazemojiEditorRegistration.cs` | changed | Registers the state, the first section and the desktop's store |
| `Blazemoji.Components/BlazemojiIcons.cs` | changed | Two icons |
| `Blazemoji.Components/_Imports.razor` | changed | Three usings |
| `Blazemoji.Components/Layout/AppShell.razor` | changed | The Settings button |
| `Blazemoji.Components/Components/EmojiCodeEditor.razor` | changed | Takes its options from the state |
| `Blazemoji/Services/Settings/LocalStorageSettingsStore.cs` | new | The website's store |
| `Blazemoji/Program.cs` | changed | Registers it |
| `Blazemoji.Desktop/Program.cs` | changed | One state for the app, and says it is the desktop |
| `Blazemoji.Desktop/Scripts/smoke.ts`, `Blazemoji.Desktop/Smoke/SmokeRun.razor` | changed | One more check on each side |
| `Blazemoji.E2E/EditorPage.cs`, `Blazemoji.E2E/SettingsFlowsTests.cs` | changed, new | The browser tests |
| `docs/hosting.md` | changed | What a host does, and how to add a setting |
| `Blazemoji.Test/Settings/*.cs` | new | The tests of the above |

---

### Task 0: The branch, with the spec and this plan in it

**Files:**
- Create: `docs/superpowers/specs/2026-10-09-settings-core-design.md` and `docs/superpowers/plans/2026-10-09-settings-core.md` in the worktree (copied from the main checkout, where they are untracked)

- [x] **Step 1: The base**

As written this step stopped unless `editor-tabs` was in `main`. On 2026-10-09 it was not, and Tom said: "it's just waiting on the push, you can branch from there and we should get a clean rebase later if we want it". So the branch starts from `editor-tabs` at `9d795d7`.

- [x] **Step 2: Make the worktree** with superpowers:using-git-worktrees, on a new branch `settings-core` from `editor-tabs`. It is a folder beside the main checkout.

- [x] **Step 3: Bring the two documents in and commit**

```bash
mkdir -p docs/superpowers/specs docs/superpowers/plans
cp ../Blazemoji/docs/superpowers/specs/2026-10-09-settings-core-design.md docs/superpowers/specs/
cp ../Blazemoji/docs/superpowers/plans/2026-10-09-settings-core.md docs/superpowers/plans/
git add docs/superpowers/specs/2026-10-09-settings-core-design.md docs/superpowers/plans/2026-10-09-settings-core.md
git commit -m "The spec and plan for settings"
```

- [ ] **Step 4: See that the tests pass before anything is changed**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Layout.FileLayoutStoreTests`
Expected: `succeeded: 12`, `failed: 0`.

---

### Task 1: A section, a setting, and the check on a value

**Files:**
- Create: `Blazemoji.Core/Shared/Models/Settings/SettingHosts.cs`
- Create: `Blazemoji.Core/Shared/Models/Settings/SettingAttribute.cs`
- Create: `Blazemoji.Core/Shared/Models/Settings/SettingsBase.cs`
- Create: `Blazemoji.Core/Services/Settings/SettingProperties.cs`
- Test: `Blazemoji.Test/Settings/SampleSettings.cs`, `Blazemoji.Test/Settings/SettingPropertiesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces, in namespace `Blazemoji.Shared.Models.Settings`: `SettingHosts { None, Web, Desktop, All }`; `SettingAttribute` with `Label`, `Description`, `Group`, `Order`, `Min`, `Max`, `Step`, `Options`, `Hide`, `Hosts`, `HasRange`; `SettingsBase` with `SettingsId`, `DisplayName`, `Icon`, `Order`, `Hosts`.
- Produces, in namespace `Blazemoji.Services.Settings`: `SettingProperty` with `Name`, `Type`, `Attribute`, `Default`, `Options`, `IsFor(SettingHosts)`, `Read(SettingsBase)`, `Assign(SettingsBase, object?)`, `TryAccept(object?, out object?)`, `const int LongestText = 1000`; `SettingGroup(string Name, IReadOnlyList<SettingProperty> Settings)`; `SettingProperties.Of(Type)`, `SettingProperties.Of(SettingsBase)`, `SettingProperties.Grouped(IEnumerable<SettingProperty>)`.
- Produces, for later tests: `SampleSettings` and `DesktopOnlySettings` in `Blazemoji.Test.Settings`.

- [ ] **Step 1: Write the two sections the tests use**

`Blazemoji.Test/Settings/SampleSettings.cs`:

```csharp
using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Test.Settings
{
    /// <summary>A section with one setting of each kind, one that is hidden and one for the desktop alone.</summary>
    internal sealed class SampleSettings : SettingsBase
    {
        public override string SettingsId => "sample";

        public override string DisplayName => "Sample";

        public override string Icon => string.Empty;

        [Setting(Label = "Size", Group = "Numbers", Order = 1, Min = 8, Max = 32)]
        public int Size { get; private set; } = 14;

        [Setting(Label = "Ratio", Group = "Numbers", Order = 2, Min = 0.5, Max = 3, Step = 0.1)]
        public double Ratio { get; private set; } = 1.5;

        [Setting(Label = "Wrap", Description = "Carry long lines over.", Group = "Switches", Order = 3)]
        public bool Wrap { get; private set; }

        [Setting(Label = "Whitespace", Group = "Choices", Order = 4, Options = "none,selection,all")]
        public string Whitespace { get; private set; } = "selection";

        [Setting(Label = "Name", Group = "Text", Order = 5)]
        public string Name { get; private set; } = "plain";

        [Setting(Label = "Remembered", Order = 6, Hide = true)]
        public int Remembered { get; private set; }

        [Setting(Label = "Channel", Group = "Desktop", Order = 7, Hosts = SettingHosts.Desktop)]
        public string Channel { get; private set; } = "stable";

        public int NotASetting { get; set; } = 1;
    }

    /// <summary>A whole section the website does not have.</summary>
    internal sealed class DesktopOnlySettings : SettingsBase
    {
        public override string SettingsId => "desktopOnly";

        public override string DisplayName => "Desktop only";

        public override string Icon => string.Empty;

        public override int Order => 90;

        public override SettingHosts Hosts => SettingHosts.Desktop;

        [Setting(Label = "Check at start")]
        public bool CheckAtStart { get; private set; } = true;
    }
}
```

- [ ] **Step 2: Write the failing tests**

`Blazemoji.Test/Settings/SettingPropertiesTests.cs`:

```csharp
using Blazemoji.Services.Settings;
using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Test.Settings
{
    public class SettingPropertiesTests
    {
        private static SettingProperty Setting(string name) =>
            SettingProperties.Of(typeof(SampleSettings)).Single(setting => setting.Name == name);

        [Fact]
        public void A_sections_settings_are_its_attributed_properties_in_their_order()
        {
            SettingProperties.Of(new SampleSettings()).Select(setting => setting.Name)
                .ShouldBe(["Size", "Ratio", "Wrap", "Whitespace", "Name", "Remembered", "Channel"]);
        }

        [Fact]
        public void The_default_of_a_setting_is_what_a_new_section_has()
        {
            Setting("Size").Default.ShouldBe(14);
            Setting("Ratio").Default.ShouldBe(1.5);
            Setting("Wrap").Default.ShouldBe(false);
            Setting("Whitespace").Default.ShouldBe("selection");
        }

        [Fact]
        public void A_choice_knows_its_options_and_free_text_has_none()
        {
            Setting("Whitespace").Options.ShouldBe(["none", "selection", "all"]);
            Setting("Name").Options.ShouldBeEmpty();
        }

        [Theory]
        [InlineData("Size", 20, 20)]
        [InlineData("Size", 99, 32)]
        [InlineData("Size", 2, 8)]
        [InlineData("Size", 20.0, 20)]
        [InlineData("Ratio", 2, 2.0)]
        [InlineData("Ratio", 9.5, 3.0)]
        [InlineData("Ratio", 0.1, 0.5)]
        [InlineData("Wrap", true, true)]
        [InlineData("Whitespace", "all", "all")]
        [InlineData("Name", "anything at all", "anything at all")]
        public void A_value_of_the_right_kind_is_taken_and_a_number_is_brought_within_its_limits(string setting, object value, object accepted)
        {
            Setting(setting).TryAccept(value, out var taken).ShouldBeTrue();

            taken.ShouldBe(accepted);
        }

        [Theory]
        [InlineData("Size", 20.5)]
        [InlineData("Size", "20")]
        [InlineData("Size", true)]
        [InlineData("Size", null)]
        [InlineData("Ratio", double.NaN)]
        [InlineData("Ratio", double.PositiveInfinity)]
        [InlineData("Ratio", "1.5")]
        [InlineData("Wrap", 1)]
        [InlineData("Wrap", "true")]
        [InlineData("Whitespace", "everything")]
        [InlineData("Whitespace", "All")]
        [InlineData("Name", 5)]
        [InlineData("Name", null)]
        public void A_value_of_the_wrong_kind_or_not_among_the_options_is_refused(string setting, object? value)
        {
            Setting(setting).TryAccept(value, out _).ShouldBeFalse();
        }

        [Fact]
        public void Text_is_taken_up_to_its_limit_and_refused_beyond_it()
        {
            Setting("Name").TryAccept(new string('a', SettingProperty.LongestText), out _).ShouldBeTrue();
            Setting("Name").TryAccept(new string('a', SettingProperty.LongestText + 1), out _).ShouldBeFalse();
        }

        [Fact]
        public void A_setting_is_assigned_through_its_private_setter()
        {
            var section = new SampleSettings();

            Setting("Size").Assign(section, 20);

            section.Size.ShouldBe(20);
            Setting("Size").Read(section).ShouldBe(20);
        }

        [Fact]
        public void A_setting_says_which_hosts_it_is_for()
        {
            Setting("Size").IsFor(SettingHosts.Web).ShouldBeTrue();
            Setting("Size").IsFor(SettingHosts.Desktop).ShouldBeTrue();
            Setting("Channel").IsFor(SettingHosts.Web).ShouldBeFalse();
            Setting("Channel").IsFor(SettingHosts.Desktop).ShouldBeTrue();
        }

        [Fact]
        public void Groups_come_in_the_order_each_first_appears_with_their_settings_in_order()
        {
            var groups = SettingProperties.Grouped(SettingProperties.Of(typeof(SampleSettings)));

            groups.Select(group => group.Name).ShouldBe(["Numbers", "Switches", "Choices", "Text", "", "Desktop"]);
            groups[0].Settings.Select(setting => setting.Name).ShouldBe(["Size", "Ratio"]);
        }

        [Fact]
        public void A_setting_of_a_kind_that_is_not_supported_is_refused_when_the_section_is_first_read()
        {
            Should.Throw<NotSupportedException>(() => SettingProperties.Of(typeof(HasADate)));
        }

        private sealed class HasADate : SettingsBase
        {
            public override string SettingsId => "dated";

            public override string DisplayName => "Dated";

            public override string Icon => string.Empty;

            [Setting(Label = "When")]
            public DateTime When { get; private set; }
        }
    }
}
```

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.SettingPropertiesTests`
Expected: the build fails: `The type or namespace name 'Settings' does not exist in the namespace 'Blazemoji.Services'`.

- [ ] **Step 4: Write the model**

`Blazemoji.Core/Shared/Models/Settings/SettingHosts.cs`:

```csharp
namespace Blazemoji.Shared.Models.Settings
{
    /// <summary>Which hosts a section or a setting belongs to. Most belong to both.</summary>
    [Flags]
    public enum SettingHosts
    {
        None = 0,
        Web = 1,
        Desktop = 2,
        All = Web | Desktop,
    }
}
```

`Blazemoji.Core/Shared/Models/Settings/SettingAttribute.cs`:

```csharp
namespace Blazemoji.Shared.Models.Settings
{
    /// <summary>
    /// Marks a property of a section as a setting and says how the panel shows it. The names
    /// are Mythetech.Framework's, so a section written for one reads the same in the other.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class SettingAttribute : Attribute
    {
        public string Label { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>The heading it is shown under. Settings with none come under no heading.</summary>
        public string? Group { get; set; }

        public int Order { get; set; } = 100;

        public double Min { get; set; } = double.NaN;

        public double Max { get; set; } = double.NaN;

        public double Step { get; set; } = 1;

        /// <summary>For a string: the values it may have, separated by commas. With these it is a choice, not free text.</summary>
        public string? Options { get; set; }

        /// <summary>Kept like any other, but not shown in the panel.</summary>
        public bool Hide { get; set; }

        public SettingHosts Hosts { get; set; } = SettingHosts.All;

        public bool HasRange => !double.IsNaN(Min) && !double.IsNaN(Max);
    }
}
```

`Blazemoji.Core/Shared/Models/Settings/SettingsBase.cs`:

```csharp
namespace Blazemoji.Shared.Models.Settings
{
    /// <summary>
    /// A section of settings. Each setting is a property of the section's own class with a
    /// <see cref="SettingAttribute"/> and a private setter: what a new section holds is the
    /// default, and only the settings state assigns anything else.
    /// </summary>
    public abstract class SettingsBase
    {
        /// <summary>What the section is kept under. It must not change once people have settings.</summary>
        public abstract string SettingsId { get; }

        public abstract string DisplayName { get; }

        public abstract string Icon { get; }

        public virtual int Order => 50;

        public virtual SettingHosts Hosts => SettingHosts.All;
    }
}
```

- [ ] **Step 5: Write the properties and the check**

`Blazemoji.Core/Services/Settings/SettingProperties.cs`:

```csharp
using System.Collections.Concurrent;
using System.Reflection;
using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Services.Settings
{
    /// <summary>One setting of a section: its property, what its attribute says, and its default.</summary>
    public sealed class SettingProperty
    {
        /// <summary>The longest text a setting may hold. A page can send anything, so the limit is here and not on a field.</summary>
        public const int LongestText = 1000;

        private readonly PropertyInfo _property;

        internal SettingProperty(PropertyInfo property, SettingAttribute attribute, object? defaultValue)
        {
            if (property.PropertyType != typeof(bool) && property.PropertyType != typeof(int)
                && property.PropertyType != typeof(double) && property.PropertyType != typeof(string))
                throw new NotSupportedException($"{property.DeclaringType?.Name}.{property.Name} is a {property.PropertyType.Name}. A setting is a bool, an int, a double or a string.");

            if (property.SetMethod is null)
                throw new NotSupportedException($"{property.DeclaringType?.Name}.{property.Name} has no setter. A setting needs one, and it may be private.");

            _property = property;
            Attribute = attribute;
            Default = defaultValue;
            Options = string.IsNullOrWhiteSpace(attribute.Options)
                ? []
                : attribute.Options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public string Name => _property.Name;

        public Type Type => _property.PropertyType;

        public SettingAttribute Attribute { get; }

        public object? Default { get; }

        public IReadOnlyList<string> Options { get; }

        public bool IsFor(SettingHosts host) => (Attribute.Hosts & host) != 0;

        public object? Read(SettingsBase section) => _property.GetValue(section);

        /// <summary>For the settings state alone, which is the one place a setting is changed.</summary>
        public void Assign(SettingsBase section, object? value) => _property.SetValue(section, value);

        /// <summary>
        /// Whether a value may be this setting's, and what it becomes: a number is brought
        /// within the limits. The panel and the kept text both go through here, so nothing
        /// gets in from one that could not get in from the other.
        /// </summary>
        public bool TryAccept(object? value, out object? accepted)
        {
            accepted = null;

            if (Type == typeof(bool))
            {
                if (value is not bool flag)
                    return false;

                accepted = flag;
                return true;
            }

            if (Type == typeof(string))
            {
                if (value is not string text || text.Length > LongestText)
                    return false;

                if (Options.Count > 0 && !Options.Contains(text, StringComparer.Ordinal))
                    return false;

                accepted = text;
                return true;
            }

            double number;
            switch (value)
            {
                case int whole: number = whole; break;
                case long whole: number = whole; break;
                case double fraction: number = fraction; break;
                default: return false;
            }

            if (!double.IsFinite(number))
                return false;

            if (Type == typeof(double))
            {
                accepted = Within(number);
                return true;
            }

            if (number != Math.Truncate(number))
                return false;

            number = Within(number);
            if (number < int.MinValue || number > int.MaxValue)
                return false;

            accepted = (int)number;
            return true;
        }

        private double Within(double number) => Attribute.HasRange ? Math.Clamp(number, Attribute.Min, Attribute.Max) : number;
    }

    /// <summary>The settings shown under one heading. Those with no heading have an empty name.</summary>
    public sealed record SettingGroup(string Name, IReadOnlyList<SettingProperty> Settings);

    /// <summary>The settings of each section type, read once.</summary>
    public static class SettingProperties
    {
        private static readonly ConcurrentDictionary<Type, IReadOnlyList<SettingProperty>> Known = new();

        public static IReadOnlyList<SettingProperty> Of(SettingsBase section) => Of(section.GetType());

        public static IReadOnlyList<SettingProperty> Of(Type sectionType) => Known.GetOrAdd(sectionType, Read);

        /// <summary>Groups in the order each first appears, with the settings in the order given.</summary>
        public static IReadOnlyList<SettingGroup> Grouped(IEnumerable<SettingProperty> settings)
        {
            var groups = new List<(string Name, List<SettingProperty> Settings)>();
            foreach (var setting in settings)
            {
                var name = setting.Attribute.Group ?? string.Empty;
                var group = groups.FirstOrDefault(candidate => candidate.Name == name);
                if (group.Settings is null)
                    groups.Add(group = (name, new List<SettingProperty>()));

                group.Settings.Add(setting);
            }

            return groups.Select(group => new SettingGroup(group.Name, group.Settings)).ToList();
        }

        private static IReadOnlyList<SettingProperty> Read(Type sectionType)
        {
            if (!typeof(SettingsBase).IsAssignableFrom(sectionType) || sectionType.GetConstructor(Type.EmptyTypes) is null)
                throw new NotSupportedException($"{sectionType.Name} must derive from {nameof(SettingsBase)} and have a constructor that takes nothing.");

            // A new one is what holds the defaults: there is nowhere else they are written.
            var fresh = (SettingsBase)Activator.CreateInstance(sectionType)!;
            return sectionType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => (Property: property, Attribute: property.GetCustomAttribute<SettingAttribute>()))
                .Where(found => found.Attribute is not null)
                .OrderBy(found => found.Attribute!.Order)
                .ThenBy(found => found.Property.Name, StringComparer.Ordinal)
                .Select(found => new SettingProperty(found.Property, found.Attribute!, found.Property.GetValue(fresh)))
                .ToList();
        }
    }
}
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.SettingPropertiesTests`
Expected: `failed: 0`.

- [ ] **Step 7: Commit**

```bash
git add Blazemoji.Core/Shared/Models/Settings Blazemoji.Core/Services/Settings/SettingProperties.cs Blazemoji.Test/Settings
git commit -m "A setting can be declared, and a value for one checked"
```

---

### Task 2: Settings as the text a store keeps

**Files:**
- Create: `Blazemoji.Core/Services/Settings/SettingsJson.cs`
- Test: `Blazemoji.Test/Settings/SettingsJsonTests.cs`

**Interfaces:**
- Consumes: `SettingProperties.Of`, `SettingProperty.Read`, `Assign`, `TryAccept`, `IsFor`, `Default` from Task 1; `SampleSettings`, `DesktopOnlySettings`.
- Produces, in `Blazemoji.Services.Settings`: `KeptSetting(SettingsBase Section, SettingProperty Setting, object? Value)`; `SettingsJson.Write(IEnumerable<SettingsBase> sections, SettingHosts host) : string`; `SettingsJson.Read(string? text, IEnumerable<SettingsBase> sections, SettingHosts host) : IReadOnlyList<KeptSetting>`. `Read` assigns nothing: it returns only values that `TryAccept` took, already brought within limits.

- [ ] **Step 1: Write the failing tests**

`Blazemoji.Test/Settings/SettingsJsonTests.cs`:

```csharp
using Blazemoji.Services.Settings;
using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Test.Settings
{
    public class SettingsJsonTests
    {
        private readonly SampleSettings _sample = new();
        private readonly DesktopOnlySettings _desktopOnly = new();

        private SettingsBase[] Sections => [_sample, _desktopOnly];

        private static void Set(SettingsBase section, string name, object? value) =>
            SettingProperties.Of(section).Single(setting => setting.Name == name).Assign(section, value);

        private IReadOnlyList<KeptSetting> Read(string? text, SettingHosts host = SettingHosts.Desktop) =>
            SettingsJson.Read(text, Sections, host);

        private static object? ValueOf(IReadOnlyList<KeptSetting> kept, string name) =>
            kept.Single(one => one.Setting.Name == name).Value;

        [Fact]
        public void With_everything_at_its_default_nothing_is_written()
        {
            SettingsJson.Write(Sections, SettingHosts.Desktop).Trim().ShouldBe("{}");
        }

        [Fact]
        public void Only_what_was_changed_is_written_under_its_sections_id()
        {
            Set(_sample, "Size", 20);
            Set(_sample, "Wrap", true);

            var text = SettingsJson.Write(Sections, SettingHosts.Desktop);

            text.ShouldContain("\"sample\": {");
            text.ShouldContain("\"size\": 20");
            text.ShouldContain("\"wrap\": true");
            text.ShouldNotContain("ratio");
            text.ShouldNotContain("desktopOnly");
        }

        [Fact]
        public void What_was_written_is_read_back_the_same()
        {
            Set(_sample, "Size", 20);
            Set(_sample, "Ratio", 2.25);
            Set(_sample, "Wrap", true);
            Set(_sample, "Whitespace", "all");
            Set(_sample, "Name", "other");
            Set(_desktopOnly, "CheckAtStart", false);

            var kept = SettingsJson.Read(SettingsJson.Write(Sections, SettingHosts.Desktop), [new SampleSettings(), new DesktopOnlySettings()], SettingHosts.Desktop);

            ValueOf(kept, "Size").ShouldBe(20);
            ValueOf(kept, "Ratio").ShouldBe(2.25);
            ValueOf(kept, "Wrap").ShouldBe(true);
            ValueOf(kept, "Whitespace").ShouldBe("all");
            ValueOf(kept, "Name").ShouldBe("other");
            ValueOf(kept, "CheckAtStart").ShouldBe(false);
        }

        [Fact]
        public void A_section_and_a_setting_for_the_other_host_are_neither_written_nor_read()
        {
            Set(_sample, "Channel", "beta");
            Set(_desktopOnly, "CheckAtStart", false);

            SettingsJson.Write(Sections, SettingHosts.Web).Trim().ShouldBe("{}");
            Read("{ \"sample\": { \"channel\": \"beta\" }, \"desktopOnly\": { \"checkAtStart\": false } }", SettingHosts.Web).ShouldBeEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not json at all")]
        [InlineData("[1, 2, 3]")]
        [InlineData("null")]
        [InlineData("\"sample\"")]
        [InlineData("{ \"sample\": 5 }")]
        [InlineData("{ \"sample\": { \"size\": 20 ")]
        public void Text_that_is_not_settings_is_no_settings(string? text)
        {
            Read(text).ShouldBeEmpty();
        }

        [Fact]
        public void Text_nested_deeper_than_json_allows_is_no_settings()
        {
            var deep = new string('[', 500) + new string(']', 500);

            Read("{ \"sample\": { \"size\": " + deep + " } }").ShouldBeEmpty();
        }

        [Fact]
        public void What_this_version_does_not_know_is_passed_over()
        {
            var kept = Read("{ \"gone\": { \"size\": 1 }, \"sample\": { \"gone\": 1, \"notASetting\": 9, \"size\": 20 } }");

            kept.ShouldHaveSingleItem().Value.ShouldBe(20);
        }

        [Fact]
        public void A_value_that_would_be_refused_leaves_that_setting_alone_and_the_rest_are_still_read()
        {
            var kept = Read("{ \"sample\": { \"size\": \"big\", \"whitespace\": \"everything\", \"wrap\": 1, \"ratio\": 2 } }");

            kept.ShouldHaveSingleItem().Setting.Name.ShouldBe("Ratio");
        }

        [Fact]
        public void A_number_out_of_range_is_brought_within_it()
        {
            ValueOf(Read("{ \"sample\": { \"size\": 400 } }"), "Size").ShouldBe(32);
            ValueOf(Read("{ \"sample\": { \"size\": -400 } }"), "Size").ShouldBe(8);
        }

        [Fact]
        public void A_number_too_large_to_be_a_number_leaves_the_default()
        {
            Read("{ \"sample\": { \"size\": 1e999 } }").ShouldBeEmpty();
        }

        [Fact]
        public void Text_over_the_limit_leaves_the_default()
        {
            Read("{ \"sample\": { \"name\": \"" + new string('a', SettingProperty.LongestText + 1) + "\" } }").ShouldBeEmpty();
        }

        [Fact]
        public void Emoji_and_accents_come_back_as_typed_and_can_be_read_in_the_file()
        {
            Set(_sample, "Name", "Café 🍇 für");

            var text = SettingsJson.Write(Sections, SettingHosts.Desktop);

            text.ShouldContain("Café 🍇 für");
            ValueOf(SettingsJson.Read(text, [new SampleSettings()], SettingHosts.Desktop), "Name").ShouldBe("Café 🍇 für");
        }

        [Fact]
        public void A_hidden_setting_is_kept_like_any_other()
        {
            Set(_sample, "Remembered", 7);

            ValueOf(Read(SettingsJson.Write(Sections, SettingHosts.Desktop)), "Remembered").ShouldBe(7);
        }
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.SettingsJsonTests`
Expected: the build fails: `The name 'SettingsJson' does not exist in the current context`.

- [ ] **Step 3: Write `SettingsJson`**

`Blazemoji.Core/Services/Settings/SettingsJson.cs`:

```csharp
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Services.Settings
{
    /// <summary>A value the kept text gives a setting, already checked and brought within its limits.</summary>
    public sealed record KeptSetting(SettingsBase Section, SettingProperty Setting, object? Value);

    /// <summary>
    /// Settings as the text a store keeps, the same wherever it is kept: one object for each
    /// section, holding only what is not at its default. Reading forgives. Text that is not
    /// such an object is no settings, and a value that could not have come from the panel is
    /// passed over.
    /// </summary>
    public static class SettingsJson
    {
        // Text is written as it is, not as \u escapes: the file is for a person to read too,
        // and it is never put inside a page.
        private static readonly JsonWriterOptions Readable = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static string Write(IEnumerable<SettingsBase> sections, SettingHosts host)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, Readable))
            {
                writer.WriteStartObject();
                foreach (var section in sections.Where(section => (section.Hosts & host) != 0))
                {
                    var changed = SettingProperties.Of(section)
                        .Where(setting => setting.IsFor(host) && !Equals(setting.Read(section), setting.Default))
                        .ToList();
                    if (changed.Count == 0)
                        continue;

                    writer.WriteStartObject(section.SettingsId);
                    foreach (var setting in changed)
                    {
                        writer.WritePropertyName(KeyOf(setting));
                        switch (setting.Read(section))
                        {
                            case bool flag: writer.WriteBooleanValue(flag); break;
                            case int whole: writer.WriteNumberValue(whole); break;
                            case double fraction: writer.WriteNumberValue(fraction); break;
                            case string text: writer.WriteStringValue(text); break;
                            default: writer.WriteNullValue(); break;
                        }
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
        }

        public static IReadOnlyList<KeptSetting> Read(string? text, IEnumerable<SettingsBase> sections, SettingHosts host)
        {
            if (string.IsNullOrWhiteSpace(text))
                return [];

            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return [];

                var kept = new List<KeptSetting>();
                foreach (var section in sections.Where(section => (section.Hosts & host) != 0))
                {
                    if (!document.RootElement.TryGetProperty(section.SettingsId, out var values) || values.ValueKind != JsonValueKind.Object)
                        continue;

                    foreach (var setting in SettingProperties.Of(section).Where(setting => setting.IsFor(host)))
                    {
                        if (values.TryGetProperty(KeyOf(setting), out var value) && setting.TryAccept(Plain(value), out var accepted))
                            kept.Add(new KeptSetting(section, setting, accepted));
                    }
                }

                return kept;
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static string KeyOf(SettingProperty setting) => JsonNamingPolicy.CamelCase.ConvertName(setting.Name);

        // A number too large to be one comes out as infinity or not at all, and either way is refused.
        private static object? Plain(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String => value.GetString(),
            _ => null,
        };
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.SettingsJsonTests`
Expected: `failed: 0`. If `Text_nested_deeper_than_json_allows_is_no_settings` fails with an exception other than `JsonException`, catch that exception type in `Read` too: no text may make reading throw.

- [ ] **Step 5: Commit**

```bash
git add Blazemoji.Core/Services/Settings/SettingsJson.cs Blazemoji.Test/Settings/SettingsJsonTests.cs
git commit -m "Settings can be written as text and read back, forgiving what is not settings"
```

---

### Task 3: A store, and the desktop's on disk

**Files:**
- Create: `Blazemoji.Core/Services/Settings/ISettingsStore.cs`
- Create: `Blazemoji.Core/Services/Settings/FileSettingsStore.cs`
- Modify: `Blazemoji.Components/BlazemojiEditorRegistration.cs` (in `AddBlazemojiProjectsOnDisk`, after the line that registers `FileLayoutStore`)
- Test: `Blazemoji.Test/Settings/FileSettingsStoreTests.cs`
- Modify: `Blazemoji.Test/HostingTests.cs` (the test `A_host_that_keeps_projects_on_disk_gets_both_stores_from_one_call_and_their_folder_from_configuration`)

**Interfaces:**
- Consumes: `ProjectsFolder` (internal to Core: `Root`, `OwnFolder`, `InTurn`, `ReadTextAsync`, `WriteTextAsync`), `FileProjectStoreOptions`, `ProjectStoreException`, all in `Blazemoji.Services.Projects`.
- Produces, in `Blazemoji.Services.Settings`: `ISettingsStore { Task<string?> LoadAsync(); Task SaveAsync(string settings); }`; `FileSettingsStore(IOptions<FileProjectStoreOptions> options, TimeProvider clock)`.

- [ ] **Step 1: Write the failing tests**

`Blazemoji.Test/Settings/FileSettingsStoreTests.cs`:

```csharp
using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Settings
{
    public sealed class FileSettingsStoreTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "settings-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private FileSettingsStore Create() =>
            new(Options.Create(new FileProjectStoreOptions { Root = _root }), TimeProvider.System);

        private string SettingsFile => Path.Combine(_root, ".blazemoji", "settings.json");

        [Fact]
        public async Task With_nothing_kept_there_is_nothing_and_no_folder_is_made_by_looking()
        {
            (await Create().LoadAsync()).ShouldBeNull();
            Directory.Exists(_root).ShouldBeFalse();
        }

        [Fact]
        public async Task What_was_kept_is_read_back_by_another_store_on_the_same_folder()
        {
            await Create().SaveAsync("{ \"editor\": { \"fontSize\": 16 } }\n");

            (await Create().LoadAsync()).ShouldBe("{ \"editor\": { \"fontSize\": 16 } }\n");
        }

        [Fact]
        public async Task It_is_one_file_in_the_apps_own_folder_beside_where_the_layout_goes()
        {
            await Create().SaveAsync("{}\n");

            File.Exists(SettingsFile).ShouldBeTrue();
            Directory.GetFiles(Path.GetDirectoryName(SettingsFile)!).ShouldBe([SettingsFile]);
        }

        [Fact]
        public async Task A_later_save_takes_the_place_of_the_earlier_one()
        {
            var store = Create();
            await store.SaveAsync("first");

            await store.SaveAsync("second");

            (await Create().LoadAsync()).ShouldBe("second");
        }

        [Fact]
        public async Task A_folder_that_cannot_be_written_to_is_reported_as_the_store_failing()
        {
            // A file where the projects folder should be: nothing can be made under it.
            Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
            await File.WriteAllTextAsync(_root, "in the way");
            try
            {
                await Should.ThrowAsync<ProjectStoreException>(() => Create().SaveAsync("{}\n"));
            }
            finally
            {
                File.Delete(_root);
            }
        }
    }
}
```

In `Blazemoji.Test/HostingTests.cs`, add `using Blazemoji.Services.Settings;` and, at the end of `A_host_that_keeps_projects_on_disk_gets_both_stores_from_one_call_and_their_folder_from_configuration`:

```csharp
            one.ServiceProvider.GetRequiredService<ISettingsStore>().ShouldBeOfType<FileSettingsStore>();
            one.ServiceProvider.GetRequiredService<ISettingsStore>().ShouldBeSameAs(two.ServiceProvider.GetRequiredService<ISettingsStore>());
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.FileSettingsStoreTests`
Expected: the build fails: `The type or namespace name 'FileSettingsStore' could not be found`.

- [ ] **Step 3: Write the interface and the store**

`Blazemoji.Core/Services/Settings/ISettingsStore.cs`:

```csharp
using Blazemoji.Services.Projects;

namespace Blazemoji.Services.Settings
{
    /// <summary>
    /// Where settings are kept between visits, as one piece of text that <see cref="SettingsJson"/>
    /// makes and reads. A host that has somewhere to keep it registers one; without one the
    /// settings last as long as the page does.
    /// </summary>
    /// <remarks>Both methods throw <see cref="ProjectStoreException"/> when the storage cannot be used.</remarks>
    public interface ISettingsStore
    {
        /// <returns>Null when nothing has been kept.</returns>
        Task<string?> LoadAsync();

        Task SaveAsync(string settings);
    }
}
```

`Blazemoji.Core/Services/Settings/FileSettingsStore.cs`:

```csharp
using Blazemoji.Services.Projects;
using Microsoft.Extensions.Options;

namespace Blazemoji.Services.Settings
{
    /// <summary>
    /// Keeps the settings as one file in the app's own folder under
    /// <see cref="FileProjectStoreOptions.Root"/>, beside the layout.
    /// </summary>
    public sealed class FileSettingsStore(IOptions<FileProjectStoreOptions> options, TimeProvider clock) : ISettingsStore
    {
        private const string SettingsFile = "settings.json";

        private readonly ProjectsFolder _disk = new(options.Value.Root, clock);

        private string SettingsPath => Path.Combine(_disk.Root, ProjectsFolder.OwnFolder, SettingsFile);

        public Task<string?> LoadAsync() => _disk.InTurn(() => ProjectsFolder.ReadTextAsync(SettingsPath));

        public Task SaveAsync(string settings) => _disk.InTurn(() => ProjectsFolder.WriteTextAsync(SettingsPath, settings));
    }
}
```

In `Blazemoji.Components/BlazemojiEditorRegistration.cs` add `using Blazemoji.Services.Settings;` and, in `AddBlazemojiProjectsOnDisk`, after `services.AddSingleton<ILayoutStore, FileLayoutStore>();`:

```csharp
            services.AddSingleton<ISettingsStore, FileSettingsStore>();
```

Change that method's summary sentence "projects as folders, and what is saved from the editor as files beside them" to end "..., with the layout and the settings in a folder of the app's own beside them".

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.FileSettingsStoreTests`
Then: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.HostingTests`
Expected: `failed: 0` for both.

- [ ] **Step 5: Commit**

```bash
git add Blazemoji.Core/Services/Settings/ISettingsStore.cs Blazemoji.Core/Services/Settings/FileSettingsStore.cs Blazemoji.Components/BlazemojiEditorRegistration.cs Blazemoji.Test/Settings/FileSettingsStoreTests.cs Blazemoji.Test/HostingTests.cs
git commit -m "The desktop keeps settings in a file beside the layout"
```

---

### Task 4: The state

**Files:**
- Create: `Blazemoji.Core/Services/Settings/SettingsOptions.cs`
- Create: `Blazemoji.Components/Shared/State/SettingsState.cs`
- Modify: `Blazemoji.Components/BlazemojiEditorRegistration.cs` (in `AddBlazemojiEditor`, with the other state classes)
- Test: `Blazemoji.Test/Settings/SettingsStateTests.cs`
- Modify: `Blazemoji.Test/HostingTests.cs`

**Interfaces:**
- Consumes: everything Tasks 1 to 3 produce; `RecordingLogger<T>` in `Blazemoji.Test.State`.
- Produces, in `Blazemoji.Services.Settings`: `SettingsOptions` with `SettingHosts Host { get; set; }` (default `Web`), `IReadOnlyList<Type> Sections`, `SettingsOptions Add<T>() where T : SettingsBase, new()`.
- Produces, in `Blazemoji.Shared.State`: `SettingsState(IOptions<SettingsOptions>, ILogger<SettingsState>, ISettingsStore? store = null)` with `IReadOnlyList<SettingsBase> Sections`, `T Get<T>()`, `IReadOnlyList<SettingProperty> ShownIn(SettingsBase)`, `bool IsAtDefaults(SettingsBase)`, `event Action<SettingsBase>? SettingsChanged`, `bool SaveFailed`, `event Action? SaveFailedChanged`, `Task LoadAsync()`, `Task SetAsync(SettingsBase section, string property, object? value)`, `Task RestoreDefaultsAsync(SettingsBase section)`.

`SaveFailedChanged` is not in the spec's sketch. The panel needs it: a save ends after `SettingsChanged` has been raised, so nothing else would tell the panel to draw the warning.

- [ ] **Step 1: Write the failing tests**

`Blazemoji.Test/Settings/SettingsStateTests.cs`:

```csharp
using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
using Blazemoji.Shared.Models.Settings;
using Blazemoji.Shared.State;
using Blazemoji.Test.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.Settings
{
    public class SettingsStateTests
    {
        private readonly ISettingsStore _store = Substitute.For<ISettingsStore>();
        private readonly RecordingLogger<SettingsState> _logger = new();
        private readonly List<string> _happened = [];
        private string? _kept;

        public SettingsStateTests()
        {
            _store.LoadAsync().Returns(_ => Task.FromResult(_kept));
            _store.SaveAsync(Arg.Any<string>()).Returns(call =>
            {
                _kept = call.Arg<string>();
                _happened.Add("saved");
                return Task.CompletedTask;
            });
        }

        private SettingsState CreateState(SettingHosts host = SettingHosts.Web, bool withStore = true)
        {
            var options = new SettingsOptions { Host = host };
            options.Add<SampleSettings>().Add<DesktopOnlySettings>();
            var state = new SettingsState(Options.Create(options), _logger, withStore ? _store : null);
            state.SettingsChanged += section => _happened.Add("changed " + section.SettingsId);
            state.SaveFailedChanged += () => _happened.Add("save failed is " + state.SaveFailed);
            return state;
        }

        private static ProjectStoreException StorageOff() => new("The browser's storage could not be used.", new IOException("off"));

        [Fact]
        public void Before_anything_is_loaded_every_setting_is_at_its_default()
        {
            var state = CreateState();

            state.Get<SampleSettings>().Size.ShouldBe(14);
            state.IsAtDefaults(state.Get<SampleSettings>()).ShouldBeTrue();
        }

        [Fact]
        public async Task Loading_takes_what_was_kept_and_says_so_once_for_each_section_that_changed()
        {
            _kept = "{ \"sample\": { \"size\": 20, \"wrap\": true } }";
            var state = CreateState();

            await state.LoadAsync();

            state.Get<SampleSettings>().Size.ShouldBe(20);
            state.Get<SampleSettings>().Wrap.ShouldBeTrue();
            _happened.ShouldBe(["changed sample"]);
        }

        [Fact]
        public async Task Loading_with_nothing_kept_announces_nothing_and_does_not_read_twice()
        {
            var state = CreateState();

            await state.LoadAsync();
            await state.LoadAsync();

            _happened.ShouldBeEmpty();
            await _store.Received(1).LoadAsync();
        }

        [Fact]
        public async Task A_change_is_assigned_then_announced_then_saved()
        {
            var state = CreateState();
            var sample = state.Get<SampleSettings>();
            state.SettingsChanged += _ => sample.Size.ShouldBe(20);

            await state.SetAsync(sample, nameof(SampleSettings.Size), 20);

            _happened.ShouldBe(["changed sample", "saved"]);
            _kept.ShouldNotBeNull().ShouldContain("\"size\": 20");
        }

        [Fact]
        public async Task A_value_equal_to_the_current_one_does_nothing()
        {
            var state = CreateState();

            await state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Size), 14);

            _happened.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_number_out_of_range_is_brought_within_it_and_a_refused_value_changes_nothing()
        {
            var state = CreateState();
            var sample = state.Get<SampleSettings>();

            await state.SetAsync(sample, nameof(SampleSettings.Size), 400);
            await state.SetAsync(sample, nameof(SampleSettings.Ratio), double.NaN);
            await state.SetAsync(sample, nameof(SampleSettings.Whitespace), "everything");
            await state.SetAsync(sample, nameof(SampleSettings.Name), new string('a', SettingProperty.LongestText + 1));

            sample.Size.ShouldBe(32);
            sample.Ratio.ShouldBe(1.5);
            sample.Whitespace.ShouldBe("selection");
            sample.Name.ShouldBe("plain");
            _happened.ShouldBe(["changed sample", "saved"]);
        }

        [Fact]
        public async Task What_is_not_a_setting_of_this_host_is_a_mistake_in_the_caller()
        {
            var state = CreateState();
            var other = CreateState();

            await Should.ThrowAsync<ArgumentException>(() => state.SetAsync(state.Get<SampleSettings>(), "NotASetting", 2));
            await Should.ThrowAsync<ArgumentException>(() => state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Channel), "beta"));
            await Should.ThrowAsync<ArgumentException>(() => state.SetAsync(state.Get<DesktopOnlySettings>(), nameof(DesktopOnlySettings.CheckAtStart), false));
            await Should.ThrowAsync<ArgumentException>(() => state.SetAsync(other.Get<SampleSettings>(), nameof(SampleSettings.Size), 20));
        }

        [Fact]
        public async Task A_change_made_while_the_store_is_still_answering_lands_on_top_of_what_it_answers()
        {
            var answering = new TaskCompletionSource<string?>();
            _store.LoadAsync().Returns(answering.Task);
            var state = CreateState();
            var sample = state.Get<SampleSettings>();
            var loading = state.LoadAsync();

            var changing = state.SetAsync(sample, nameof(SampleSettings.Wrap), true);
            changing.IsCompleted.ShouldBeFalse();
            answering.SetResult("{ \"sample\": { \"size\": 20 } }");
            await loading;
            await changing;

            sample.Size.ShouldBe(20);
            sample.Wrap.ShouldBeTrue();
            _kept.ShouldNotBeNull().ShouldContain("\"size\": 20");
            _kept.ShouldContain("\"wrap\": true");
        }

        [Fact]
        public async Task A_store_that_cannot_be_read_leaves_the_defaults_is_logged_and_settings_still_change()
        {
            _store.LoadAsync().ThrowsAsync(StorageOff());
            var state = CreateState();

            await state.LoadAsync();
            await state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Size), 20);

            state.Get<SampleSettings>().Size.ShouldBe(20);
            _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task A_fault_in_the_store_reaches_whoever_asked_first_and_after_it_the_defaults_stand()
        {
            _store.LoadAsync().ThrowsAsync(new InvalidOperationException("a fault"));
            var state = CreateState();

            await Should.ThrowAsync<InvalidOperationException>(() => state.LoadAsync());
            await state.LoadAsync();
            await state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Size), 20);

            state.Get<SampleSettings>().Size.ShouldBe(20);
        }

        [Fact]
        public async Task A_save_that_fails_is_said_the_change_still_holds_and_the_next_that_succeeds_clears_it()
        {
            var state = CreateState();
            var sample = state.Get<SampleSettings>();
            _store.SaveAsync(Arg.Any<string>()).Returns(Task.FromException(StorageOff()), Task.CompletedTask);

            await state.SetAsync(sample, nameof(SampleSettings.Size), 20);
            state.SaveFailed.ShouldBeTrue();
            sample.Size.ShouldBe(20);

            await state.SetAsync(sample, nameof(SampleSettings.Size), 21);
            state.SaveFailed.ShouldBeFalse();

            _happened.ShouldBe(["changed sample", "save failed is True", "changed sample", "save failed is False"]);
            _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task An_earlier_save_that_fails_after_a_later_one_succeeded_raises_no_warning()
        {
            var first = new TaskCompletionSource();
            var second = new TaskCompletionSource();
            _store.SaveAsync(Arg.Any<string>()).Returns(first.Task, second.Task);
            var state = CreateState();
            var sample = state.Get<SampleSettings>();

            var one = state.SetAsync(sample, nameof(SampleSettings.Size), 20);
            var two = state.SetAsync(sample, nameof(SampleSettings.Size), 21);
            second.SetResult();
            await two;
            first.SetException(StorageOff());
            await one;

            state.SaveFailed.ShouldBeFalse();
        }

        [Fact]
        public async Task Restoring_defaults_puts_back_what_the_panel_shows_with_one_announcement_and_one_save()
        {
            var state = CreateState();
            var sample = state.Get<SampleSettings>();
            await state.SetAsync(sample, nameof(SampleSettings.Size), 20);
            await state.SetAsync(sample, nameof(SampleSettings.Wrap), true);
            await state.SetAsync(sample, nameof(SampleSettings.Remembered), 7);
            _happened.Clear();

            await state.RestoreDefaultsAsync(sample);
            await state.RestoreDefaultsAsync(sample);

            sample.Size.ShouldBe(14);
            sample.Wrap.ShouldBeFalse();
            sample.Remembered.ShouldBe(7);
            state.IsAtDefaults(sample).ShouldBeTrue();
            _happened.ShouldBe(["changed sample", "saved"]);
        }

        [Fact]
        public async Task The_website_has_neither_the_desktops_section_nor_its_settings()
        {
            _kept = "{ \"sample\": { \"channel\": \"beta\" }, \"desktopOnly\": { \"checkAtStart\": false } }";
            var state = CreateState(SettingHosts.Web);

            await state.LoadAsync();

            state.Sections.Select(section => section.SettingsId).ShouldBe(["sample"]);
            state.ShownIn(state.Get<SampleSettings>()).Select(setting => setting.Name).ShouldBe(["Size", "Ratio", "Wrap", "Whitespace", "Name"]);
            state.Get<SampleSettings>().Channel.ShouldBe("stable");
            state.Get<DesktopOnlySettings>().CheckAtStart.ShouldBeTrue();
        }

        [Fact]
        public async Task The_desktop_has_both_in_their_order()
        {
            _kept = "{ \"sample\": { \"channel\": \"beta\" }, \"desktopOnly\": { \"checkAtStart\": false } }";
            var state = CreateState(SettingHosts.Desktop);

            await state.LoadAsync();

            state.Sections.Select(section => section.SettingsId).ShouldBe(["sample", "desktopOnly"]);
            state.ShownIn(state.Get<SampleSettings>()).Select(setting => setting.Name).ShouldContain("Channel");
            state.Get<SampleSettings>().Channel.ShouldBe("beta");
            state.Get<DesktopOnlySettings>().CheckAtStart.ShouldBeFalse();
        }

        [Fact]
        public async Task With_no_store_a_change_holds_and_nothing_is_said_to_have_failed()
        {
            var state = CreateState(withStore: false);

            await state.LoadAsync();
            await state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Size), 20);

            state.Get<SampleSettings>().Size.ShouldBe(20);
            state.SaveFailed.ShouldBeFalse();
        }

        [Fact]
        public void Asking_for_a_section_that_was_never_added_is_a_mistake_in_the_caller()
        {
            var state = new SettingsState(Options.Create(new SettingsOptions()), _logger);

            Should.Throw<InvalidOperationException>(() => state.Get<SampleSettings>());
        }
    }
}
```

In `Blazemoji.Test/HostingTests.cs`, add to `The_registrations_a_host_is_told_to_make_give_the_editor_every_service_it_uses`:

```csharp
            Services.GetRequiredService<SettingsState>().ShouldNotBeNull();
```

and to `State_is_per_session_and_the_catalog_is_shared`:

```csharp
            one.ServiceProvider.GetRequiredService<SettingsState>().ShouldNotBeSameAs(two.ServiceProvider.GetRequiredService<SettingsState>());
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.SettingsStateTests`
Expected: the build fails: `The type or namespace name 'SettingsState' could not be found`.

- [ ] **Step 3: Write the options**

`Blazemoji.Core/Services/Settings/SettingsOptions.cs`:

```csharp
using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Services.Settings
{
    /// <summary>
    /// Which host this is and which sections of settings there are. The editor adds its own
    /// sections; a host adds any it has and says which host it is.
    /// </summary>
    public sealed class SettingsOptions
    {
        private readonly List<Type> _sections = [];

        /// <summary>A host that says nothing is taken for the website, which has no desktop-only settings.</summary>
        public SettingHosts Host { get; set; } = SettingHosts.Web;

        public IReadOnlyList<Type> Sections => _sections;

        public SettingsOptions Add<T>() where T : SettingsBase, new()
        {
            if (!_sections.Contains(typeof(T)))
                _sections.Add(typeof(T));

            return this;
        }
    }
}
```

- [ ] **Step 4: Write the state**

`Blazemoji.Components/Shared/State/SettingsState.cs`:

```csharp
using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
using Blazemoji.Shared.Models.Settings;
using Microsoft.Extensions.Options;

namespace Blazemoji.Shared.State
{
    /// <summary>
    /// The settings of one session: one visitor on the website, the one window on the desktop.
    /// It makes the sections, is the only thing that changes a setting, and keeps them through
    /// an <see cref="ISettingsStore"/> when the host has one. A store that fails never stops a
    /// setting from changing.
    /// </summary>
    public sealed class SettingsState
    {
        private readonly SettingHosts _host;
        private readonly ILogger<SettingsState> _logger;
        private readonly ISettingsStore? _store;
        private readonly List<SettingsBase> _all;
        private Task? _loading;
        private int _saves;

        public SettingsState(IOptions<SettingsOptions> options, ILogger<SettingsState> logger, ISettingsStore? store = null)
        {
            _host = options.Value.Host;
            _logger = logger;
            _store = store;
            _all = options.Value.Sections.Select(type => (SettingsBase)Activator.CreateInstance(type)!).ToList();
            Sections = _all
                .Where(section => (section.Hosts & _host) != 0)
                .OrderBy(section => section.Order)
                .ThenBy(section => section.DisplayName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Raised with the section whose settings changed.</summary>
        public event Action<SettingsBase>? SettingsChanged;

        public event Action? SaveFailedChanged;

        /// <summary>This host's sections, in the order the panel shows them.</summary>
        public IReadOnlyList<SettingsBase> Sections { get; }

        /// <summary>The newest save did not succeed, so what is set lasts only as long as the session.</summary>
        public bool SaveFailed { get; private set; }

        /// <summary>A section this host does not show is given at its defaults.</summary>
        public T Get<T>() where T : SettingsBase =>
            _all.OfType<T>().FirstOrDefault()
            ?? throw new InvalidOperationException($"{typeof(T).Name} was not added to {nameof(SettingsOptions)}.");

        /// <summary>The settings of a section that the panel shows on this host.</summary>
        public IReadOnlyList<SettingProperty> ShownIn(SettingsBase section) =>
            SettingProperties.Of(section).Where(setting => setting.IsFor(_host) && !setting.Attribute.Hide).ToList();

        public bool IsAtDefaults(SettingsBase section) =>
            ShownIn(section).All(setting => Equals(setting.Read(section), setting.Default));

        /// <summary>
        /// Takes up what was kept, once. Only to be called where the browser can be reached:
        /// after the first render, or from something the user did.
        /// </summary>
        public Task LoadAsync()
        {
            if (_store is null)
                return Task.CompletedTask;

            if (_loading is not null)
                return _loading;

            // A store that fails at once has failed before there is a task to remember. The
            // failure is for whoever asked first; after it the defaults stand.
            var loading = LoadFromAsync(_store);
            _loading = loading.IsFaulted ? Task.CompletedTask : loading;
            return loading;
        }

        private async Task LoadFromAsync(ISettingsStore from)
        {
            string? text = null;
            try
            {
                text = await from.LoadAsync();
            }
            catch (ProjectStoreException exception)
            {
                _logger.LogWarning(exception, "The kept settings could not be read, so the defaults are used");
            }
            catch
            {
                _loading = Task.CompletedTask;
                throw;
            }

            var changed = new List<SettingsBase>();
            foreach (var kept in SettingsJson.Read(text, _all, _host))
            {
                if (Equals(kept.Setting.Read(kept.Section), kept.Value))
                    continue;

                kept.Setting.Assign(kept.Section, kept.Value);
                if (!changed.Contains(kept.Section))
                    changed.Add(kept.Section);
            }

            foreach (var section in changed)
                SettingsChanged?.Invoke(section);
        }

        public async Task SetAsync(SettingsBase section, string property, object? value)
        {
            var setting = SettingProperties.Of(Own(section)).FirstOrDefault(candidate => candidate.Name == property && candidate.IsFor(_host))
                ?? throw new ArgumentException($"{section.GetType().Name} has no setting called {property} on this host.", nameof(property));

            // A change made before the kept settings have arrived would be written over by them.
            await LoadAsync();

            if (!setting.TryAccept(value, out var accepted) || Equals(setting.Read(section), accepted))
                return;

            setting.Assign(section, accepted);
            SettingsChanged?.Invoke(section);
            await KeepAsync();
        }

        public async Task RestoreDefaultsAsync(SettingsBase section)
        {
            var shown = ShownIn(Own(section));
            await LoadAsync();

            var away = shown.Where(setting => !Equals(setting.Read(section), setting.Default)).ToList();
            if (away.Count == 0)
                return;

            foreach (var setting in away)
                setting.Assign(section, setting.Default);

            SettingsChanged?.Invoke(section);
            await KeepAsync();
        }

        private SettingsBase Own(SettingsBase section) =>
            Sections.Contains(section)
                ? section
                : throw new ArgumentException($"{section.GetType().Name} is not one of this host's sections, or belongs to another session.", nameof(section));

        private async Task KeepAsync()
        {
            if (_store is null)
                return;

            var mine = ++_saves;
            bool failed;
            try
            {
                await _store.SaveAsync(SettingsJson.Write(_all, _host));
                failed = false;
            }
            catch (ProjectStoreException exception)
            {
                _logger.LogWarning(exception, "The settings could not be kept, so they last only as long as this session");
                failed = true;
            }

            // Each save holds every setting, so only the newest says whether what is kept is what is set.
            if (mine != _saves || failed == SaveFailed)
                return;

            SaveFailed = failed;
            SaveFailedChanged?.Invoke();
        }
    }
}
```

- [ ] **Step 5: Register it**

In `Blazemoji.Components/BlazemojiEditorRegistration.cs`, in `AddBlazemojiEditor`, after `services.TryAddScoped<LayoutState>();`:

```csharp
            services.TryAddScoped<SettingsState>();
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.SettingsStateTests`
Then: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.HostingTests`
Expected: `failed: 0` for both.

- [ ] **Step 7: Commit**

```bash
git add Blazemoji.Core/Services/Settings/SettingsOptions.cs Blazemoji.Components/Shared/State/SettingsState.cs Blazemoji.Components/BlazemojiEditorRegistration.cs Blazemoji.Test/Settings/SettingsStateTests.cs Blazemoji.Test/HostingTests.cs
git commit -m "Each session has settings of its own, changed in one place and kept when there is somewhere to keep them"
```

---

### Task 5: The first section

**Files:**
- Create: `Blazemoji.Components/Settings/EditorSettings.cs`
- Create: `Blazemoji.Components/Settings/EditorOptionValues.cs`
- Modify: `Blazemoji.Components/BlazemojiIcons.cs` (after `KeyCommands`)
- Modify: `Blazemoji.Components/BlazemojiEditorRegistration.cs` (in `AddBlazemojiEditor`)
- Test: `Blazemoji.Test/Settings/EditorSettingsTests.cs`

**Interfaces:**
- Consumes: `SettingsBase`, `SettingAttribute`, `SettingsOptions.Add<T>()`, `SettingsState`; BlazorMonaco's `EditorOptions` (the base of both `StandaloneEditorConstructionOptions` and `EditorUpdateOptions`), `EditorUpdateOptions`, `EditorMinimapOptions`, all in `BlazorMonaco.Editor`.
- Produces, in `Blazemoji.Settings`: `EditorSettings` with `FontSize` (int, 14, 8 to 32), `WordWrap` (bool, false), `Minimap` (bool, true), `LineNumbers` (bool, true), `RenderWhitespace` (string, "selection"); `EditorOptionValues(int FontSize, string WordWrap, bool Minimap, string LineNumbers, string RenderWhitespace)` with `static From(EditorSettings)`, `void ApplyTo(EditorOptions)`, `EditorUpdateOptions ToUpdateOptions()`.
- Produces: `BlazemojiIcons.Settings`, `BlazemojiIcons.EditorSettings`.

- [ ] **Step 1: Write the failing tests**

`Blazemoji.Test/Settings/EditorSettingsTests.cs`:

```csharp
using Blazemoji.Services.Settings;
using Blazemoji.Settings;
using Blazemoji.Shared.State;
using BlazorMonaco.Editor;
using Microsoft.Extensions.DependencyInjection;

namespace Blazemoji.Test.Settings
{
    public class EditorSettingsTests
    {
        [Fact]
        public void The_defaults_are_what_the_editor_does_today_with_text_at_fourteen()
        {
            var settings = new EditorSettings();

            settings.FontSize.ShouldBe(14);
            settings.WordWrap.ShouldBeFalse();
            settings.Minimap.ShouldBeTrue();
            settings.LineNumbers.ShouldBeTrue();
            settings.RenderWhitespace.ShouldBe("selection");
        }

        [Fact]
        public void Every_setting_has_a_label_and_a_group_and_they_come_in_the_panels_order()
        {
            var settings = SettingProperties.Of(typeof(EditorSettings));

            settings.Select(setting => setting.Name).ShouldBe(["FontSize", "WordWrap", "Minimap", "LineNumbers", "RenderWhitespace"]);
            settings.ShouldAllBe(setting => setting.Attribute.Label.Length > 0 && !string.IsNullOrEmpty(setting.Attribute.Group));
            settings.Single(setting => setting.Name == "RenderWhitespace").Options.ShouldBe(["none", "boundary", "selection", "trailing", "all"]);
        }

        [Fact]
        public void The_values_are_put_as_monaco_wants_them()
        {
            var values = EditorOptionValues.From(new EditorSettings());

            values.ShouldBe(new EditorOptionValues(14, "off", true, "on", "selection"));
        }

        [Fact]
        public void They_are_handed_to_monaco_under_its_own_names()
        {
            var options = new EditorOptionValues(18, "on", false, "off", "all").ToUpdateOptions();

            options.FontSize.ShouldBe(18);
            options.WordWrap.ShouldBe("on");
            options.Minimap.Enabled.ShouldBe(false);
            options.LineNumbers.ShouldBe("off");
            options.RenderWhitespace.ShouldBe("all");
        }

        [Fact]
        public void The_editors_own_call_adds_the_section()
        {
            var services = new ServiceCollection();
            services.AddBlazemojiEditor();
            using var provider = services.BuildServiceProvider(validateScopes: true);
            using var scope = provider.CreateScope();

            var state = scope.ServiceProvider.GetRequiredService<SettingsState>();

            state.Sections.ShouldHaveSingleItem().ShouldBeOfType<EditorSettings>();
            state.Get<EditorSettings>().FontSize.ShouldBe(14);
        }
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.EditorSettingsTests`
Expected: the build fails: `The type or namespace name 'Settings' does not exist in the namespace 'Blazemoji'`.

- [ ] **Step 3: Add the icons**

In `Blazemoji.Components/BlazemojiIcons.cs`, after the `KeyCommands` line:

```csharp
        public static string Settings { get; } = Emoji("⚙️");

        /// <summary>The Editor section of the settings.</summary>
        public static string EditorSettings { get; } = Emoji("📝");
```

- [ ] **Step 4: Write the section and its values**

`Blazemoji.Components/Settings/EditorSettings.cs`:

```csharp
using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Settings
{
    /// <summary>
    /// How the editor shows text. Each default is what Monaco does when it is told nothing,
    /// except the font size: Monaco's own is 12 on macOS and 14 elsewhere, and a setting has
    /// one default.
    /// </summary>
    public sealed class EditorSettings : SettingsBase
    {
        public override string SettingsId => "editor";

        public override string DisplayName => "Editor";

        public override string Icon => BlazemojiIcons.EditorSettings;

        public override int Order => 10;

        [Setting(Label = "Font size", Description = "How large the editor's text is, in pixels.", Group = "Text", Order = 1, Min = 8, Max = 32)]
        public int FontSize { get; private set; } = 14;

        [Setting(Label = "Word wrap", Description = "Carry a long line over to the next instead of scrolling sideways.", Group = "Text", Order = 2)]
        public bool WordWrap { get; private set; }

        [Setting(Label = "Minimap", Description = "A small picture of the whole file beside the text.", Group = "Display", Order = 3)]
        public bool Minimap { get; private set; } = true;

        [Setting(Label = "Line numbers", Group = "Display", Order = 4)]
        public bool LineNumbers { get; private set; } = true;

        [Setting(Label = "Show whitespace", Description = "Where spaces and tabs are drawn as marks.", Group = "Display", Order = 5, Options = "none,boundary,selection,trailing,all")]
        public string RenderWhitespace { get; private set; } = "selection";
    }
}
```

`Blazemoji.Components/Settings/EditorOptionValues.cs`:

```csharp
using BlazorMonaco.Editor;

namespace Blazemoji.Settings
{
    /// <summary>
    /// The editor's settings as Monaco wants them. Two of these are equal when they would make
    /// the editor look the same, which is how the editor knows whether there is anything to do.
    /// </summary>
    public sealed record EditorOptionValues(int FontSize, string WordWrap, bool Minimap, string LineNumbers, string RenderWhitespace)
    {
        private const string On = "on";
        private const string Off = "off";

        public static EditorOptionValues From(EditorSettings settings) => new(
            settings.FontSize,
            settings.WordWrap ? On : Off,
            settings.Minimap,
            settings.LineNumbers ? On : Off,
            settings.RenderWhitespace);

        /// <summary>Puts them onto the options an editor is made with, or changed with.</summary>
        public void ApplyTo(EditorOptions options)
        {
            options.FontSize = FontSize;
            options.WordWrap = WordWrap;
            options.Minimap = new EditorMinimapOptions { Enabled = Minimap };
            options.LineNumbers = LineNumbers;
            options.RenderWhitespace = RenderWhitespace;
        }

        public EditorUpdateOptions ToUpdateOptions()
        {
            var options = new EditorUpdateOptions();
            ApplyTo(options);
            return options;
        }
    }
}
```

- [ ] **Step 5: Add the section to the editor's own call**

In `Blazemoji.Components/BlazemojiEditorRegistration.cs` add `using Blazemoji.Settings;` and, in `AddBlazemojiEditor`, straight after `services.AddLogging();`:

```csharp
            services.Configure<SettingsOptions>(settings => settings.Add<EditorSettings>());
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.EditorSettingsTests`
Expected: `failed: 0`.

- [ ] **Step 7: Commit**

```bash
git add Blazemoji.Components/Settings Blazemoji.Components/BlazemojiIcons.cs Blazemoji.Components/BlazemojiEditorRegistration.cs Blazemoji.Test/Settings/EditorSettingsTests.cs
git commit -m "Five of the editor's options are settings"
```

---

### Task 6: The website's store, and each visitor their own

**Files:**
- Create: `Blazemoji/Services/Settings/LocalStorageSettingsStore.cs`
- Modify: `Blazemoji/Program.cs` (after the line that registers `LocalStorageLayoutStore`)
- Test: `Blazemoji.Test/Settings/LocalStorageSettingsStoreTests.cs`, `Blazemoji.Test/Settings/SettingsPerVisitorTests.cs`

**Interfaces:**
- Consumes: `ISettingsStore`, `ProjectStoreException`, `LocalStorageProjectStore.KeyPrefix` (`"blazemoji."`), Blazored's `ILocalStorageService`, `SettingsState`, `EditorSettings`.
- Produces, in `Blazemoji.Services.Settings` (the web host's assembly): `LocalStorageSettingsStore(ILocalStorageService localStorage)`, keeping everything under the key `blazemoji.settings`.

- [ ] **Step 1: Write the failing tests**

`Blazemoji.Test/Settings/LocalStorageSettingsStoreTests.cs`:

```csharp
using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
using Blazored.LocalStorage;
using Microsoft.JSInterop;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.Settings
{
    public class LocalStorageSettingsStoreTests
    {
        private const string Key = "blazemoji.settings";

        private readonly Dictionary<string, string> _browser = [];
        private readonly ILocalStorageService _localStorage = Substitute.For<ILocalStorageService>();

        public LocalStorageSettingsStoreTests()
        {
            _localStorage.GetItemAsStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => new ValueTask<string?>(_browser.GetValueOrDefault(call.Arg<string>())));
            _localStorage.SetItemAsStringAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    _browser[call.ArgAt<string>(0)] = call.ArgAt<string>(1);
                    return ValueTask.CompletedTask;
                });
        }

        private LocalStorageSettingsStore CreateStore() => new(_localStorage);

        [Fact]
        public async Task A_browser_with_nothing_kept_has_no_settings()
        {
            (await CreateStore().LoadAsync()).ShouldBeNull();
        }

        [Fact]
        public async Task What_was_kept_is_read_back_on_the_next_visit()
        {
            await CreateStore().SaveAsync("{ \"editor\": { \"fontSize\": 16 } }");

            (await CreateStore().LoadAsync()).ShouldBe("{ \"editor\": { \"fontSize\": 16 } }");
        }

        [Fact]
        public async Task It_is_kept_under_one_key_that_starts_as_the_project_stores_keys_do()
        {
            await CreateStore().SaveAsync("{}");

            // The library clears what it saved by the look of a key, and leaves these alone.
            _browser.Keys.ShouldBe([Key]);
            Key.ShouldStartWith(LocalStorageProjectStore.KeyPrefix);
        }

        [Fact]
        public async Task Storage_that_is_switched_off_or_full_is_reported_as_the_store_failing()
        {
            _localStorage.GetItemAsStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Throws(new JSException("SecurityError"));
            _localStorage.SetItemAsStringAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Throws(new JSException("QuotaExceededError"));

            await Should.ThrowAsync<ProjectStoreException>(() => CreateStore().LoadAsync());
            await Should.ThrowAsync<ProjectStoreException>(() => CreateStore().SaveAsync("{}"));
        }

        [Fact]
        public async Task A_page_that_has_gone_away_is_reported_the_same_way()
        {
            _localStorage.SetItemAsStringAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Throws(new JSDisconnectedException("gone"));

            await Should.ThrowAsync<ProjectStoreException>(() => CreateStore().SaveAsync("{}"));
        }
    }
}
```

`Blazemoji.Test/Settings/SettingsPerVisitorTests.cs`:

```csharp
using Blazemoji.Services.Settings;
using Blazemoji.Settings;
using Blazemoji.Shared.State;
using Blazored.LocalStorage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Blazemoji.Test.Settings
{
    /// <summary>
    /// The website has many visitors in one process. This is why the settings code was
    /// brought over and not taken from a library that keeps one set for the whole process.
    /// </summary>
    public class SettingsPerVisitorTests
    {
        [Fact]
        public async Task Two_visitors_to_the_website_each_have_their_own_settings()
        {
            var services = new ServiceCollection();
            services.AddBlazemojiEditor();
            services.AddScoped(_ => Substitute.For<ILocalStorageService>());
            services.AddScoped<ISettingsStore, LocalStorageSettingsStore>();
            await using var provider = services.BuildServiceProvider(validateScopes: true);
            await using var one = provider.CreateAsyncScope();
            await using var two = provider.CreateAsyncScope();
            var first = one.ServiceProvider.GetRequiredService<SettingsState>();
            var second = two.ServiceProvider.GetRequiredService<SettingsState>();

            await first.SetAsync(first.Get<EditorSettings>(), nameof(EditorSettings.FontSize), 20);

            first.ShouldNotBeSameAs(second);
            first.Get<EditorSettings>().ShouldNotBeSameAs(second.Get<EditorSettings>());
            first.Get<EditorSettings>().FontSize.ShouldBe(20);
            second.Get<EditorSettings>().FontSize.ShouldBe(14);
        }
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.LocalStorageSettingsStoreTests`
Expected: the build fails: `The type or namespace name 'LocalStorageSettingsStore' could not be found`.

- [ ] **Step 3: Write the store**

`Blazemoji/Services/Settings/LocalStorageSettingsStore.cs`:

```csharp
using Blazemoji.Services.Projects;
using Microsoft.JSInterop;

namespace Blazemoji.Services.Settings
{
    /// <summary>
    /// Keeps the settings in the browser's local storage, under one key beside the project
    /// store's. Local storage can only be reached once the page has rendered, and can be
    /// full or switched off, so every failure is a <see cref="ProjectStoreException"/>.
    /// </summary>
    public sealed class LocalStorageSettingsStore(ILocalStorageService localStorage) : ISettingsStore
    {
        private const string Key = LocalStorageProjectStore.KeyPrefix + "settings";

        public Task<string?> LoadAsync() => Guarded(async () => await localStorage.GetItemAsStringAsync(Key));

        public Task SaveAsync(string settings) => Guarded(async () =>
        {
            await localStorage.SetItemAsStringAsync(Key, settings);
            return true;
        });

        private static async Task<T> Guarded<T>(Func<Task<T>> useStorage)
        {
            try
            {
                return await useStorage();
            }
            catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException or OperationCanceledException)
            {
                throw new ProjectStoreException("The browser's storage could not be used.", exception);
            }
        }
    }
}
```

- [ ] **Step 4: Register it**

In `Blazemoji/Program.cs` add `using Blazemoji.Services.Settings;` and, after `builder.Services.AddScoped<ILayoutStore, LocalStorageLayoutStore>();`:

```csharp
builder.Services.AddScoped<ISettingsStore, LocalStorageSettingsStore>();
```

Change the comment above that block from "projects and snippets in local storage" to "projects, snippets, the layout and settings in local storage".

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.LocalStorageSettingsStoreTests`
Then: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.SettingsPerVisitorTests`
Expected: `failed: 0` for both.

- [ ] **Step 6: Commit**

```bash
git add Blazemoji/Services/Settings Blazemoji/Program.cs Blazemoji.Test/Settings/LocalStorageSettingsStoreTests.cs Blazemoji.Test/Settings/SettingsPerVisitorTests.cs
git commit -m "The website keeps each visitor's settings in their own browser"
```

---

### Task 7: The panel

**Files:**
- Create: `Blazemoji.Components/Components/Settings/BoolSettingEditor.razor`
- Create: `Blazemoji.Components/Components/Settings/NumberSettingEditor.razor`
- Create: `Blazemoji.Components/Components/Settings/ChoiceSettingEditor.razor`
- Create: `Blazemoji.Components/Components/Settings/TextSettingEditor.razor`
- Create: `Blazemoji.Components/Components/Settings/SettingRow.razor`, `SettingRow.razor.css`
- Create: `Blazemoji.Components/Components/Settings/SettingsSectionView.razor`
- Create: `Blazemoji.Components/Components/Settings/SettingsDialog.razor`
- Create: `Blazemoji.Components/Components/Settings/SettingsButton.razor`
- Modify: `Blazemoji.Components/_Imports.razor` (three usings at the end)
- Modify: `Blazemoji.Components/Layout/AppShell.razor` (before the dark mode `MudTooltip`)
- Modify: `Blazemoji.E2E/EditorPage.cs` (`ThemeToggle`)
- Test: `Blazemoji.Test/Settings/SettingsPanelTests.cs`
- Modify: `Blazemoji.Test/Components/AppShellTests.cs`

**Interfaces:**
- Consumes: `SettingsState` (`Sections`, `ShownIn`, `IsAtDefaults`, `SetAsync`, `RestoreDefaultsAsync`, `LoadAsync`, `SaveFailed`, `SettingsChanged`, `SaveFailedChanged`), `SettingProperty`, `SettingProperties.Grouped`, `SegmentedTabs` and `SegmentedTab` (`Blazemoji.Components.Shared`: `Active`, `ActiveChanged`, `AriaLabel`; `Id`, `Text`, `Icon`, `ChildContent`), `BlazemojiIcons.Settings`.
- Produces, in `Blazemoji.Components.Settings`: the eight components. Markup the tests and the browser rely on: a row is `[data-testid=setting][data-setting=<property name>]`; a group heading is `[data-testid=settings-group-name]`; the button at a section's end is `[data-testid=restore-defaults]`; the warning is `[data-testid=settings-not-saved]`; the title bar's button is `[data-testid=settings-button]`.

`EditorPage.ThemeToggle` finds the dark mode button as the first icon button in the title bar. The Settings button goes before it, so that locator is changed to the button's own test id in this task.

- [ ] **Step 1: Write the failing tests**

`Blazemoji.Test/Settings/SettingsPanelTests.cs`:

```csharp
using Blazemoji.Components.Settings;
using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
using Blazemoji.Shared.Models.Settings;
using Blazemoji.Shared.State;
using Blazemoji.Test.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace Blazemoji.Test.Settings
{
    public sealed class SettingsPanelTests : BunitContext
    {
        private readonly ISettingsStore _store = Substitute.For<ISettingsStore>();

        public SettingsPanelTests()
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            _store.LoadAsync().Returns(Task.FromResult<string?>(null));
        }

        private SettingsState UseState(SettingHosts host = SettingHosts.Web, bool alsoDesktopOnly = false)
        {
            var options = new SettingsOptions { Host = host };
            options.Add<SampleSettings>();
            if (alsoDesktopOnly)
                options.Add<DesktopOnlySettings>();

            var state = new SettingsState(Options.Create(options), new RecordingLogger<SettingsState>(), _store);
            Services.AddSingleton(state);
            return state;
        }

        private IRenderedComponent<SettingsSectionView> RenderSection(SettingsState state) =>
            Render<SettingsSectionView>(parameters => parameters.Add(view => view.Section, state.Get<SampleSettings>()));

        private static IRenderedComponent<SettingRow> Row(IRenderedComponent<SettingsSectionView> cut, string setting) =>
            cut.FindComponents<SettingRow>().Single(row => row.Instance.Setting.Name == setting);

        private async Task<IRenderedComponent<MudDialogProvider>> OpenDialogAsync()
        {
            var provider = Render<MudDialogProvider>();
            var dialogs = Services.GetRequiredService<IDialogService>();
            await provider.InvokeAsync(() => dialogs.ShowAsync<SettingsDialog>("Settings"));
            return provider;
        }

        [Fact]
        public void A_section_shows_its_settings_in_order_under_their_groups_without_the_hidden_or_the_other_hosts()
        {
            var cut = RenderSection(UseState());

            cut.FindAll("[data-testid=setting]").Select(row => row.GetAttribute("data-setting"))
                .ShouldBe(["Size", "Ratio", "Wrap", "Whitespace", "Name"]);
            cut.FindAll("[data-testid=settings-group-name]").Select(name => name.TextContent.Trim())
                .ShouldBe(["Numbers", "Switches", "Choices", "Text"]);
            cut.Markup.ShouldContain("Carry long lines over.");
        }

        [Fact]
        public void On_the_desktop_the_desktops_setting_is_shown_too()
        {
            var cut = RenderSection(UseState(SettingHosts.Desktop));

            cut.FindAll("[data-testid=setting]").Select(row => row.GetAttribute("data-setting")).ShouldContain("Channel");
        }

        [Fact]
        public async Task Each_kind_of_control_hands_its_new_value_to_the_state()
        {
            var state = UseState();
            var sample = state.Get<SampleSettings>();
            var cut = RenderSection(state);

            await cut.InvokeAsync(() => Row(cut, "Wrap").FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(true));
            await cut.InvokeAsync(() => Row(cut, "Size").FindComponent<MudNumericField<int>>().Instance.ValueChanged.InvokeAsync(20));
            await cut.InvokeAsync(() => Row(cut, "Ratio").FindComponent<MudNumericField<double>>().Instance.ValueChanged.InvokeAsync(2.5));
            await cut.InvokeAsync(() => Row(cut, "Whitespace").FindComponent<MudSelect<string>>().Instance.ValueChanged.InvokeAsync("all"));
            await cut.InvokeAsync(() => Row(cut, "Name").FindComponent<MudTextField<string>>().Instance.ValueChanged.InvokeAsync("other"));

            sample.Wrap.ShouldBeTrue();
            sample.Size.ShouldBe(20);
            sample.Ratio.ShouldBe(2.5);
            sample.Whitespace.ShouldBe("all");
            sample.Name.ShouldBe("other");
        }

        [Fact]
        public async Task A_number_typed_out_of_range_ends_at_the_limit()
        {
            var state = UseState();
            var cut = RenderSection(state);

            await Row(cut, "Size").Find("input").ChangeAsync(new ChangeEventArgs { Value = "99" });

            state.Get<SampleSettings>().Size.ShouldBe(32);
        }

        [Fact]
        public async Task A_number_field_that_is_emptied_leaves_the_setting_within_its_limits()
        {
            var state = UseState();
            var cut = RenderSection(state);

            await Row(cut, "Size").Find("input").ChangeAsync(new ChangeEventArgs { Value = "" });

            state.Get<SampleSettings>().Size.ShouldBeInRange(8, 32);
        }

        [Fact]
        public async Task Restore_defaults_is_off_at_the_defaults_on_after_a_change_and_puts_them_back()
        {
            var state = UseState();
            var sample = state.Get<SampleSettings>();
            var cut = RenderSection(state);
            cut.Find("[data-testid=restore-defaults]").HasAttribute("disabled").ShouldBeTrue();

            await cut.InvokeAsync(() => state.SetAsync(sample, nameof(SampleSettings.Size), 20));
            cut.Render();
            cut.Find("[data-testid=restore-defaults]").HasAttribute("disabled").ShouldBeFalse();

            await cut.Find("[data-testid=restore-defaults]").ClickAsync();

            sample.Size.ShouldBe(14);
            cut.Find("[data-testid=restore-defaults]").HasAttribute("disabled").ShouldBeTrue();
        }

        [Fact]
        public async Task With_one_section_the_dialog_has_no_tabs()
        {
            UseState();

            var dialog = await OpenDialogAsync();

            dialog.FindAll("[data-testid=setting]").ShouldNotBeEmpty();
            dialog.FindAll(".segmented-tabs").ShouldBeEmpty();
        }

        [Fact]
        public async Task With_more_than_one_section_they_are_chosen_with_tabs()
        {
            UseState(SettingHosts.Desktop, alsoDesktopOnly: true);

            var dialog = await OpenDialogAsync();

            dialog.FindAll(".segmented-tabs").ShouldHaveSingleItem();
            dialog.Markup.ShouldContain("Sample");
            dialog.Markup.ShouldContain("Desktop only");
        }

        [Fact]
        public async Task A_change_made_elsewhere_is_shown_in_the_open_dialog()
        {
            var state = UseState();
            var dialog = await OpenDialogAsync();

            await dialog.InvokeAsync(() => state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Size), 20));

            dialog.WaitForAssertion(() => dialog.Find("[data-testid=restore-defaults]").HasAttribute("disabled").ShouldBeFalse());
        }

        [Fact]
        public async Task The_dialog_says_so_in_fixed_words_when_settings_could_not_be_saved()
        {
            var state = UseState();
            _store.SaveAsync(Arg.Any<string>()).Returns(Task.FromException(new ProjectStoreException("The browser's storage could not be used.", new IOException("QuotaExceededError at C:\\secret"))));
            var dialog = await OpenDialogAsync();
            dialog.FindAll("[data-testid=settings-not-saved]").ShouldBeEmpty();

            await dialog.InvokeAsync(() => state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Size), 20));

            dialog.WaitForAssertion(() => dialog.Find("[data-testid=settings-not-saved]").TextContent
                .ShouldContain("Your settings could not be saved, so changes last only until Blazemoji is closed."));
            dialog.Markup.ShouldNotContain("QuotaExceededError");
        }
    }
}
```

In `Blazemoji.Test/Components/AppShellTests.cs`, add these usings and registrations, and one test:

```csharp
using Blazemoji.Services.Settings;
using Blazemoji.Settings;
using Blazemoji.Shared.State;
```

In the constructor, after `AddMudServices`:

```csharp
            Services.Configure<SettingsOptions>(settings => settings.Add<EditorSettings>());
            Services.AddScoped<SettingsState>();
```

```csharp
        [Fact]
        public async Task The_settings_button_opens_the_panel()
        {
            var cut = RenderAroundSomething();

            await cut.Find("[data-testid=settings-button]").ClickAsync();

            cut.WaitForAssertion(() => cut.FindAll("[data-testid=setting]").ShouldNotBeEmpty());
        }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.SettingsPanelTests`
Expected: the build fails: `The type or namespace name 'Settings' does not exist in the namespace 'Blazemoji.Components'`.

- [ ] **Step 3: Add the usings**

At the end of `Blazemoji.Components/_Imports.razor`:

```razor
@using Blazemoji.Components.Settings
@using Blazemoji.Services.Settings
@using Blazemoji.Shared.Models.Settings
```

- [ ] **Step 4: Write the four editors**

Each is handed a value and hands back a new one. None of them touches a section.

`Blazemoji.Components/Components/Settings/BoolSettingEditor.razor`:

```razor
<MudSwitch T="bool" Value="@Value" ValueChanged="@ValueChanged" Color="Color.Primary" aria-label="@Label" />

@code {
    [Parameter, EditorRequired]
    public bool Value { get; set; }

    [Parameter, EditorRequired]
    public string Label { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<bool> ValueChanged { get; set; }
}
```

`Blazemoji.Components/Components/Settings/NumberSettingEditor.razor`:

```razor
@* The field is given the limits so that it shows the number it was brought to. The state
   brings it within them whatever the field does. *@
@if (Setting.Type == typeof(int))
{
    <MudNumericField T="int"
                     Value="@((int)Value)"
                     ValueChanged="@((int value) => ValueChanged.InvokeAsync(value))"
                     Min="@(Setting.Attribute.HasRange ? (int)Setting.Attribute.Min : int.MinValue)"
                     Max="@(Setting.Attribute.HasRange ? (int)Setting.Attribute.Max : int.MaxValue)"
                     Step="@((int)Math.Max(1, Setting.Attribute.Step))"
                     Variant="Variant.Outlined"
                     Margin="Margin.Dense"
                     aria-label="@Setting.Attribute.Label" />
}
else
{
    <MudNumericField T="double"
                     Value="@((double)Value)"
                     ValueChanged="@((double value) => ValueChanged.InvokeAsync(value))"
                     Min="@(Setting.Attribute.HasRange ? Setting.Attribute.Min : double.MinValue)"
                     Max="@(Setting.Attribute.HasRange ? Setting.Attribute.Max : double.MaxValue)"
                     Step="@Setting.Attribute.Step"
                     Variant="Variant.Outlined"
                     Margin="Margin.Dense"
                     aria-label="@Setting.Attribute.Label" />
}

@code {
    [Parameter, EditorRequired]
    public SettingProperty Setting { get; set; } = default!;

    [Parameter, EditorRequired]
    public object Value { get; set; } = default!;

    [Parameter]
    public EventCallback<object?> ValueChanged { get; set; }
}
```

`Blazemoji.Components/Components/Settings/ChoiceSettingEditor.razor`:

```razor
<MudSelect T="string" Value="@Value" ValueChanged="@ValueChanged" Dense="true" Margin="Margin.Dense" Variant="Variant.Outlined" aria-label="@Label">
    @foreach (var option in Options)
    {
        <MudSelectItem T="string" @key="option" Value="@option">@option</MudSelectItem>
    }
</MudSelect>

@code {
    [Parameter, EditorRequired]
    public string Value { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public IReadOnlyList<string> Options { get; set; } = [];

    [Parameter, EditorRequired]
    public string Label { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }
}
```

`Blazemoji.Components/Components/Settings/TextSettingEditor.razor`:

```razor
@* Not Immediate: the setting changes when the field is left or Enter is pressed, not at each key. *@
<MudTextField T="string" Value="@Value" ValueChanged="@ValueChanged" MaxLength="SettingProperty.LongestText" Variant="Variant.Outlined" Margin="Margin.Dense" aria-label="@Label" />

@code {
    [Parameter, EditorRequired]
    public string Value { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string Label { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }
}
```

- [ ] **Step 5: Write the row**

`Blazemoji.Components/Components/Settings/SettingRow.razor`:

```razor
@inject SettingsState State

<div class="setting-row d-flex align-center gap-4 py-2" data-testid="setting" data-setting="@Setting.Name">
    <div class="setting-row-words d-flex flex-column">
        <MudText Typo="Typo.body1">@Setting.Attribute.Label</MudText>
        @if (!string.IsNullOrEmpty(Setting.Attribute.Description))
        {
            <MudText Typo="Typo.caption" Class="mud-text-secondary">@Setting.Attribute.Description</MudText>
        }
    </div>
    <div class="setting-row-control d-flex justify-end">
        @if (Setting.Type == typeof(bool))
        {
            <BoolSettingEditor Value="@((bool)Value!)" Label="@Setting.Attribute.Label" ValueChanged="@((bool value) => ChangeAsync(value))" />
        }
        else if (Setting.Type == typeof(string) && Setting.Options.Count > 0)
        {
            <ChoiceSettingEditor Value="@((string)Value!)" Options="@Setting.Options" Label="@Setting.Attribute.Label" ValueChanged="@((string value) => ChangeAsync(value))" />
        }
        else if (Setting.Type == typeof(string))
        {
            <TextSettingEditor Value="@((string)Value!)" Label="@Setting.Attribute.Label" ValueChanged="@((string value) => ChangeAsync(value))" />
        }
        else
        {
            <NumberSettingEditor Setting="@Setting" Value="@Value!" ValueChanged="ChangeAsync" />
        }
    </div>
</div>

@code {
    [Parameter, EditorRequired]
    public SettingsBase Section { get; set; } = default!;

    [Parameter, EditorRequired]
    public SettingProperty Setting { get; set; } = default!;

    private object? Value => Setting.Read(Section);

    private Task ChangeAsync(object? value) => State.SetAsync(Section, Setting.Name, value);
}
```

`Blazemoji.Components/Components/Settings/SettingRow.razor.css`:

```css
/* The words take the room that is left and the control keeps one width, so a row is the
   same size whatever its value is. */
.setting-row-words {
    flex: 1 1 0;
    min-width: 0;
}

.setting-row-control {
    flex: 0 0 12rem;
}

@media (max-width: 600px) {
    .setting-row {
        flex-direction: column;
        align-items: stretch !important;
    }

    .setting-row-control {
        flex-basis: auto;
        justify-content: flex-start !important;
    }
}
```

- [ ] **Step 6: Write the section view, the dialog and the button**

`Blazemoji.Components/Components/Settings/SettingsSectionView.razor`:

```razor
@inject SettingsState State

@foreach (var group in SettingProperties.Grouped(State.ShownIn(Section)))
{
    <div class="mb-4" @key="group.Name">
        @if (group.Name.Length > 0)
        {
            <MudText Typo="Typo.subtitle2" Class="mud-text-secondary" data-testid="settings-group-name">@group.Name</MudText>
        }
        @foreach (var setting in group.Settings)
        {
            <SettingRow @key="setting.Name" Section="@Section" Setting="@setting" />
        }
    </div>
}

<MudButton Variant="Variant.Text" Disabled="@State.IsAtDefaults(Section)" OnClick="@(() => State.RestoreDefaultsAsync(Section))" data-testid="restore-defaults">Restore defaults</MudButton>

@code {
    [Parameter, EditorRequired]
    public SettingsBase Section { get; set; } = default!;
}
```

`Blazemoji.Components/Components/Settings/SettingsDialog.razor`:

```razor
@implements IDisposable
@inject SettingsState State

<MudDialog>
    <DialogContent>
        @if (State.SaveFailed)
        {
            <MudAlert Severity="Severity.Warning" Dense="true" Class="mb-3" data-testid="settings-not-saved">Your settings could not be saved, so changes last only until Blazemoji is closed.</MudAlert>
        }
        @if (State.Sections.Count > 1)
        {
            <SegmentedTabs AriaLabel="Sections of settings" @bind-Active="_section">
                @foreach (var section in State.Sections)
                {
                    <SegmentedTab @key="section.SettingsId" Id="@section.SettingsId" Text="@section.DisplayName" Icon="@section.Icon">
                        <SettingsSectionView Section="@section" />
                    </SegmentedTab>
                }
            </SegmentedTabs>
        }
        else if (State.Sections.Count == 1)
        {
            <SettingsSectionView Section="@State.Sections[0]" />
        }
    </DialogContent>
</MudDialog>

@code {
    private string? _section;

    protected override void OnInitialized()
    {
        State.SettingsChanged += OnSettingsChanged;
        State.SaveFailedChanged += OnSaveFailedChanged;
    }

    private void OnSettingsChanged(SettingsBase section)
    {
        InvokeAsync(StateHasChanged);
    }

    private void OnSaveFailedChanged()
    {
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        State.SettingsChanged -= OnSettingsChanged;
        State.SaveFailedChanged -= OnSaveFailedChanged;
    }
}
```

`Blazemoji.Components/Components/Settings/SettingsButton.razor`:

```razor
@inject SettingsState State
@inject IDialogService Dialogs

@* A dialog gives focus back to the button that opened it, and a tooltip that shows on focus
   would then sit over the title bar. *@
<MudTooltip Text="Settings" ShowOnFocus="false">
    <MudIconButton Icon="@BlazemojiIcons.Settings" Color="Color.Inherit" OnClick="OpenAsync" aria-label="Settings" data-testid="settings-button" />
</MudTooltip>

@code {
    private static DialogOptions Options => new() { CloseOnEscapeKey = true, CloseButton = true, FullWidth = true, MaxWidth = MaxWidth.Small };

    private async Task OpenAsync()
    {
        await State.LoadAsync();
        await Dialogs.ShowAsync<SettingsDialog>("Settings", Options);
    }
}
```

- [ ] **Step 7: Put the button in the title bar**

In `Blazemoji.Components/Layout/AppShell.razor`, between `<MudSpacer />` and the dark mode `<MudTooltip ...>`:

```razor
        <SettingsButton />
```

In `Blazemoji.E2E/EditorPage.cs`, replace

```csharp
        public ILocator ThemeToggle => Page.Locator("header button.mud-icon-button").First;
```

with

```csharp
        public ILocator ThemeToggle => Page.GetByTestId("dark-mode-toggle");
```

- [ ] **Step 8: Run the tests to see them pass**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.SettingsPanelTests`
Then: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Components.AppShellTests`
Expected: `failed: 0` for both. If `A_number_typed_out_of_range_ends_at_the_limit` or the emptied-field test cannot find the `input`, look at `Row(cut, "Size").Markup` and change the selector to the field's own input; what each asserts about the state stays as written.

- [ ] **Step 9: Look at it**

Run the website (`dotnet run --project Blazemoji`), open Settings in light and in dark, at a wide window and at 1024 by 768, and keep a screenshot of each for Tom. Check by eye: nothing in a row moves when a value changes; the button reads "Restore defaults" as typed; the dialog's controls match the app's other fields.

- [ ] **Step 10: Commit**

```bash
git add Blazemoji.Components/Components/Settings Blazemoji.Components/_Imports.razor Blazemoji.Components/Layout/AppShell.razor Blazemoji.E2E/EditorPage.cs Blazemoji.Test/Settings/SettingsPanelTests.cs Blazemoji.Test/Components/AppShellTests.cs
git commit -m "A Settings button in the title bar opens a panel drawn from the settings themselves"
```

---

### Task 8: The editor takes its options from the settings

**Files:**
- Modify: `Blazemoji.Components/Components/EmojiCodeEditor.razor`
- Modify: `Blazemoji.Test/Components/EmojiCodeEditorTests.cs`
- Modify: `Blazemoji.Test/Components/WorkspaceTests.cs` (the constructor's registrations)

**Interfaces:**
- Consumes: `SettingsState` (`Get<EditorSettings>()`, `LoadAsync()`, `SettingsChanged`), `EditorSettings`, `EditorOptionValues` (`From`, `ApplyTo`, `ToUpdateOptions`), `TimeProvider` (registered by `AddBlazemojiEditor`), BlazorMonaco's `StandaloneCodeEditor.UpdateOptions(EditorUpdateOptions)`, which calls the script function `blazorMonaco.editor.updateOptions` with the editor's id and the options as JSON.
- Produces: nothing new for later tasks. The editor waits at most two seconds for kept settings before it shows.

- [ ] **Step 1: Register what the editor now asks for, in the two test classes that build their own services**

In `Blazemoji.Test/Components/EmojiCodeEditorTests.cs` add usings:

```csharp
using System.Text.Json;
using Blazemoji.Services.Settings;
using Blazemoji.Settings;
using Blazemoji.Test.Settings;
using Microsoft.Extensions.Time.Testing;
```

fields:

```csharp
        private const string UpdateOptions = "blazorMonaco.editor.updateOptions";

        private readonly ISettingsStore _settingsStore = Substitute.For<ISettingsStore>();
        private readonly FakeTimeProvider _clock = new();
```

and at the end of the constructor:

```csharp
            _settingsStore.LoadAsync().Returns(Task.FromResult<string?>(null));
            Services.Configure<SettingsOptions>(settings => settings.Add<EditorSettings>().Add<SampleSettings>());
            Services.AddSingleton(_settingsStore);
            Services.AddSingleton<TimeProvider>(_clock);
            Services.AddScoped<SettingsState>();
```

In `Blazemoji.Test/Components/WorkspaceTests.cs` add `using Blazemoji.Services.Settings;` and `using Blazemoji.Settings;`, and after `Services.AddScoped<LayoutState>();`:

```csharp
            Services.Configure<SettingsOptions>(settings => settings.Add<EditorSettings>());
            Services.AddScoped<SettingsState>();
```

- [ ] **Step 2: Write the failing tests**

Add to `Blazemoji.Test/Components/EmojiCodeEditorTests.cs`:

```csharp
        private SettingsState Settings => Services.GetRequiredService<SettingsState>();

        private int FontSizesSent(int size) =>
            JSInterop.Invocations[UpdateOptions].Count(sent => ((JsonElement)sent.Arguments[1]!).GetProperty("fontSize").GetInt32() == size);

        [Fact]
        public void The_editor_is_made_with_the_settings_as_they_stand()
        {
            var cut = Render<EmojiCodeEditor>();

            var options = cut.Instance.Editor.ConstructionOptions!(cut.Instance.Editor);
            options.FontSize.ShouldBe(14);
            options.WordWrap.ShouldBe("off");
            options.Minimap.Enabled.ShouldBe(true);
            options.LineNumbers.ShouldBe("on");
            options.RenderWhitespace.ShouldBe("selection");
            options.TabSize.ShouldBe(2);
        }

        [Fact]
        public void With_nothing_kept_the_editor_is_not_told_to_change_anything()
        {
            Render<EmojiCodeEditor>();

            JSInterop.Invocations[UpdateOptions].ShouldBeEmpty();
        }

        [Fact]
        public async Task A_change_to_an_editor_setting_reaches_the_editor_once()
        {
            var cut = Render<EmojiCodeEditor>();

            await cut.InvokeAsync(() => Settings.SetAsync(Settings.Get<EditorSettings>(), nameof(EditorSettings.FontSize), 18));

            cut.WaitForAssertion(() => FontSizesSent(18).ShouldBe(1));
            JSInterop.Invocations[UpdateOptions].Count.ShouldBe(1);
        }

        [Fact]
        public async Task A_change_to_another_section_leaves_the_editor_alone()
        {
            var cut = Render<EmojiCodeEditor>();
            var renders = cut.RenderCount;

            await cut.InvokeAsync(() => Settings.SetAsync(Settings.Get<SampleSettings>(), nameof(SampleSettings.Size), 20));

            cut.RenderCount.ShouldBe(renders);
            JSInterop.Invocations[UpdateOptions].ShouldBeEmpty();
        }

        [Fact]
        public async Task A_redraw_while_the_editor_is_still_taking_its_options_does_not_send_them_again()
        {
            // The browser has not answered the first change yet.
            var taking = JSInterop.SetupVoid(UpdateOptions, _ => true);
            var cut = Render<EmojiCodeEditor>();
            await cut.InvokeAsync(() => Settings.SetAsync(Settings.Get<EditorSettings>(), nameof(EditorSettings.FontSize), 18));
            cut.WaitForAssertion(() => JSInterop.Invocations[UpdateOptions].Count.ShouldBe(1));

            cut.Render();
            cut.Render();

            JSInterop.Invocations[UpdateOptions].Count.ShouldBe(1);
            taking.SetVoidResult();
        }

        [Fact]
        public void Kept_settings_are_in_place_before_the_editor_says_it_is_ready()
        {
            _settingsStore.LoadAsync().Returns(Task.FromResult<string?>("{ \"editor\": { \"fontSize\": 20 } }"));
            int? sentWhenReady = null;

            Render<EmojiCodeEditor>(parameters => parameters.Add(editor => editor.Ready, () => sentWhenReady = FontSizesSent(20)));

            sentWhenReady.ShouldBe(1);
        }

        [Fact]
        public async Task A_setting_changed_before_the_editor_has_started_is_in_place_when_it_is_ready()
        {
            // The colours are still being read, so the editor has not finished starting.
            var colours = _module.SetupVoid(ApplyTheme, _ => true);
            int? sentWhenReady = null;
            var cut = Render<EmojiCodeEditor>(parameters => parameters.Add(editor => editor.Ready, () => sentWhenReady = FontSizesSent(22)));
            sentWhenReady.ShouldBeNull();

            await cut.InvokeAsync(() => Settings.SetAsync(Settings.Get<EditorSettings>(), nameof(EditorSettings.FontSize), 22));
            colours.SetVoidResult();

            await EventuallyAsync(() => sentWhenReady.ShouldBe(1));
        }

        [Fact]
        public async Task A_store_that_does_not_answer_holds_the_editor_back_for_two_seconds_and_no_longer()
        {
            var answering = new TaskCompletionSource<string?>();
            _settingsStore.LoadAsync().Returns(answering.Task);
            var ready = false;
            var cut = Render<EmojiCodeEditor>(parameters => parameters.Add(editor => editor.Ready, () => ready = true));
            ready.ShouldBeFalse();

            _clock.Advance(TimeSpan.FromSeconds(1));
            ready.ShouldBeFalse();

            // The wait starts on whichever thread the editor's start-up reached it, so the clock
            // is moved until the editor has seen it.
            await EventuallyAsync(() =>
            {
                _clock.Advance(TimeSpan.FromSeconds(1));
                ready.ShouldBeTrue();
            });

            await cut.InvokeAsync(() => answering.SetResult("{ \"editor\": { \"fontSize\": 20 } }"));
            cut.WaitForAssertion(() => FontSizesSent(20).ShouldBe(1));
        }
```

`EventuallyAsync` is already in this file, and gives up after about two seconds. `UpdateOptions` reaches the page through `InvokeVoidAsync`, which is why `SetupVoid` can hold it. Before running these, read each `await` in them: on the unchanged editor, `Settings.SetAsync` still completes (it does not depend on the editor) and `EventuallyAsync` gives up on its own, so none can hang.

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Components.EmojiCodeEditorTests`
Expected: the new tests fail (`options.FontSize` is null, `FontSizesSent(18)` is 0, `ready` is true at once in the last). The tests that were there before still pass.

- [ ] **Step 4: Change the editor**

In `Blazemoji.Components/Components/EmojiCodeEditor.razor`:

At the top, after `@inject EmojicodeLanguageInterop _language`:

```razor
@using Blazemoji.Settings
@inject SettingsState _settings
@inject TimeProvider _clock
```

Among the fields, after `private bool _coloursAreDue;`:

```csharp
    // How long the editor waits for kept settings before it shows itself with the defaults.
    private static readonly TimeSpan LongestWaitForSettings = TimeSpan.FromSeconds(2);

    // What Monaco was last told. Null until it has been told anything.
    private EditorOptionValues? _applied;
```

In `EditorConstructionOptions`, replace `return new StandaloneEditorConstructionOptions` with `var options = new StandaloneEditorConstructionOptions`, and after the object's closing `};` add:

```csharp
        _applied = EditorOptionValues.From(_settings.Get<EditorSettings>());
        _applied.ApplyTo(options);
        return options;
```

In `InitEditor`, between `await _language.ApplyThemeAsync();` and `_initializing = false;`:

```csharp
        await WaitForSettingsAsync();
        await BringOptionsInLineAsync();
```

Add before `OnParametersSet`:

```csharp
    protected override void OnInitialized()
    {
        _settings.SettingsChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(SettingsBase section)
    {
        if (section is EditorSettings)
            InvokeAsync(StateHasChanged);
    }

    // Kept settings are worth a short wait, so that the editor first appears as it was left.
    // A store that is slow or broken must not keep it hidden: what arrives late is applied
    // after the redraw its arrival asks for.
    private async Task WaitForSettingsAsync()
    {
        var loading = _settings.LoadAsync();
        if (!loading.IsCompleted)
        {
            using var givingUp = new CancellationTokenSource();
            var waited = Task.Delay(LongestWaitForSettings, _clock, givingUp.Token);
            if (await Task.WhenAny(loading, waited) != loading)
                return;

            await givingUp.CancelAsync();
        }

        await loading;
    }

    private async Task BringOptionsInLineAsync()
    {
        var wanted = EditorOptionValues.From(_settings.Get<EditorSettings>());
        if (_applied is null || wanted == _applied)
            return;

        // Remembered before the call, not after: a redraw while Monaco is still answering
        // would otherwise send the same options again.
        _applied = wanted;
        await _editor.UpdateOptions(wanted.ToUpdateOptions());
    }
```

In `OnAfterRenderAsync`, after the block that applies the colours:

```csharp
        if (!_initializing)
            await BringOptionsInLineAsync();
```

In `Dispose`, as the first line:

```csharp
        _settings.SettingsChanged -= OnSettingsChanged;
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Components.EmojiCodeEditorTests`
Then: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Components.WorkspaceTests`
Then: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.HostingTests`
Expected: `failed: 0` for all three.

- [ ] **Step 6: Run the last new test with every core busy**

It moves a clock while the editor starts, which is the kind of test that has been racy here before.

```bash
for i in $(seq 1 12); do yes > /dev/null & done
for i in 1 2 3 4 5 6; do dotnet test --no-build --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Components.EmojiCodeEditorTests | tail -4; done
kill $(jobs -p)
```

Expected: `failed: 0` six times.

- [ ] **Step 7: Commit**

```bash
git add Blazemoji.Components/Components/EmojiCodeEditor.razor Blazemoji.Test/Components/EmojiCodeEditorTests.cs Blazemoji.Test/Components/WorkspaceTests.cs
git commit -m "The editor takes its options from the settings and follows them as they change"
```

---

### Task 9: The desktop app

**Files:**
- Modify: `Blazemoji.Desktop/Program.cs`
- Modify: `Blazemoji.Desktop/Scripts/smoke.ts`, then rebuild `Blazemoji.Desktop/wwwroot/js/smoke.js` with `scripts/build-js.sh`
- Modify: `Blazemoji.Desktop/Smoke/SmokeRun.razor`

**Interfaces:**
- Consumes: `SettingsState`, `SettingsOptions`, `SettingHosts.Desktop`, `ISettingsStore` (the `FileSettingsStore` that `AddBlazemojiProjectsOnDisk` registers), the panel's markup from Task 7 (`settings-button`, `[data-testid=setting][data-setting=Minimap]`).
- Produces: two smoke checks, `a-setting-reaches-the-editor` (page) and `settings-are-kept-on-disk` (host).

Nothing in this part behaves differently on the desktop for being told it is the desktop: there is no desktop-only setting yet. The two registrations in step 3 are what part 2's update settings depend on, and what makes the app's one set of settings outlive a scope. The smoke checks are the test that the panel, the editor and the file work inside the real window.

- [ ] **Step 1: Add the two checks**

In `Blazemoji.Desktop/Scripts/smoke.ts`, add as the last entry of the `checks` array:

```ts
    ["a-setting-reaches-the-editor", async () => {
        const minimap = (): boolean => editor().getOption(monaco.editor.EditorOption.minimap).enabled;
        must(minimap(), "The minimap is off before any setting was changed.");
        const button = testId("settings-button");
        must(button, "There is no Settings button.");
        button.click();
        const toggle = await until(() => document.querySelector<HTMLInputElement>("[data-testid=setting][data-setting=Minimap] input"), 10000);
        must(toggle, `The Settings dialog did not show a Minimap setting. ${windowSize()}`);
        toggle.click();
        must(await until(() => !minimap(), 5000), "Turning the minimap off in Settings did not reach the editor.");
        document.querySelector<HTMLElement>(".mud-dialog .mud-button-close")?.click();
        must(await until(() => !document.querySelector(".mud-dialog"), 5000), "The Settings dialog did not close.");
    }],
```

In `Blazemoji.Desktop/Smoke/SmokeRun.razor` add `@using Blazemoji.Services.Settings` and `@inject ISettingsStore SettingsStore`, a constant beside the others:

```csharp
    private const string SettingsOnDisk = "settings-are-kept-on-disk";
```

after `checks.Add(await TheLayoutIsOnDiskAsync());`:

```csharp
        checks.Add(await TheSettingsAreOnDiskAsync());
```

and the method:

```csharp
    /// <summary>The page's checks turn the minimap off in the panel. This is where that has to end up.</summary>
    private async Task<SmokeCheckOutcome> TheSettingsAreOnDiskAsync()
    {
        var timer = Stopwatch.StartNew();
        var file = Path.Combine(Session.ProjectsRoot, ".blazemoji", "settings.json");
        string? problem;
        try
        {
            // The store takes its turns in order, so this is answered after the save the switch started.
            var kept = await SettingsStore.LoadAsync();
            problem = kept is null ? "No settings were kept after the minimap was turned off."
                : !kept.Contains("\"minimap\": false") ? "The settings that were kept do not have the minimap off."
                : !File.Exists(file) ? "The settings were kept somewhere other than the projects folder."
                : null;
        }
        catch (ProjectStoreException exception)
        {
            problem = "The kept settings could not be read (" + exception.InnerException?.GetType().Name + ").";
        }

        return new SmokeCheckOutcome(SettingsOnDisk, problem is null, timer.ElapsedMilliseconds, problem);
    }
```

- [ ] **Step 2: Build the script and run the smoke test to see the new checks**

Run: `scripts/build-js.sh && scripts/desktop-smoke.sh`
Expected: `HERMES_SMOKE_CHECK_PASS: a-setting-reaches-the-editor` and `HERMES_SMOKE_CHECK_PASS: settings-are-kept-on-disk`. They pass already, because Tasks 3, 4 and 7 gave the window its store, its state and its panel. To see each fail for the right reason once: change `"\"minimap\": false"` in the host's check to `"\"minimap\": true"`, run again, expect `HERMES_SMOKE_CHECK_FAIL: settings-are-kept-on-disk: The settings that were kept do not have the minimap off.`, and put it back.

`scripts/build-js.sh` rewrites `Blazemoji.Desktop/wwwroot/js/smoke.js`, which is committed.

- [ ] **Step 3: Say this host is the desktop, with one state for the app**

In `Blazemoji.Desktop/Program.cs` add `using Blazemoji.Services.Settings;` and `using Blazemoji.Shared.Models.Settings;`, and after `builder.Services.AddSingleton<LayoutState>();`:

```csharp
        builder.Services.AddSingleton<SettingsState>();
        builder.Services.Configure<SettingsOptions>(settings => settings.Host = SettingHosts.Desktop);
```

- [ ] **Step 4: Pin it with a test of the same registrations**

The test project does not reference the desktop host, so this makes the registrations step 3 makes and checks what they give. It passes as soon as it is written: it holds what Tasks 4 and 5 built, for a host that registers this way.

Add to `Blazemoji.Test/Settings/SettingsPerVisitorTests.cs`:

```csharp
        [Fact]
        public void A_host_with_one_user_has_one_set_and_can_say_it_is_the_desktop()
        {
            var services = new ServiceCollection();
            services.AddSingleton<SettingsState>();
            services.Configure<SettingsOptions>(settings => settings.Host = SettingHosts.Desktop);
            services.Configure<SettingsOptions>(settings => settings.Add<DesktopOnlySettings>());
            services.AddBlazemojiEditor();
            using var provider = services.BuildServiceProvider(validateScopes: true);
            using var one = provider.CreateScope();
            using var two = provider.CreateScope();

            var state = one.ServiceProvider.GetRequiredService<SettingsState>();

            state.ShouldBeSameAs(two.ServiceProvider.GetRequiredService<SettingsState>());
            state.Sections.Select(section => section.SettingsId).ShouldBe(["editor", "desktopOnly"]);
        }
```

with `using Blazemoji.Shared.Models.Settings;` at the top.

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.Settings.SettingsPerVisitorTests`
Expected: `failed: 0`.

- [ ] **Step 5: Run the smoke test again, as a release is built**

Run: `scripts/desktop-smoke.sh --published`
Expected: `HERMES_SMOKE_RESULT: PASSED` with both new checks among those passed.

- [ ] **Step 6: Commit**

```bash
git add Blazemoji.Desktop/Program.cs Blazemoji.Desktop/Scripts/smoke.ts Blazemoji.Desktop/wwwroot/js/smoke.js Blazemoji.Desktop/Smoke/SmokeRun.razor Blazemoji.Test/Settings/SettingsPerVisitorTests.cs
git commit -m "The desktop app has one set of settings, and checks that a change reaches the editor and the disk"
```

---

### Task 10: In a real browser

**Files:**
- Modify: `Blazemoji.E2E/EditorPage.cs`
- Create: `Blazemoji.E2E/SettingsFlowsTests.cs`

**Interfaces:**
- Consumes: `BrowserFixture`, `EditorPage` (`OpenAsync`, `Page`, `Dialog`, `OpenTabAsync`), the panel's markup from Task 7, the Library's `[data-testid=clear-saved]` and its confirm button named "Delete".
- Produces: `EditorPage.SettingsButton`, `EditorPage.Setting(string)`, `EditorPage.OpenSettingsAsync()`, `EditorPage.CloseSettingsAsync()`, `EditorPage.FontSizeAsync()`.

- [ ] **Step 1: Add to the page object**

In `Blazemoji.E2E/EditorPage.cs`, beside the other locators:

```csharp
        public ILocator SettingsButton => Page.GetByTestId("settings-button");

        /// <summary>The row of one setting in the open Settings dialog, by its property's name.</summary>
        public ILocator Setting(string name) => Page.Locator($"[data-testid=setting][data-setting={name}]");

        public async Task OpenSettingsAsync()
        {
            await SettingsButton.ClickAsync();
            await Page.GetByTestId("setting").First.WaitForAsync();
        }

        public async Task CloseSettingsAsync()
        {
            await Dialog.Locator(".mud-button-close").ClickAsync();
            await Dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        }

        /// <summary>The size the editor's text is drawn at, as the browser has it.</summary>
        public Task<string> FontSizeAsync() =>
            Page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.monaco-editor .view-lines')).fontSize");
```

- [ ] **Step 2: Write the tests**

`Blazemoji.E2E/SettingsFlowsTests.cs`:

```csharp
namespace Blazemoji.E2E
{
    /// <summary>
    /// Settings from the panel to the editor and back after a reload. Only a real browser has
    /// local storage, draws Monaco's text at a size, and gives a dialog's focus back.
    /// </summary>
    public class SettingsFlowsTests(BrowserFixture browser)
    {
        // After a reload the editor is not there at first, and a predicate that throws ends the wait.
        private const string FontSizeIs = "size => { const lines = document.querySelector('.monaco-editor .view-lines'); return !!lines && getComputedStyle(lines).fontSize === size; }";
        private const string MinimapIs = "on => monaco.editor.getEditors()[0].getOption(monaco.editor.EditorOption.minimap).enabled === on";

        // Records the size of the editor's text at the moment the editor is first shown, which
        // is too brief a moment to catch from outside the page.
        private const string RememberTheSizeWhenShown = @"(() => {
            const look = () => {
                const editor = document.querySelector('.editor');
                const lines = document.querySelector('.monaco-editor .view-lines');
                if (editor && lines && !editor.classList.contains('invisible')) {
                    window.__fontSizeWhenShown = getComputedStyle(lines).fontSize;
                    return;
                }
                requestAnimationFrame(look);
            };
            requestAnimationFrame(look);
        })();";

        private static async Task SetFontSizeAsync(EditorPage editor, string size)
        {
            var field = editor.Setting("FontSize").Locator("input");
            await field.FillAsync(size);
            await field.PressAsync("Tab");
            await editor.Page.WaitForFunctionAsync(FontSizeIs, size + "px");
        }

        [Theory]
        [InlineData(1920, 1080)]
        [InlineData(1024, 768)]
        public async Task A_font_size_chosen_in_the_panel_changes_the_editors_text_without_a_reload(int width, int height)
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.Page.SetViewportSizeAsync(width, height);
            (await editor.FontSizeAsync()).ShouldBe("14px");

            await editor.OpenSettingsAsync();
            await SetFontSizeAsync(editor, "20");

            (await editor.FontSizeAsync()).ShouldBe("20px");
            await editor.CloseSettingsAsync();
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task Turning_the_minimap_off_takes_it_away_and_restoring_defaults_brings_it_back()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.OpenSettingsAsync();

            await editor.Setting("Minimap").Locator(".mud-switch").ClickAsync();
            await editor.Page.WaitForFunctionAsync(MinimapIs, false);

            await editor.Page.GetByTestId("restore-defaults").ClickAsync();
            await editor.Page.WaitForFunctionAsync(MinimapIs, true);
        }

        [Fact]
        public async Task After_a_reload_the_text_is_the_chosen_size_when_the_editor_first_shows()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.OpenSettingsAsync();
            await SetFontSizeAsync(editor, "20");
            await editor.CloseSettingsAsync();

            await editor.Page.AddInitScriptAsync(RememberTheSizeWhenShown);
            await editor.Page.ReloadAsync();
            await editor.Page.WaitForFunctionAsync("() => window.__fontSizeWhenShown !== undefined");

            (await editor.Page.EvaluateAsync<string>("() => window.__fontSizeWhenShown")).ShouldBe("20px");
            (await editor.FontSizeAsync()).ShouldBe("20px");
        }

        [Fact]
        public async Task Another_visitor_has_the_defaults()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var one = await EditorPage.OpenAsync(browser);
            await one.OpenSettingsAsync();
            await SetFontSizeAsync(one, "20");

            await using var other = await EditorPage.OpenAsync(browser);

            (await other.FontSizeAsync()).ShouldBe("14px");
            (await one.FontSizeAsync()).ShouldBe("20px");
        }

        [Fact]
        public async Task Clearing_the_saved_files_in_the_library_leaves_the_settings_alone()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.OpenSettingsAsync();
            await SetFontSizeAsync(editor, "20");
            await editor.CloseSettingsAsync();

            await editor.OpenTabAsync("Library");
            await editor.Page.GetByTestId("clear-saved").ClickAsync();
            await editor.Dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();
            await editor.Dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
            await editor.Page.ReloadAsync();

            await editor.Page.WaitForFunctionAsync(FontSizeIs, "20px");
        }

        [Fact]
        public async Task After_the_panel_is_closed_no_tooltip_is_left_over_the_title_bar()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.OpenSettingsAsync();

            await editor.CloseSettingsAsync();
            await editor.Page.WaitForTimeoutAsync(600);

            (await editor.Page.Locator(".mud-tooltip.mud-popover-open").CountAsync()).ShouldBe(0);
            await editor.ThemeToggle.ClickAsync();
            await editor.Page.WaitForFunctionAsync("() => document.querySelector('.monaco-editor.vs-dark') !== null");
        }
    }
}
```

Every predicate handed to `WaitForFunctionAsync` here is an ordinary function, not an `async` one: an `async` predicate returns a promise, which is truthy, and the wait would return at once.

- [ ] **Step 3: Run them against the container stack**

Run: `scripts/e2e.sh`
Expected: every test in `SettingsFlowsTests` passes, and so does everything that passed before, `ShellFlowsTests.The_theme_toggle_switches_the_editor_to_dark_and_back` among them (it uses the locator Task 7 changed). If the last test's dark mode wait fails because the colour scheme starts dark, wait for the class to differ from what it was before the click.

Since the code is already written, see each new test fail for the right reason once: in `Blazemoji.Components/Components/EmojiCodeEditor.razor` comment out the two lines added to `InitEditor`, run `scripts/e2e.sh`, and expect `After_a_reload_the_text_is_the_chosen_size_when_the_editor_first_shows` to fail with `"14px"` where `"20px"` was wanted. Put the lines back.

- [ ] **Step 4: Commit**

```bash
git add Blazemoji.E2E/EditorPage.cs Blazemoji.E2E/SettingsFlowsTests.cs
git commit -m "Browser tests of settings: applied at once, kept over a reload, and each visitor's own"
```

---

### Task 11: The hosting guide

**Files:**
- Modify: `docs/hosting.md`
- Modify: `Blazemoji.Test/HostingTests.cs`

**Interfaces:**
- Consumes: everything above, by name.
- Produces: the document a host follows, and tests that read it.

- [ ] **Step 1: Write the failing test**

Add to `Blazemoji.Test/HostingTests.cs`:

```csharp
        [Fact]
        public void The_document_says_what_a_host_does_for_settings_and_uses_the_word_in_one_sense()
        {
            var document = Document();

            document.ShouldContain("`ISettingsStore`");
            document.ShouldContain("`LocalStorageSettingsStore`");
            document.ShouldContain("`FileSettingsStore`");
            document.ShouldContain("settings.json");
            document.ShouldContain("SettingHosts.Desktop");
            document.ShouldContain("`SettingsState`");
            document.ShouldContain("## Adding a setting");
            document.ShouldNotContain("## The one setting");
        }
```

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.HostingTests`
Expected: this test fails on `ISettingsStore`.

- [ ] **Step 2: Change the document**

In `docs/hosting.md`:

1. Rename the heading `## The one setting: where the toolchain service is` to `## The one piece of configuration: where the toolchain service is`, and in the paragraph under it change "the address is one setting" to "the address is one piece of configuration". In "The desktop host", change "Settings are read from environment variables and the command line" to "Configuration is read from environment variables and the command line".

2. In the table under "Services to register", after the `ILayoutStore` row, add:

```markdown
| `ISettingsStore` (optional) | Where a person's settings are kept between sessions, as one piece of text. Without one they last as long as the page. | `LocalStorageSettingsStore`: local storage, under `blazemoji.settings`. | `FileSettingsStore`: one file beside the projects, registered by `AddBlazemojiProjectsOnDisk`. |
| `SettingsOptions.Host` (optional) | Which host this is, so that a setting marked for one host is not shown, read or kept on the other. A host that says nothing is taken for the website. | The default. | `services.Configure<SettingsOptions>(settings => settings.Host = SettingHosts.Desktop)`. |
```

3. In the sentence after the table, change "`IProjectStore`, `ILibraryService` or `ILayoutStore`" to "`IProjectStore`, `ILibraryService`, `ILayoutStore` or `ISettingsStore`", and add at the end of that paragraph: "Settings that cannot be kept are logged and the Settings panel says so; a change still holds until the page or the app is closed."

4. In the "State lifetime" bullet, change the list to "(`RunState`, `ProjectState`, `RequestState`, `LayoutState`, `SettingsState`, `LocalStorageFiles`)".

5. In "In the page shell", item 1, change "and the dark mode button" to ", the Settings button and the dark mode button", and add after that paragraph: "A host that draws its own shell places `<SettingsButton />` (`Blazemoji.Components.Settings`) wherever it wants the panel opened from."

6. In the picture of the projects folder, after the `layout.json` line, add:

```
    settings.json         what was changed in the Settings panel, and nothing that is still at its default
```

7. In "Its test", in the sentence listing what the page checks, add "a setting turned off in the panel reaches the editor" before "a dragged divider is heard", and in the sentence listing what the host adds, change "and the layout the drag left is in the projects folder" to "the layout the drag left is in the projects folder, and so is the setting that was changed".

8. Add a new section before "## Projects on disk":

````markdown
## Adding a setting

Settings are what a person chooses in the app, as opposed to configuration, which whoever runs the app sets. A section of settings is a class, and a setting is a property of it:

```csharp
[Setting(Label = "Font size", Description = "How large the editor's text is, in pixels.", Group = "Text", Order = 1, Min = 8, Max = 32)]
public int FontSize { get; private set; } = 14;
```

That is all it takes for the setting to be shown in the Settings panel, kept between sessions and read by whatever acts on it (`SettingsState.Get<EditorSettings>().FontSize`). The rules:

- A setting is a `bool`, an `int`, a `double` or a `string`. A string with `Options = "a,b,c"` is a choice between those.
- The value a new section has is the default. Only what differs from its default is kept, so a default changed in a later version reaches everyone who left it alone.
- The setter is private. Only `SettingsState` changes a setting (`SetAsync`), and it raises `SettingsChanged` with the section when it has.
- `Hosts = SettingHosts.Desktop` on a setting, or `Hosts` overridden on the section, keeps it to one host.
- `Hide = true` keeps a setting without showing it in the panel.
- A new section is a class deriving from `SettingsBase`, added with `services.Configure<SettingsOptions>(settings => settings.Add<YourSettings>())`.
- A component that acts on a setting subscribes to `SettingsChanged`, asks for a redraw, and does its work after the redraw, as `EmojiCodeEditor` does for the editor's options.
````

9. In "What is not here yet", add:

```markdown
- Two tabs of the website share one browser's storage and each holds its own copy of the settings. The tab that changes a setting last writes its whole set, so a change made in the other tab since it loaded is lost.
- A setting kept by a newer version is dropped when an older version next saves.
- Dark mode is not a setting and is not kept.
```

- [ ] **Step 3: Run the tests to see them pass**

Run: `dotnet test --project Blazemoji.Test/Blazemoji.Test.csproj -- --filter-class Blazemoji.Test.HostingTests`
Expected: `failed: 0`, `The_document_lists_exactly_the_registrations_these_tests_make` among them: the first `csharp` block in the document is unchanged.

- [ ] **Step 4: Commit**

```bash
git add docs/hosting.md Blazemoji.Test/HostingTests.cs
git commit -m "The hosting guide says what a host does for settings and how a setting is added"
```

---

### Task 12: The whole thing, as it will be run

**Files:** none changed unless something is found.

- [ ] **Step 1: Everything, with the real compiler**

Run: `scripts/test-in-docker.sh`
Expected: every project reports `failed: 0`. A run the emulator froze is retried by the script; a test that fails is not.

- [ ] **Step 2: The other way the tests are run**

Run: `docker build -f Blazemoji/Dockerfile --target test --output type=cacheonly --progress=plain .`
Expected: success. This stage copies only some folders; no new test reads a repository file outside `docs/` and the projects, so it should need no change. If it fails for a missing file, add that folder to the stage's `COPY` lines.

- [ ] **Step 3: The browser tests and the desktop app**

Run: `scripts/e2e.sh`, then `scripts/desktop-smoke.sh --published`
Expected: both pass.

- [ ] **Step 4: The way Tom runs it**

Build the image his IDE builds and see the panel work in it:

```bash
docker build -f Blazemoji/Dockerfile -t blazemoji:settings-check .
docker run --rm -d --name blazemoji-settings-check -p 5055:8080 -e ASPNETCORE_ENVIRONMENT=Development blazemoji:settings-check
```

Open http://localhost:5055, change the font size in Settings, reload, and see it kept. Then `docker stop blazemoji-settings-check` and `docker image rm blazemoji:settings-check`.

- [ ] **Step 5: A slow line, by hand**

Start the website (`dotnet run --project Blazemoji`) and put `scratchpad/smoke/slow-proxy.mjs` from the earlier layout review in front of it at 250 ms each way (it was kept in an earlier session's scratch folder; if it is gone, write the same thing: a TCP proxy that holds each chunk for 250 ms). Through the proxy: type a font size and press Tab, then flip Word wrap and Minimap one straight after the other. Expected: each ends as it was left, and the font size field does not jump back. Write down what was seen.

- [ ] **Step 6: Check the spec's goal, line by line**

Open `docs/superpowers/specs/2026-10-09-settings-core-design.md` and, for each of the seven lines under "Done when", name the test or the step above that shows it. For line 7 run `grep -rn "Mythetech.Framework\|IMessageBus" --include="*.csproj" --include="*.cs" --include="*.razor" . | grep -v "/bin/\|/obj/"` and expect nothing but comments.

- [ ] **Step 7: Push**

```bash
git push -u origin settings-core
```

Then `git switch --detach` and leave the worktree in place. The repository is public: if the pre-push hook refuses (weekdays 9 to 5 Eastern), do not bypass it. Say that the branch is unpushed, with its name and the worktree's path. Do not merge and do not open a pull request.

- [ ] **Step 8: Report**

To Tom: the branch, the worktree, whether it is pushed, the screenshots from Task 7, what the slow line showed, and anything in the spec's "Decisions taken on Tom's behalf" that turned out differently in the building.
