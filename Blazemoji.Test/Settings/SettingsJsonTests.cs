using Blazemoji.Services.Settings;
using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Test.Settings
{
    public class SettingsJsonTests
    {
        private readonly SampleSettings _sample = new();
        private readonly DesktopOnlySettings _desktopOnly = new();

        private SettingsBase[] Sections => [_sample, _desktopOnly];

        private static void Set(SettingsBase section, string name, object? value) =>
            SettingProperties.Of(section).Single(setting => setting.Name == name).Assign(section, value);

        private IReadOnlyList<KeptSetting> Read(string? text, SettingHosts host = SettingHosts.Desktop) =>
            SettingsJson.Read(text, Sections, host);

        private static object? ValueOf(IReadOnlyList<KeptSetting> kept, string name) =>
            kept.Single(one => one.Setting.Name == name).Value;

        [Fact]
        public void With_everything_at_its_default_nothing_is_written()
        {
            SettingsJson.Write(Sections, SettingHosts.Desktop).Trim().ShouldBe("{}");
        }

        [Fact]
        public void Only_what_was_changed_is_written_under_its_sections_id()
        {
            Set(_sample, "Size", 20);
            Set(_sample, "Wrap", true);

            var text = SettingsJson.Write(Sections, SettingHosts.Desktop);

            text.ShouldContain("\"sample\": {");
            text.ShouldContain("\"size\": 20");
            text.ShouldContain("\"wrap\": true");
            text.ShouldNotContain("ratio");
            text.ShouldNotContain("desktopOnly");
        }

        [Fact]
        public void What_was_written_is_read_back_the_same()
        {
            Set(_sample, "Size", 20);
            Set(_sample, "Ratio", 2.25);
            Set(_sample, "Wrap", true);
            Set(_sample, "Whitespace", "all");
            Set(_sample, "Name", "other");
            Set(_desktopOnly, "CheckAtStart", false);

            var kept = SettingsJson.Read(SettingsJson.Write(Sections, SettingHosts.Desktop), [new SampleSettings(), new DesktopOnlySettings()], SettingHosts.Desktop);

            ValueOf(kept, "Size").ShouldBe(20);
            ValueOf(kept, "Ratio").ShouldBe(2.25);
            ValueOf(kept, "Wrap").ShouldBe(true);
            ValueOf(kept, "Whitespace").ShouldBe("all");
            ValueOf(kept, "Name").ShouldBe("other");
            ValueOf(kept, "CheckAtStart").ShouldBe(false);
        }

        [Fact]
        public void A_section_and_a_setting_for_the_other_host_are_neither_written_nor_read()
        {
            Set(_sample, "Channel", "beta");
            Set(_desktopOnly, "CheckAtStart", false);

            SettingsJson.Write(Sections, SettingHosts.Web).Trim().ShouldBe("{}");
            Read("{ \"sample\": { \"channel\": \"beta\" }, \"desktopOnly\": { \"checkAtStart\": false } }", SettingHosts.Web).ShouldBeEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not json at all")]
        [InlineData("[1, 2, 3]")]
        [InlineData("null")]
        [InlineData("\"sample\"")]
        [InlineData("{ \"sample\": 5 }")]
        [InlineData("{ \"sample\": { \"size\": 20 ")]
        public void Text_that_is_not_settings_is_no_settings(string? text)
        {
            Read(text).ShouldBeEmpty();
        }

        [Fact]
        public void Text_nested_deeper_than_json_allows_is_no_settings()
        {
            var deep = new string('[', 500) + new string(']', 500);

            Read("{ \"sample\": { \"size\": " + deep + " } }").ShouldBeEmpty();
        }

        [Fact]
        public void What_this_version_does_not_know_is_passed_over()
        {
            var kept = Read("{ \"gone\": { \"size\": 1 }, \"sample\": { \"gone\": 1, \"notASetting\": 9, \"size\": 20 } }");

            kept.ShouldHaveSingleItem().Value.ShouldBe(20);
        }

        [Fact]
        public void A_value_that_would_be_refused_leaves_that_setting_alone_and_the_rest_are_still_read()
        {
            var kept = Read("{ \"sample\": { \"size\": \"big\", \"whitespace\": \"everything\", \"wrap\": 1, \"ratio\": 2 } }");

            kept.ShouldHaveSingleItem().Setting.Name.ShouldBe("Ratio");
        }

        [Fact]
        public void A_number_out_of_range_is_brought_within_it()
        {
            ValueOf(Read("{ \"sample\": { \"size\": 400 } }"), "Size").ShouldBe(32);
            ValueOf(Read("{ \"sample\": { \"size\": -400 } }"), "Size").ShouldBe(8);
        }

        [Fact]
        public void A_number_too_large_to_be_a_number_leaves_the_default()
        {
            Read("{ \"sample\": { \"size\": 1e999 } }").ShouldBeEmpty();
        }

        [Fact]
        public void Text_over_the_limit_leaves_the_default()
        {
            Read("{ \"sample\": { \"name\": \"" + new string('a', SettingProperty.LongestText + 1) + "\" } }").ShouldBeEmpty();
        }

        [Fact]
        public void Emoji_and_accents_come_back_as_typed_and_the_letters_can_be_read_in_the_file()
        {
            Set(_sample, "Name", "Café 🍇 für");

            var text = SettingsJson.Write(Sections, SettingHosts.Desktop);

            // The writer escapes emoji whatever encoder it is given, so only the letters are looked for.
            text.ShouldContain("Café");
            text.ShouldContain("für");
            ValueOf(SettingsJson.Read(text, [new SampleSettings()], SettingHosts.Desktop), "Name").ShouldBe("Café 🍇 für");
        }

        [Fact]
        public void Text_that_holds_half_of_an_emoji_is_read_without_throwing()
        {
            var escaped = Read("{ \"sample\": { \"name\": \"a\\uD83Cb\", \"size\": 20 } }");
            var raw = Read("{ \"sample\": { \"name\": \"a\uD83Cb\", \"size\": 20 } }");

            escaped.ShouldHaveSingleItem().Setting.Name.ShouldBe("Size");
            raw.ShouldBeEmpty();
        }

        [Fact]
        public void A_hidden_setting_is_kept_like_any_other()
        {
            Set(_sample, "Remembered", 7);

            ValueOf(Read(SettingsJson.Write(Sections, SettingHosts.Desktop)), "Remembered").ShouldBe(7);
        }
    }
}
