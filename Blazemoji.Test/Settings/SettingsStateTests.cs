using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
using Blazemoji.Shared.Models.Settings;
using Blazemoji.Shared.State;
using Blazemoji.Test.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.Settings
{
    public class SettingsStateTests
    {
        private readonly ISettingsStore _store = Substitute.For<ISettingsStore>();
        private readonly RecordingLogger<SettingsState> _logger = new();
        private readonly List<string> _happened = [];
        private string? _kept;

        public SettingsStateTests()
        {
            _store.LoadAsync().Returns(_ => Task.FromResult(_kept));
            _store.SaveAsync(Arg.Any<string>()).Returns(call =>
            {
                _kept = call.Arg<string>();
                _happened.Add("saved");
                return Task.CompletedTask;
            });
        }

        private SettingsState CreateState(SettingHosts host = SettingHosts.Web, bool withStore = true)
        {
            var options = new SettingsOptions { Host = host };
            options.Add<SampleSettings>().Add<DesktopOnlySettings>();
            var state = new SettingsState(Options.Create(options), _logger, withStore ? _store : null);
            state.SettingsChanged += section => _happened.Add("changed " + section.SettingsId);
            state.SaveFailedChanged += () => _happened.Add("save failed is " + state.SaveFailed);
            return state;
        }

        private static ProjectStoreException StorageOff() => new("The browser's storage could not be used.", new IOException("off"));

        [Fact]
        public void Before_anything_is_loaded_every_setting_is_at_its_default()
        {
            var state = CreateState();

            state.Get<SampleSettings>().Size.ShouldBe(14);
            state.IsAtDefaults(state.Get<SampleSettings>()).ShouldBeTrue();
        }

        [Fact]
        public async Task Loading_takes_what_was_kept_and_says_so_once_for_each_section_that_changed()
        {
            _kept = "{ \"sample\": { \"size\": 20, \"wrap\": true } }";
            var state = CreateState();

            await state.LoadAsync();

            state.Get<SampleSettings>().Size.ShouldBe(20);
            state.Get<SampleSettings>().Wrap.ShouldBeTrue();
            _happened.ShouldBe(["changed sample"]);
        }

        [Fact]
        public async Task Loading_with_nothing_kept_announces_nothing_and_does_not_read_twice()
        {
            var state = CreateState();

            await state.LoadAsync();
            await state.LoadAsync();

            _happened.ShouldBeEmpty();
            await _store.Received(1).LoadAsync();
        }

        [Fact]
        public async Task A_change_is_assigned_then_announced_then_saved()
        {
            var state = CreateState();
            var sample = state.Get<SampleSettings>();
            state.SettingsChanged += _ => sample.Size.ShouldBe(20);

            await state.SetAsync(sample, nameof(SampleSettings.Size), 20);

            _happened.ShouldBe(["changed sample", "saved"]);
            _kept.ShouldNotBeNull().ShouldContain("\"size\": 20");
        }

        [Fact]
        public async Task A_value_equal_to_the_current_one_does_nothing()
        {
            var state = CreateState();

            await state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Size), 14);

            _happened.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_number_out_of_range_is_brought_within_it_and_a_refused_value_changes_nothing()
        {
            var state = CreateState();
            var sample = state.Get<SampleSettings>();

            await state.SetAsync(sample, nameof(SampleSettings.Size), 400);
            await state.SetAsync(sample, nameof(SampleSettings.Ratio), double.NaN);
            await state.SetAsync(sample, nameof(SampleSettings.Whitespace), "everything");
            await state.SetAsync(sample, nameof(SampleSettings.Name), new string('a', SettingProperty.LongestText + 1));

            sample.Size.ShouldBe(32);
            sample.Ratio.ShouldBe(1.5);
            sample.Whitespace.ShouldBe("selection");
            sample.Name.ShouldBe("plain");
            _happened.ShouldBe(["changed sample", "saved"]);
        }

        [Fact]
        public async Task What_is_not_a_setting_of_this_host_is_a_mistake_in_the_caller()
        {
            var state = CreateState();
            var other = CreateState();

            await Should.ThrowAsync<ArgumentException>(() => state.SetAsync(state.Get<SampleSettings>(), "NotASetting", 2));
            await Should.ThrowAsync<ArgumentException>(() => state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Channel), "beta"));
            await Should.ThrowAsync<ArgumentException>(() => state.SetAsync(state.Get<DesktopOnlySettings>(), nameof(DesktopOnlySettings.CheckAtStart), false));
            await Should.ThrowAsync<ArgumentException>(() => state.SetAsync(other.Get<SampleSettings>(), nameof(SampleSettings.Size), 20));
        }

        [Fact]
        public async Task A_change_made_while_the_store_is_still_answering_lands_on_top_of_what_it_answers()
        {
            var answering = new TaskCompletionSource<string?>();
            _store.LoadAsync().Returns(answering.Task);
            var state = CreateState();
            var sample = state.Get<SampleSettings>();
            var loading = state.LoadAsync();

            var changing = state.SetAsync(sample, nameof(SampleSettings.Wrap), true);
            changing.IsCompleted.ShouldBeFalse();
            answering.SetResult("{ \"sample\": { \"size\": 20 } }");
            loading.IsCompleted.ShouldBeTrue();
            changing.IsCompleted.ShouldBeTrue();
            await loading;
            await changing;

            sample.Size.ShouldBe(20);
            sample.Wrap.ShouldBeTrue();
            _kept.ShouldNotBeNull().ShouldContain("\"size\": 20");
            _kept.ShouldContain("\"wrap\": true");
        }

        [Fact]
        public async Task A_store_that_cannot_be_read_leaves_the_defaults_is_logged_and_settings_still_change()
        {
            _store.LoadAsync().ThrowsAsync(StorageOff());
            var state = CreateState();

            await state.LoadAsync();
            await state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Size), 20);

            state.Get<SampleSettings>().Size.ShouldBe(20);
            _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task A_fault_in_the_store_reaches_whoever_asked_first_and_after_it_the_defaults_stand()
        {
            _store.LoadAsync().ThrowsAsync(new InvalidOperationException("a fault"));
            var state = CreateState();

            await Should.ThrowAsync<InvalidOperationException>(() => state.LoadAsync());
            await state.LoadAsync();
            await state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Size), 20);

            state.Get<SampleSettings>().Size.ShouldBe(20);
        }

        [Fact]
        public async Task A_save_that_fails_is_said_the_change_still_holds_and_the_next_that_succeeds_clears_it()
        {
            var state = CreateState();
            var sample = state.Get<SampleSettings>();
            _store.SaveAsync(Arg.Any<string>()).Returns(Task.FromException(StorageOff()), Task.CompletedTask);

            await state.SetAsync(sample, nameof(SampleSettings.Size), 20);
            state.SaveFailed.ShouldBeTrue();
            sample.Size.ShouldBe(20);

            await state.SetAsync(sample, nameof(SampleSettings.Size), 21);
            state.SaveFailed.ShouldBeFalse();

            _happened.ShouldBe(["changed sample", "save failed is True", "changed sample", "save failed is False"]);
            _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task An_earlier_save_that_fails_after_a_later_one_succeeded_raises_no_warning()
        {
            var first = new TaskCompletionSource();
            var second = new TaskCompletionSource();
            _store.SaveAsync(Arg.Any<string>()).Returns(first.Task, second.Task);
            var state = CreateState();
            var sample = state.Get<SampleSettings>();

            var one = state.SetAsync(sample, nameof(SampleSettings.Size), 20);
            var two = state.SetAsync(sample, nameof(SampleSettings.Size), 21);
            second.SetResult();
            two.IsCompleted.ShouldBeTrue();
            await two;
            first.SetException(StorageOff());
            one.IsCompleted.ShouldBeTrue();
            await one;

            state.SaveFailed.ShouldBeFalse();
        }

        [Fact]
        public async Task Restoring_defaults_puts_back_what_the_panel_shows_with_one_announcement_and_one_save()
        {
            var state = CreateState();
            var sample = state.Get<SampleSettings>();
            await state.SetAsync(sample, nameof(SampleSettings.Size), 20);
            await state.SetAsync(sample, nameof(SampleSettings.Wrap), true);
            await state.SetAsync(sample, nameof(SampleSettings.Remembered), 7);
            _happened.Clear();

            await state.RestoreDefaultsAsync(sample);
            await state.RestoreDefaultsAsync(sample);

            sample.Size.ShouldBe(14);
            sample.Wrap.ShouldBeFalse();
            sample.Remembered.ShouldBe(7);
            state.IsAtDefaults(sample).ShouldBeTrue();
            _happened.ShouldBe(["changed sample", "saved"]);
        }

        [Fact]
        public async Task The_website_has_neither_the_desktops_section_nor_its_settings()
        {
            _kept = "{ \"sample\": { \"channel\": \"beta\" }, \"desktopOnly\": { \"checkAtStart\": false } }";
            var state = CreateState(SettingHosts.Web);

            await state.LoadAsync();

            state.Sections.Select(section => section.SettingsId).ShouldBe(["sample"]);
            state.ShownIn(state.Get<SampleSettings>()).Select(setting => setting.Name).ShouldBe(["Size", "Ratio", "Wrap", "Whitespace", "Name"]);
            state.Get<SampleSettings>().Channel.ShouldBe("stable");
            state.Get<DesktopOnlySettings>().CheckAtStart.ShouldBeTrue();
        }

        [Fact]
        public async Task The_desktop_has_both_in_their_order()
        {
            _kept = "{ \"sample\": { \"channel\": \"beta\" }, \"desktopOnly\": { \"checkAtStart\": false } }";
            var state = CreateState(SettingHosts.Desktop);

            await state.LoadAsync();

            state.Sections.Select(section => section.SettingsId).ShouldBe(["sample", "desktopOnly"]);
            state.ShownIn(state.Get<SampleSettings>()).Select(setting => setting.Name).ShouldContain("Channel");
            state.Get<SampleSettings>().Channel.ShouldBe("beta");
            state.Get<DesktopOnlySettings>().CheckAtStart.ShouldBeFalse();
        }

        [Fact]
        public async Task With_no_store_a_change_holds_and_nothing_is_said_to_have_failed()
        {
            var state = CreateState(withStore: false);

            await state.LoadAsync();
            await state.SetAsync(state.Get<SampleSettings>(), nameof(SampleSettings.Size), 20);

            state.Get<SampleSettings>().Size.ShouldBe(20);
            state.SaveFailed.ShouldBeFalse();
        }

        [Fact]
        public void Asking_for_a_section_that_was_never_added_is_a_mistake_in_the_caller()
        {
            var state = new SettingsState(Options.Create(new SettingsOptions()), _logger);

            Should.Throw<InvalidOperationException>(() => state.Get<SampleSettings>());
        }
    }
}
