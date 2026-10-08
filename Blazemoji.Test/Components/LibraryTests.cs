using Blazemoji.Components;
using Blazemoji.Services.Library;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Components;
using Blazemoji.Shared.Models.Library;
using Blazemoji.Shared.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.Components
{
    public sealed class LibraryTests : BunitContext
    {
        private readonly ILibraryService _library = Substitute.For<ILibraryService>();
        private readonly ISamples _samples = Substitute.For<ISamples>();
        private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

        public LibraryTests()
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            _samples.AllAsync().Returns([new EmojicFile { Name = "HelloWorld.🍇", Code = "🏁 🍇 🍉" }]);
            _library.GetSavedAsync().Returns([new EmojicFile { Name = "Mine.🍇", Code = "🏁 🍇 🍉" }]);

            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            Services.AddSingleton(_library);
            Services.AddSingleton(_samples);
            Services.AddSingleton(_dialogs);
            Services.AddSingleton(new LocalStorageFiles());
        }

        private void TheDialogIsAnswered(DialogResult? answer)
        {
            var dialog = Substitute.For<IDialogReference>();
            dialog.Result.Returns(Task.FromResult(answer));
            _dialogs.ShowAsync<DestructiveDialog>(Arg.Any<string?>(), Arg.Any<DialogParameters<DestructiveDialog>>(), Arg.Any<DialogOptions?>())
                .Returns(Task.FromResult(dialog));
        }

        private IReadOnlyCollection<Snackbar> Shown => Services.GetRequiredService<ISnackbar>().ShownSnackbars.ToList();

        [Fact]
        public void The_tab_lists_the_samples_and_what_was_saved_without_saying_where_that_is_kept()
        {
            var cut = Render<Library>();

            cut.WaitForAssertion(() => cut.Markup.ShouldContain("Mine.🍇"));
            cut.Markup.ShouldContain("HelloWorld.🍇");
            cut.Markup.ShouldContain("Saved");
            cut.Markup.ShouldNotContain("Local Storage", Case.Insensitive);
        }

        [Fact]
        public async Task Clearing_what_was_saved_asks_first_and_then_clears()
        {
            TheDialogIsAnswered(DialogResult.Ok(true));
            var cut = Render<Library>();
            cut.WaitForAssertion(() => cut.Markup.ShouldContain("Mine.🍇"));
            _library.GetSavedAsync().Returns([]);

            await cut.Find("[data-testid=clear-saved] > .mud-treeview-item-content").ClickAsync();

            await _library.Received(1).ClearSavedAsync();
            cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Mine.🍇"));
            Shown.ShouldBeEmpty();
        }

        [Fact]
        public async Task Saved_files_that_cannot_be_cleared_are_reported_and_the_tab_stays_as_it_was()
        {
            TheDialogIsAnswered(DialogResult.Ok(true));
            _library.ClearSavedAsync().ThrowsAsync(new ProjectStoreException("read only", new IOException("/secret/path")));
            var cut = Render<Library>();
            cut.WaitForAssertion(() => cut.Markup.ShouldContain("Mine.🍇"));

            await cut.Find("[data-testid=clear-saved] > .mud-treeview-item-content").ClickAsync();

            Shown.Single().Message.ShouldBe("The saved files could not be cleared.");
            cut.Markup.ShouldContain("Mine.🍇");
        }

        [Fact]
        public void Saved_files_that_cannot_be_read_leave_the_samples_on_show()
        {
            _library.GetSavedAsync().ThrowsAsync(new ProjectStoreException("gone", new IOException("/secret/path")));

            var cut = Render<Library>();

            cut.WaitForAssertion(() => cut.Markup.ShouldContain("HelloWorld.🍇"));
            cut.Markup.ShouldNotContain("/secret/path");
        }
    }
}
