using System.Text.Json;
using Blazemoji.Components;
using Blazemoji.Emojicode;
using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Interop;
using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
using Blazemoji.Settings;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Shared.State;
using Blazemoji.Test.Settings;
using BlazorMonaco;
using BlazorMonaco.Editor;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;
using NSubstitute;

namespace Blazemoji.Test.Components
{
    public sealed class EmojiCodeEditorTests : BunitContext
    {
        private const string CreateModel = "blazorMonaco.editor.createModel";
        private const string AroundCursor = "aroundCursor";
        private const string TypeFunction = "type";
        private const string ApplyTheme = "applyTheme";
        private const string UpdateOptions = "blazorMonaco.editor.updateOptions";
        private const int ExclamationKey = (int)KeyMod.Shift | (int)KeyCode.Digit1;
        private const int GrapesKey = (int)KeyMod.Shift | (int)KeyCode.BracketLeft;
        private const int WatermelonKey = (int)KeyMod.Shift | (int)KeyCode.BracketRight;

        private readonly BunitJSModuleInterop _module;
        private readonly ISettingsStore _settingsStore = Substitute.For<ISettingsStore>();
        private readonly FakeTimeProvider _clock = new();

        public EmojiCodeEditorTests()
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            _module = JSInterop.SetupModule();
            _module.SetupModule("register", _ => true);
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);

            // The editor registers the language's providers, which answer from the open project.
            var templates = Substitute.For<IProjectTemplates>();
            templates.All.Returns([new ProjectTemplate("hello-world", "Hello World", "One file.", ProjectKind.Program, "main.🍇", [new ProjectFile("main.🍇", "🏁 🍇 🍉")])]);
            Services.AddSingleton(templates);
            Services.AddSingleton(Substitute.For<IProjectStore>());
            Services.AddSingleton(Substitute.For<ICodeIntelligence>());
            Services.AddSingleton(Substitute.For<IPackageLibrary>());
            Services.AddScoped<ProjectState>();
            Services.AddScoped<EmojicodeLanguageInterop>();
            _settingsStore.LoadAsync().Returns(Task.FromResult<string?>(null));
            Services.Configure<SettingsOptions>(settings => settings.Add<EditorSettings>().Add<SampleSettings>());
            Services.AddSingleton(_settingsStore);
            Services.AddSingleton<TimeProvider>(_clock);
            Services.AddScoped<SettingsState>();
        }

        private void ModelsAreMadeAtOnce()
        {
            for (var created = 1; created <= 3; created++)
            {
                var uri = $"inmemory://blazemoji/{created}";
                JSInterop.Setup<TextModel>(CreateModel, invocation => (string?)invocation.Arguments[2] == uri)
                    .SetResult(new TextModel { Id = "model-" + created, Uri = uri });
            }
        }

        [Fact]
        public async Task A_file_opened_while_markers_are_being_set_does_not_break_the_marking()
        {
            ModelsAreMadeAtOnce();
            var settingMarkers = JSInterop.SetupVoid("blazorMonaco.editor.setModelMarkers", _ => true);
            var cut = Render<EmojiCodeEditor>();
            await cut.InvokeAsync(() => cut.Instance.OpenFileAsync("one", "1"));
            await cut.InvokeAsync(() => cut.Instance.OpenFileAsync("two", "2"));

            var marking = cut.InvokeAsync(() => cut.Instance.SetMarkersAsync([], _ => "one", _ => string.Empty));
            await cut.InvokeAsync(() => cut.Instance.OpenFileAsync("three", "3"));
            settingMarkers.SetVoidResult();

            await Should.NotThrowAsync(() => marking);
        }

        [Fact]
        public async Task The_text_of_a_file_is_read_from_that_files_own_model()
        {
            ModelsAreMadeAtOnce();
            JSInterop.Setup<string>("blazorMonaco.editor.model.getValue", invocation => (string?)invocation.Arguments[0] == "inmemory://blazemoji/1").SetResult("text of one");
            JSInterop.Setup<string>("blazorMonaco.editor.model.getValue", invocation => (string?)invocation.Arguments[0] == "inmemory://blazemoji/2").SetResult("text of two");
            var cut = Render<EmojiCodeEditor>();
            await cut.InvokeAsync(() => cut.Instance.OpenFileAsync("one", "1"));
            await cut.InvokeAsync(() => cut.Instance.OpenFileAsync("two", "2"));

            (await cut.InvokeAsync(() => cut.Instance.GetCodeAsync("one"))).ShouldBe("text of one");
            (await cut.InvokeAsync(() => cut.Instance.GetCodeAsync("two"))).ShouldBe("text of two");
            (await cut.InvokeAsync(() => cut.Instance.GetCodeAsync("never opened"))).ShouldBeNull();
        }

        [Fact]
        public async Task A_file_that_is_being_closed_is_already_gone_to_anything_that_asks_for_its_text()
        {
            ModelsAreMadeAtOnce();
            // The browser has not finished getting rid of the model yet.
            var closing = JSInterop.SetupVoid("blazorMonaco.editor.model.dispose", _ => true);
            var reading = JSInterop.Setup<string>("blazorMonaco.editor.model.getValue", _ => true).SetResult("text of one");
            var cut = Render<EmojiCodeEditor>();
            await cut.InvokeAsync(() => cut.Instance.OpenFileAsync("one", "1"));
            var closed = cut.InvokeAsync(() => cut.Instance.CloseFilesExceptAsync(new HashSet<string>()));

            (await cut.InvokeAsync(() => cut.Instance.GetCodeAsync("one"))).ShouldBeNull();
            reading.Invocations.ShouldBeEmpty();

            closing.SetVoidResult();
            await closed;
        }

        /// <summary>The editor with the cursor at the end of <paramref name="before"/> and <paramref name="after"/> following it on the line.</summary>
        private IRenderedComponent<EmojiCodeEditor> RenderWithTheCursorAfter(string before, string after = "", string selected = "")
        {
            _module.Setup<AroundCursorAnswer>(AroundCursor, _ => true).SetResult(new AroundCursorAnswer(before, selected, after, "as it was read"));
            return Render<EmojiCodeEditor>();
        }

        private IReadOnlyList<TypedText> Typed() =>
            _module.Invocations[TypeFunction].Select(invocation => (TypedText)invocation.Arguments[1]!).ToList();

        [Theory]
        [InlineData("😀 🔤Hello World", "!")]
        [InlineData("💭 What a day", "!")]
        [InlineData("😀 🔤Hello World🔤", "❗")]
        [InlineData("", "❗")]
        public async Task The_exclamation_key_types_a_plain_mark_in_a_string_or_comment_and_the_emoji_in_code(string before, string typed)
        {
            var cut = RenderWithTheCursorAfter(before);

            await cut.InvokeAsync(() => cut.Instance.Editor.CommandCallback(ExclamationKey));

            cut.WaitForAssertion(() => Typed().ShouldHaveSingleItem().Text.ShouldBe(typed));
        }

        [Fact]
        public async Task A_shortcut_that_is_the_same_everywhere_does_not_ask_where_the_cursor_is()
        {
            var cut = RenderWithTheCursorAfter("😀 🔤Hello World");

            await cut.InvokeAsync(() => cut.Instance.Editor.CommandCallback((int)KeyMod.CtrlCmd | (int)KeyCode.KeyP));

            cut.WaitForAssertion(() => Typed().ShouldHaveSingleItem().Text.ShouldBe("😀"));
            _module.Invocations[AroundCursor].ShouldBeEmpty();
        }

        [Fact]
        public async Task The_key_for_grapes_brings_the_watermelon_and_leaves_the_cursor_between_them()
        {
            var cut = RenderWithTheCursorAfter("🏁 ");

            await cut.InvokeAsync(() => cut.Instance.Editor.CommandCallback(GrapesKey));

            cut.WaitForAssertion(() => Typed().ShouldHaveSingleItem().ShouldBe(new TypedText(0, 0, "🍇🍉", 2, 2, "🍇", "as it was read")));
        }

        [Fact]
        public async Task The_key_for_grapes_is_a_plain_brace_inside_a_string_and_brings_nothing()
        {
            var cut = RenderWithTheCursorAfter("😀 🔤");

            await cut.InvokeAsync(() => cut.Instance.Editor.CommandCallback(GrapesKey));

            cut.WaitForAssertion(() => Typed().ShouldHaveSingleItem().ShouldBe(new TypedText(0, 0, "{", 1, 1, "{", "as it was read")));
        }

        [Fact]
        public async Task The_key_for_watermelon_steps_over_one_that_is_already_there()
        {
            var cut = RenderWithTheCursorAfter("🏁 🍇", after: "🍉");

            await cut.InvokeAsync(() => cut.Instance.Editor.CommandCallback(WatermelonKey));

            cut.WaitForAssertion(() => Typed().ShouldHaveSingleItem().ShouldBe(new TypedText(0, 2, "🍉", 2, 2, "🍉", "as it was read")));
        }

        [Fact]
        public async Task An_emoji_put_in_from_the_toolbox_is_paired_like_one_from_a_key()
        {
            var cut = RenderWithTheCursorAfter("↪️ ");

            await cut.InvokeAsync(() => cut.Instance.InsertTextAsync("🤜"));

            Typed().ShouldHaveSingleItem().Text.ShouldBe("🤜🤛");
        }

        [Fact]
        public async Task An_emoji_that_is_no_half_of_a_pair_is_put_in_without_asking_where_the_cursor_is()
        {
            var cut = RenderWithTheCursorAfter("↪️ ");

            await cut.InvokeAsync(() => cut.Instance.InsertTextAsync("👍"));

            Typed().ShouldHaveSingleItem().ShouldBe(new TypedText(0, 0, "👍", 2, 2, "👍", null));
            _module.Invocations[AroundCursor].ShouldBeEmpty();
        }

        [Fact]
        public async Task With_no_editor_to_look_around_in_the_text_is_still_handed_over_as_typed()
        {
            var cut = Render<EmojiCodeEditor>();

            await cut.InvokeAsync(() => cut.Instance.InsertTextAsync("🍇"));

            Typed().ShouldHaveSingleItem().ShouldBe(new TypedText(0, 0, "🍇", 2, 2, "🍇", null));
        }

        [Fact]
        public async Task A_second_key_waits_for_the_first_to_be_typed_before_it_looks_around()
        {
            // The browser has not answered the first key yet when the second is pressed.
            var firstLook = _module.Setup<AroundCursorAnswer>(AroundCursor, _ => true);
            var cut = Render<EmojiCodeEditor>();
            await cut.InvokeAsync(() => cut.Instance.Editor.CommandCallback(GrapesKey));
            await cut.InvokeAsync(() => cut.Instance.Editor.CommandCallback(WatermelonKey));

            _module.Invocations[AroundCursor].Count.ShouldBe(1);
            Typed().ShouldBeEmpty();

            firstLook.SetResult(new AroundCursorAnswer("🏁 ", string.Empty, string.Empty, "first"));

            await EventuallyAsync(() => Typed().Select(typed => typed.Plain).ShouldBe(["🍇", "🍉"]));
            _module.Invocations[AroundCursor].Count.ShouldBe(2);
        }

        // Typing renders nothing, so there is no render to wait for.
        private static async Task EventuallyAsync(Action assertion)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                try
                {
                    assertion();
                    return;
                }
                catch (ShouldAssertException)
                {
                    await Task.Delay(20, Xunit.TestContext.Current.CancellationToken);
                }
            }

            assertion();
        }

        [Fact]
        public void The_language_is_registered_with_the_pairs_and_comments_of_emojicode()
        {
            Render<EmojiCodeEditor>();

            var syntax = (LanguageSyntax)_module.Invocations["register"].ShouldHaveSingleItem().Arguments[2]!;
            syntax.Matched.Select(pair => pair[0] + pair[1]).ShouldBe(["🍇🍉", "🤜🤛", "🍿🍆", "🐚🍆"]);
            syntax.Completed.Select(pair => pair[0] + pair[1]).ShouldBe(["🍇🍉", "🤜🤛", "🍿🍆", "🐚🍆", "🔤🔤"]);
            syntax.LineComment.ShouldBe("💭");
            syntax.BlockComment.ShouldBe(["💭🔜", "🔚💭"]);
            syntax.Escape.ShouldBe("❌");
            syntax.Interpolation.ShouldBe(["🧲", "🧲"]);
        }

        [Fact]
        public void The_keys_for_emoji_only_act_while_the_text_has_the_keyboard()
        {
            // Without this a key pressed in the Find box types over the match in the file.
            Render<EmojiCodeEditor>();

            var keys = JSInterop.Invocations["blazorMonaco.editor.addCommand"]
                .Where(invocation => EmojicodeKeybindings.Keybindings.ContainsKey(Convert.ToInt32(invocation.Arguments[1])))
                .ToList();
            keys.Count.ShouldBe(EmojicodeKeybindings.Keybindings.Count);
            keys.ShouldAllBe(invocation => (string?)invocation.Arguments[2] == "editorTextFocus");
        }

        [Fact]
        public async Task Text_put_in_place_of_a_files_text_is_looked_at_for_how_it_is_indented()
        {
            ModelsAreMadeAtOnce();
            var cut = Render<EmojiCodeEditor>();
            await cut.InvokeAsync(() => cut.Instance.OpenFileAsync("one", "🏁 🍇 🍉"));

            await cut.InvokeAsync(() => cut.Instance.SetCodeAsync("🏁 🍇\n\t😀 🔤tabs🔤❗️\n🍉"));

            var looked = JSInterop.Invocations["blazorMonaco.editor.model.detectIndentation"].ShouldHaveSingleItem();
            looked.Arguments[0].ShouldBe("inmemory://blazemoji/1");
            looked.Arguments[1].ShouldBe(true);
            looked.Arguments[2].ShouldBe(2);
        }

        [Fact]
        public void The_editor_indents_with_two_spaces_as_emojicode_is_written()
        {
            var cut = Render<EmojiCodeEditor>();

            var options = cut.Instance.Editor.ConstructionOptions!(cut.Instance.Editor);
            options.TabSize.ShouldBe(2);
            options.InsertSpaces.ShouldBe(true);
        }

        [Fact]
        public void The_editor_takes_its_colours_from_the_page_when_it_starts()
        {
            Render<EmojiCodeEditor>();

            _module.Invocations[ApplyTheme].Count.ShouldBe(1);
        }

        [Fact]
        public void The_editor_takes_its_colours_again_when_the_page_goes_dark_or_light()
        {
            var cut = Render<ThemedHost>(parameters => parameters.Add(host => host.Dark, false));
            _module.Invocations[ApplyTheme].Count.ShouldBe(1);

            cut.Render(parameters => parameters.Add(host => host.Dark, true));
            _module.Invocations[ApplyTheme].Count.ShouldBe(2);

            cut.Render(parameters => parameters.Add(host => host.Dark, false));
            _module.Invocations[ApplyTheme].Count.ShouldBe(3);
        }

        [Fact]
        public void A_render_that_changes_nothing_about_dark_or_light_leaves_the_colours_alone()
        {
            var cut = Render<ThemedHost>(parameters => parameters.Add(host => host.Dark, true));
            var applied = _module.Invocations[ApplyTheme].Count;

            cut.Render(parameters => parameters.Add(host => host.Dark, true));

            _module.Invocations[ApplyTheme].Count.ShouldBe(applied);
        }

        [Fact]
        public async Task The_page_going_dark_while_the_editor_is_still_starting_is_not_missed()
        {
            // The browser is still reading the light colours when the page goes dark.
            var firstColours = _module.SetupVoid(ApplyTheme, _ => true);
            var cut = Render<ThemedHost>(parameters => parameters.Add(host => host.Dark, false));
            _module.Invocations[ApplyTheme].Count.ShouldBe(1);

            cut.Render(parameters => parameters.Add(host => host.Dark, true));
            _module.Invocations[ApplyTheme].Count.ShouldBe(1);

            firstColours.SetVoidResult();

            await EventuallyAsync(() => _module.Invocations[ApplyTheme].Count.ShouldBe(2));
        }

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

        /// <summary>A page that tells what is inside it whether it is dark, as the web host's layout does.</summary>
        private sealed class ThemedHost : ComponentBase
        {
            [Parameter]
            public bool Dark { get; set; }

            protected override void BuildRenderTree(RenderTreeBuilder builder)
            {
                builder.OpenComponent<CascadingValue<bool>>(0);
                builder.AddComponentParameter(1, nameof(CascadingValue<bool>.Name), "DarkMode");
                builder.AddComponentParameter(2, nameof(CascadingValue<bool>.Value), Dark);
                builder.AddComponentParameter(3, nameof(CascadingValue<bool>.ChildContent), (RenderFragment)(inner =>
                {
                    inner.OpenComponent<EmojiCodeEditor>(0);
                    inner.CloseComponent();
                }));
                builder.CloseComponent();
            }
        }
    }
}
