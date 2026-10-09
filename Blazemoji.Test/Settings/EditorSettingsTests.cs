using Blazemoji.Services.Settings;
using Blazemoji.Settings;
using Blazemoji.Shared.State;
using BlazorMonaco.Editor;
using Microsoft.Extensions.DependencyInjection;

namespace Blazemoji.Test.Settings
{
    public class EditorSettingsTests
    {
        [Fact]
        public void The_defaults_are_what_the_editor_does_today_with_text_at_fourteen()
        {
            var settings = new EditorSettings();

            settings.FontSize.ShouldBe(14);
            settings.WordWrap.ShouldBeFalse();
            settings.Minimap.ShouldBeTrue();
            settings.LineNumbers.ShouldBeTrue();
            settings.RenderWhitespace.ShouldBe("selection");
        }

        [Fact]
        public void Every_setting_has_a_label_and_a_group_and_they_come_in_the_panels_order()
        {
            var settings = SettingProperties.Of(typeof(EditorSettings));

            settings.Select(setting => setting.Name).ShouldBe(["FontSize", "WordWrap", "Minimap", "LineNumbers", "RenderWhitespace"]);
            settings.ShouldAllBe(setting => setting.Attribute.Label.Length > 0 && !string.IsNullOrEmpty(setting.Attribute.Group));
            settings.Single(setting => setting.Name == "RenderWhitespace").Options.ShouldBe(["none", "boundary", "selection", "trailing", "all"]);
        }

        [Fact]
        public void The_values_are_put_as_monaco_wants_them()
        {
            var values = EditorOptionValues.From(new EditorSettings());

            values.ShouldBe(new EditorOptionValues(14, "off", true, "on", "selection"));
        }

        [Fact]
        public void They_are_handed_to_monaco_under_its_own_names()
        {
            var options = new EditorOptionValues(18, "on", false, "off", "all").ToUpdateOptions();

            options.FontSize.ShouldBe(18);
            options.WordWrap.ShouldBe("on");
            options.Minimap.Enabled.ShouldBe(false);
            options.LineNumbers.ShouldBe("off");
            options.RenderWhitespace.ShouldBe("all");
        }

        [Fact]
        public void The_editors_own_call_adds_the_section()
        {
            var services = new ServiceCollection();
            services.AddBlazemojiEditor();
            using var provider = services.BuildServiceProvider(validateScopes: true);
            using var scope = provider.CreateScope();

            var state = scope.ServiceProvider.GetRequiredService<SettingsState>();

            state.Sections.ShouldHaveSingleItem().ShouldBeOfType<EditorSettings>();
            state.Get<EditorSettings>().FontSize.ShouldBe(14);
        }
    }
}
