using Blazemoji.Services.Settings;
using Blazemoji.Settings;
using Blazemoji.Shared.Models.Settings;
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
    }
}
