using Blazemoji.Services.Library;
using Blazored.LocalStorage;
using NSubstitute;

namespace Blazemoji.Test.Projects
{
    public class LibraryServiceTests
    {
        [Fact]
        public async Task Clearing_the_library_removes_saved_snippets_and_leaves_everything_else()
        {
            var localStorage = Substitute.For<ILocalStorageService>();
            localStorage.KeysAsync(Arg.Any<CancellationToken>())
                .Returns(new ValueTask<IEnumerable<string>>(["a.🍇", "old.emojic", "blazemoji.projects", "blazemoji.project.abc", "theme"]));

            await new LibraryService(localStorage).ClearLocalStorageAsync();

            await localStorage.Received(1).RemoveItemAsync("a.🍇", Arg.Any<CancellationToken>());
            await localStorage.Received(1).RemoveItemAsync("old.emojic", Arg.Any<CancellationToken>());
            await localStorage.DidNotReceive().RemoveItemAsync("blazemoji.projects", Arg.Any<CancellationToken>());
            await localStorage.DidNotReceive().RemoveItemAsync("blazemoji.project.abc", Arg.Any<CancellationToken>());
            await localStorage.DidNotReceive().RemoveItemAsync("theme", Arg.Any<CancellationToken>());
            await localStorage.DidNotReceive().ClearAsync(Arg.Any<CancellationToken>());
        }

        // A project keeps each file under a key that ends in the file's name, so it ends in
        // .🍇 as a snippet's key does. It is a project's file all the same.
        private const string AProjectsFile = "blazemoji.project.abc.file.app/main.🍇";

        [Fact]
        public async Task Clearing_the_library_does_not_remove_the_files_of_projects()
        {
            var localStorage = Substitute.For<ILocalStorageService>();
            localStorage.KeysAsync(Arg.Any<CancellationToken>())
                .Returns(new ValueTask<IEnumerable<string>>(["a.🍇", AProjectsFile, "blazemoji.project.abc", "blazemoji.projects"]));

            await new LibraryService(localStorage).ClearLocalStorageAsync();

            await localStorage.Received(1).RemoveItemAsync("a.🍇", Arg.Any<CancellationToken>());
            await localStorage.DidNotReceive().RemoveItemAsync(AProjectsFile, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task The_files_of_projects_are_not_listed_as_saved_snippets()
        {
            var localStorage = Substitute.For<ILocalStorageService>();
            localStorage.KeysAsync(Arg.Any<CancellationToken>())
                .Returns(new ValueTask<IEnumerable<string>>(["a.🍇", AProjectsFile]));
            localStorage.GetItemAsStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>("code"));

            var saved = await new LibraryService(localStorage).GetUserSavedFiles();

            saved.Select(file => file.Name).ShouldBe(["a.🍇"]);
        }
    }
}
