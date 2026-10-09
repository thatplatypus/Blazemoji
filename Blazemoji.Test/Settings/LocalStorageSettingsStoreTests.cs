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
