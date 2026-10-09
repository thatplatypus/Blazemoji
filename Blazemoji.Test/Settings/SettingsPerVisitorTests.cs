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
