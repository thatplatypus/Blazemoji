using Blazemoji.Components.Settings;
using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
using Blazemoji.Shared.Models.Settings;
using Blazemoji.Shared.State;
using Blazemoji.Test.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace Blazemoji.Test.Settings
{
    public sealed class SettingsPanelTests : BunitContext
    {
        private readonly ISettingsStore _store = Substitute.For<ISettingsStore>();

        public SettingsPanelTests()
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            _store.LoadAsync().Returns(Task.FromResult<string?>(null));
        }

        private SettingsState UseState(SettingHosts host = SettingHosts.Web, bool alsoDesktopOnly = false)
        {
            var options = new SettingsOptions { Host = host };
            options.Add<SampleSettings>();
            if (alsoDesktopOnly)
                options.Add<DesktopOnlySettings>();

            var state = new SettingsState(Options.Create(options), new RecordingLogger<SettingsState>(), _store);
            Services.AddSingleton(state);
            return state;
        }

        private IRenderedComponent<SettingsSectionView> RenderSection(SettingsState state) =>
            Render<SettingsSectionView>(parameters => parameters.Add(view => view.Section, state.Get<SampleSettings>()));

        private static IRenderedComponent<SettingRow> Row(IRenderedComponent<SettingsSectionView> cut, string setting) =>
            cut.FindComponents<SettingRow>().Single(row => row.Instance.Setting.Name == setting);

        private async Task<IRenderedComponent<MudDialogProvider>> OpenDialogAsync()
        {
            var provider = Render<MudDialogProvider>();
            var dialogs = Services.GetRequiredService<IDialogService>();
            await provider.InvokeAsync(() => dialogs.ShowAsync<SettingsDialog>("Settings"));
            return provider;
        }

        [Fact]
        public void A_section_shows_its_settings_in_order_under_their_groups_without_the_hidden_or_the_other_hosts()
        {
            var cut = RenderSection(UseState());

            cut.FindAll("[data-testid=setting]").Select(row => row.GetAttribute("data-setting"))
                .ShouldBe(["Size", "Ratio", "Wrap", "Whitespace", "Name"]);
            cut.FindAll("[data-testid=settings-group-name]").Select(name => name.TextContent.Trim())
                .ShouldBe(["Numbers", "Switches", "Choices", "Text"]);
            cut.Markup.ShouldContain("Carry long lines over.");
        }

        [Fact]
        public void On_the_desktop_the_desktops_setting_is_shown_too()
        {
            var cut = RenderSection(UseState(SettingHosts.Desktop));

            cut.FindAll("[data-testid=setting]").Select(row => row.GetAttribute("data-setting")).ShouldContain("Channel");
        }

        [Fact]
        public async Task Each_kind_of_control_hands_its_new_value_to_the_state()
        {
            var state = UseState();
            var sample = state.Get<SampleSettings>();
            var cut = RenderSection(state);

            await cut.InvokeAsync(() => Row(cut, "Wrap").FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(true));
            await cut.InvokeAsync(() => Row(cut, "Size").FindComponent<MudNumericField<int>>().Instance.ValueChanged.InvokeAsync(20));
            await cut.InvokeAsync(() => Row(cut, "Ratio").FindComponent<MudNumericField<double>>().Instance.ValueChanged.InvokeAsync(2.5));
            await cut.InvokeAsync(() => Row(cut, "Whitespace").FindComponent<MudSelect<string>>().Instance.ValueChanged.InvokeAsync("all"));
            await cut.InvokeAsync(() => Row(cut, "Name").FindComponent<MudTextField<string>>().Instance.ValueChanged.InvokeAsync("other"));

            sample.Wrap.ShouldBeTrue();
            sample.Size.ShouldBe(20);
            sample.Ratio.ShouldBe(2.5);
            sample.Whitespace.ShouldBe("all");
            sample.Name.ShouldBe("other");
        }

        [Fact]
        public async Task A_number_typed_out_of_range_ends_at_the_limit()
        {
            var state = UseState();
            var cut = RenderSection(state);

            await Row(cut, "Size").Find("input").ChangeAsync(new ChangeEventArgs { Value = "99" });

            state.Get<SampleSettings>().Size.ShouldBe(32);
        }

        [Fact]
        public async Task A_number_field_that_is_emptied_leaves_the_setting_within_its_limits()
        {
            var state = UseState();
            var cut = RenderSection(state);

            await Row(cut, "Size").Find("input").ChangeAsync(new ChangeEventArgs { Value = "" });

            state.Get<SampleSettings>().Size.ShouldBeInRange(8, 32);
        }

        [Fact]
        public async Task Restore_defaults_is_off_at_the_defaults_on_after_a_change_and_puts_them_back()
        {
            var state = UseState();
            var sample = state.Get<SampleSettings>();
            var cut = RenderSection(state);
            cut.Find("[data-testid=restore-defaults]").HasAttribute("disabled").ShouldBeTrue();

            await cut.InvokeAsync(() => state.SetAsync(sample, nameof(SampleSettings.Size), 20));
            cut.Render();
            cut.Find("[data-testid=restore-defaults]").HasAttribute("disabled").ShouldBeFalse();

            await cut.Find("[data-testid=restore-defaults]").ClickAsync();

            sample.Size.ShouldBe(14);
            cut.Find("[data-testid=restore-defaults]").HasAttribute("disabled").ShouldBeTrue();
        }

        [Fact]
        public async Task With_one_section_the_dialog_has_no_tabs()
        {
            UseState();

            var dialog = await OpenDialogAsync();

            dialog.FindAll("[data-testid=setting]").ShouldNotBeEmpty();
            dialog.FindAll(".segmented-tabs").ShouldBeEmpty();
        }

        [Fact]
        public async Task With_more_than_one_section_they_are_chosen_with_tabs()
        {
            UseState(SettingHosts.Desktop, alsoDesktopOnly: true);

            var dialog = await OpenDialogAsync();

            dialog.FindAll(".segmented-tabs").ShouldHaveSingleItem();
            dialog.Markup.ShouldContain("Sample");
            dialog.Markup.ShouldContain("Desktop only");
        }

        [Fact]
        public async Task A_change_made_elsewhere_is_shown_in_the_open_dialog()
        {
            var state = UseState();
            var dialog = await OpenDialogAsync();

            await dialog.InvokeAsync(() => state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Size), 20));

            dialog.WaitForAssertion(() => dialog.Find("[data-testid=restore-defaults]").HasAttribute("disabled").ShouldBeFalse());
        }

        [Fact]
        public async Task The_dialog_says_so_in_fixed_words_when_settings_could_not_be_saved()
        {
            var state = UseState();
            _store.SaveAsync(Arg.Any<string>()).Returns(Task.FromException(new ProjectStoreException("The browser's storage could not be used.", new IOException("QuotaExceededError at C:\\secret"))));
            var dialog = await OpenDialogAsync();
            dialog.FindAll("[data-testid=settings-not-saved]").ShouldBeEmpty();

            await dialog.InvokeAsync(() => state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Size), 20));

            dialog.WaitForAssertion(() => dialog.Find("[data-testid=settings-not-saved]").TextContent
                .ShouldContain("Your settings could not be saved, so changes last only until Blazemoji is closed."));
            dialog.Markup.ShouldNotContain("QuotaExceededError");
        }
    }
}
