using Blazemoji.Components;
using BlazorMonaco.Editor;
using Bunit;
using MudBlazor.Services;

namespace Blazemoji.Test.Components
{
    public sealed class EmojiCodeEditorTests : BunitContext
    {
        private const string CreateModel = "blazorMonaco.editor.createModel";

        public EmojiCodeEditorTests()
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
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
    }
}
