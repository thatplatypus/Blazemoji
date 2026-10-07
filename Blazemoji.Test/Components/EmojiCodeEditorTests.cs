using Blazemoji.Components;
using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Interop;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Shared.State;
using BlazorMonaco;
using BlazorMonaco.Editor;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;

namespace Blazemoji.Test.Components
{
    public sealed class EmojiCodeEditorTests : BunitContext
    {
        private const string CreateModel = "blazorMonaco.editor.createModel";
        private const string ExecuteEdits = "blazorMonaco.editor.executeEdits";
        private const string TextBeforeCursor = "textBeforeCursor";
        private const int ExclamationKey = (int)KeyMod.Shift | (int)KeyCode.Digit1;

        private readonly BunitJSModuleInterop _module;

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

        private IRenderedComponent<EmojiCodeEditor> RenderWithTheCursorAfter(string textBeforeCursor)
        {
            _module.Setup<string>(TextBeforeCursor, _ => true).SetResult(textBeforeCursor);
            JSInterop.Setup<Selection>("blazorMonaco.editor.getSelection", _ => true)
                .SetResult(new Selection { StartLineNumber = 1, StartColumn = 1, EndLineNumber = 1, EndColumn = 1, PositionLineNumber = 1, PositionColumn = 1 });
            return Render<EmojiCodeEditor>();
        }

        private string? TextTyped() =>
            JSInterop.Invocations[ExecuteEdits].Select(invocation => ((List<IdentifiedSingleEditOperation>)invocation.Arguments[2]!).Single().Text).SingleOrDefault();

        [Theory]
        [InlineData("😀 🔤Hello World", "!")]
        [InlineData("💭 What a day", "!")]
        [InlineData("😀 🔤Hello World🔤", "❗")]
        [InlineData("", "❗")]
        public async Task The_exclamation_key_types_a_plain_mark_in_a_string_or_comment_and_the_emoji_in_code(string before, string typed)
        {
            var cut = RenderWithTheCursorAfter(before);

            await cut.InvokeAsync(() => cut.Instance.Editor.CommandCallback(ExclamationKey));

            cut.WaitForAssertion(() => TextTyped().ShouldBe(typed));
        }

        [Fact]
        public async Task A_shortcut_that_is_the_same_everywhere_does_not_ask_where_the_cursor_is()
        {
            var cut = RenderWithTheCursorAfter("😀 🔤Hello World");

            await cut.InvokeAsync(() => cut.Instance.Editor.CommandCallback((int)KeyMod.CtrlCmd | (int)KeyCode.KeyP));

            cut.WaitForAssertion(() => TextTyped().ShouldBe("😀"));
            _module.Invocations[TextBeforeCursor].ShouldBeEmpty();
        }
    }
}
