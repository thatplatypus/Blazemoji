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
    }
}
