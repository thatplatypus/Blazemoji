using Blazemoji.Layout;
using Blazemoji.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;

namespace Blazemoji.Test.Components
{
    public sealed class AppShellTests : BunitContext
    {
        public AppShellTests()
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        }

        private IRenderedComponent<AppShell> RenderAroundSomething() =>
            Render<AppShell>(parameters => parameters.AddChildContent<SaysWhetherItIsDark>());

        private static string[] LinkedTo(IRenderedComponent<AppShell> cut) =>
            cut.FindAll("a[href]").Select(link => link.GetAttribute("href")!).ToArray();

        [Fact]
        public void What_is_given_to_the_shell_is_shown_with_the_title()
        {
            var cut = RenderAroundSomething();

            cut.Markup.ShouldContain("🔥 Blazemoji");
            cut.Find("[data-testid=inside]").ShouldNotBeNull();
        }

        [Fact]
        public void In_a_browser_the_links_are_ordinary_links_that_open_in_a_tab_of_their_own()
        {
            var cut = RenderAroundSomething();

            LinkedTo(cut).ShouldBe([BlazemojiLinks.EmojicodeDocumentation, BlazemojiLinks.Repository]);
            cut.FindAll("a[href]").ShouldAllBe(link => link.GetAttribute("target") == "_blank");
        }

        [Fact]
        public async Task A_host_that_opens_links_itself_is_asked_to_and_the_page_goes_nowhere()
        {
            var links = Substitute.For<IExternalLinks>();
            Services.AddSingleton(links);
            var cut = RenderAroundSomething();

            LinkedTo(cut).ShouldBeEmpty();
            await cut.Find("[data-testid=documentation-link]").ClickAsync();
            await cut.Find("[data-testid=repository-link]").ClickAsync();

            Received.InOrder(() =>
            {
                links.OpenAsync(BlazemojiLinks.EmojicodeDocumentation);
                links.OpenAsync(BlazemojiLinks.Repository);
            });
        }

        [Fact]
        public async Task The_toggle_turns_the_page_dark_and_light_again_and_tells_what_is_inside()
        {
            var cut = RenderAroundSomething();
            cut.Find("[data-testid=inside]").TextContent.ShouldBe("light");

            await cut.Find("[data-testid=dark-mode-toggle]").ClickAsync();
            cut.Find("[data-testid=inside]").TextContent.ShouldBe("dark");

            await cut.Find("[data-testid=dark-mode-toggle]").ClickAsync();
            cut.Find("[data-testid=inside]").TextContent.ShouldBe("light");
        }

        private sealed class SaysWhetherItIsDark : ComponentBase
        {
            [CascadingParameter(Name = "DarkMode")]
            public bool DarkMode { get; set; }

            protected override void BuildRenderTree(RenderTreeBuilder builder)
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "data-testid", "inside");
                builder.AddContent(2, DarkMode ? "dark" : "light");
                builder.CloseElement();
            }
        }
    }
}
