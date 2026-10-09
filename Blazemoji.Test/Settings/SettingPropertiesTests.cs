using Blazemoji.Services.Settings;
using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Test.Settings
{
    public class SettingPropertiesTests
    {
        private static SettingProperty Setting(string name) =>
            SettingProperties.Of(typeof(SampleSettings)).Single(setting => setting.Name == name);

        [Fact]
        public void A_sections_settings_are_its_attributed_properties_in_their_order()
        {
            SettingProperties.Of(new SampleSettings()).Select(setting => setting.Name)
                .ShouldBe(["Size", "Ratio", "Wrap", "Whitespace", "Name", "Remembered", "Channel"]);
        }

        [Fact]
        public void The_default_of_a_setting_is_what_a_new_section_has()
        {
            Setting("Size").Default.ShouldBe(14);
            Setting("Ratio").Default.ShouldBe(1.5);
            Setting("Wrap").Default.ShouldBe(false);
            Setting("Whitespace").Default.ShouldBe("selection");
        }

        [Fact]
        public void A_choice_knows_its_options_and_free_text_has_none()
        {
            Setting("Whitespace").Options.ShouldBe(["none", "selection", "all"]);
            Setting("Name").Options.ShouldBeEmpty();
        }

        [Theory]
        [InlineData("Size", 20, 20)]
        [InlineData("Size", 99, 32)]
        [InlineData("Size", 2, 8)]
        [InlineData("Size", 20.0, 20)]
        [InlineData("Ratio", 2, 2.0)]
        [InlineData("Ratio", 9.5, 3.0)]
        [InlineData("Ratio", 0.1, 0.5)]
        [InlineData("Wrap", true, true)]
        [InlineData("Whitespace", "all", "all")]
        [InlineData("Name", "anything at all", "anything at all")]
        public void A_value_of_the_right_kind_is_taken_and_a_number_is_brought_within_its_limits(string setting, object value, object accepted)
        {
            Setting(setting).TryAccept(value, out var taken).ShouldBeTrue();

            taken.ShouldBe(accepted);
        }

        [Theory]
        [InlineData("Size", 20.5)]
        [InlineData("Size", "20")]
        [InlineData("Size", true)]
        [InlineData("Size", null)]
        [InlineData("Ratio", double.NaN)]
        [InlineData("Ratio", double.PositiveInfinity)]
        [InlineData("Ratio", "1.5")]
        [InlineData("Wrap", 1)]
        [InlineData("Wrap", "true")]
        [InlineData("Whitespace", "everything")]
        [InlineData("Whitespace", "All")]
        [InlineData("Name", 5)]
        [InlineData("Name", null)]
        public void A_value_of_the_wrong_kind_or_not_among_the_options_is_refused(string setting, object? value)
        {
            Setting(setting).TryAccept(value, out object? _).ShouldBeFalse();
        }

        [Fact]
        public void Text_is_taken_up_to_its_limit_and_refused_beyond_it()
        {
            Setting("Name").TryAccept(new string('a', SettingProperty.LongestText), out object? _).ShouldBeTrue();
            Setting("Name").TryAccept(new string('a', SettingProperty.LongestText + 1), out object? _).ShouldBeFalse();
        }

        [Fact]
        public void A_setting_is_assigned_through_its_private_setter()
        {
            var section = new SampleSettings();

            Setting("Size").Assign(section, 20);

            section.Size.ShouldBe(20);
            Setting("Size").Read(section).ShouldBe(20);
        }

        [Fact]
        public void A_setting_says_which_hosts_it_is_for()
        {
            Setting("Size").IsFor(SettingHosts.Web).ShouldBeTrue();
            Setting("Size").IsFor(SettingHosts.Desktop).ShouldBeTrue();
            Setting("Channel").IsFor(SettingHosts.Web).ShouldBeFalse();
            Setting("Channel").IsFor(SettingHosts.Desktop).ShouldBeTrue();
        }

        [Fact]
        public void Groups_come_in_the_order_each_first_appears_with_their_settings_in_order()
        {
            var groups = SettingProperties.Grouped(SettingProperties.Of(typeof(SampleSettings)));

            groups.Select(group => group.Name).ShouldBe(["Numbers", "Switches", "Choices", "Text", "", "Desktop"]);
            groups[0].Settings.Select(setting => setting.Name).ShouldBe(["Size", "Ratio"]);
        }

        [Fact]
        public void A_setting_of_a_kind_that_is_not_supported_is_refused_when_the_section_is_first_read()
        {
            Should.Throw<NotSupportedException>(() => SettingProperties.Of(typeof(HasADate)));
        }

        private sealed class HasADate : SettingsBase
        {
            public override string SettingsId => "dated";

            public override string DisplayName => "Dated";

            public override string Icon => string.Empty;

            [Setting(Label = "When")]
            public DateTime When { get; private set; }
        }
    }
}
