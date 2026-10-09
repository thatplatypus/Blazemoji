using Blazemoji.Services.Layout;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Layout;
using Blazored.LocalStorage;
using Microsoft.JSInterop;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.Layout
{
    public class LocalStorageLayoutStoreTests
    {
        private const string Key = "blazemoji.layout";

        private readonly Dictionary<string, string> _browser = [];
        private readonly ILocalStorageService _localStorage = Substitute.For<ILocalStorageService>();

        public LocalStorageLayoutStoreTests()
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

        private LocalStorageLayoutStore CreateStore() => new(_localStorage);

        [Fact]
        public async Task A_browser_with_nothing_kept_has_no_layout()
        {
            (await CreateStore().LoadAsync()).ShouldBeNull();
        }

        [Fact]
        public async Task A_layout_that_was_kept_is_read_back_on_the_next_visit()
        {
            var layout = new WorkspaceLayout(0.31, 0.55, true);

            await CreateStore().SaveAsync(layout);

            (await CreateStore().LoadAsync()).ShouldBe(layout);
        }

        [Fact]
        public async Task It_is_kept_under_one_key_that_starts_as_the_project_stores_keys_do()
        {
            await CreateStore().SaveAsync(WorkspaceLayout.Default);

            // The library clears what it saved by the look of a key, and leaves these alone.
            _browser.Keys.ShouldBe([Key]);
            Key.ShouldStartWith(LocalStorageProjectStore.KeyPrefix);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json at all")]
        [InlineData("[1, 2, 3]")]
        [InlineData("null")]
        public async Task An_entry_that_is_not_a_layout_counts_as_no_layout(string kept)
        {
            _browser[Key] = kept;

            (await CreateStore().LoadAsync()).ShouldBeNull();
        }

        [Fact]
        public async Task What_is_read_is_mended_and_filled_in_from_the_default()
        {
            _browser[Key] = "{ \"sidebarShare\": 7 }";

            (await CreateStore().LoadAsync()).ShouldBe(WorkspaceLayout.Default with { SidebarShare = WorkspaceLayout.LargestShare });
        }

        [Fact]
        public async Task A_browser_whose_storage_cannot_be_read_is_reported_as_the_store_failing()
        {
            _localStorage.GetItemAsStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new JSException("The operation is insecure."));

            await Should.ThrowAsync<ProjectStoreException>(() => CreateStore().LoadAsync());
        }

        [Fact]
        public async Task A_browser_whose_storage_is_full_is_reported_as_the_store_failing()
        {
            _localStorage.SetItemAsStringAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new JSException("QuotaExceededError"));

            await Should.ThrowAsync<ProjectStoreException>(() => CreateStore().SaveAsync(WorkspaceLayout.Default));
        }
    }
}
