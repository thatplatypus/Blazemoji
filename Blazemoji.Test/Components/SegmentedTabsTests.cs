using Blazemoji.Components.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Interop;
using MudBlazor.Services;
using NSubstitute;

namespace Blazemoji.Test.Components
{
    public sealed class SegmentedTabsTests : BunitContext
    {
        private readonly IResizeObserver _resizeObserver = Substitute.For<IResizeObserver>();

        public SegmentedTabsTests()
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            Services.AddTransient(_ => _resizeObserver);
        }

        /// <summary>A strip with three tabs. The middle one has a count.</summary>
        private IRenderedComponent<SegmentedTabs> RenderThree(
            string? active = null,
            int? problems = 0,
            Action<string>? activeChanged = null,
            bool withTheLast = true,
            int compactBelow = 0)
        {
            return Render<SegmentedTabs>(parameters =>
            {
                parameters.Add(tabs => tabs.Active, active);
                parameters.Add(tabs => tabs.AriaLabel, "What the program did");
                parameters.Add(tabs => tabs.CompactBelow, compactBelow);
                if (activeChanged is not null)
                    parameters.Add(tabs => tabs.ActiveChanged, activeChanged);

                parameters.AddChildContent<SegmentedTab>(tab => tab
                    .Add(t => t.Id, "output").Add(t => t.Text, "Output").AddChildContent("<p>the output</p>"));
                parameters.AddChildContent<SegmentedTab>(tab => tab
                    .Add(t => t.Id, "problems").Add(t => t.Text, "Problems").Add(t => t.Count, problems).Add(t => t.CountColor, Color.Error)
                    .AddChildContent("<p>the problems</p>"));
                if (withTheLast)
                {
                    parameters.AddChildContent<SegmentedTab>(tab => tab
                        .Add(t => t.Id, "requests").Add(t => t.Text, "Requests").Add(t => t.Icon, BlazemojiIcons.Send).AddChildContent("<p>the requests</p>"));
                }
            });
        }

        private static IReadOnlyList<string> Names(IRenderedComponent<SegmentedTabs> cut) =>
            cut.FindAll("[role=tab]").Select(tab => tab.TextContent.Trim()).ToList();

        private static string Selected(IRenderedComponent<SegmentedTabs> cut) =>
            cut.FindAll("[role=tab]").Single(tab => tab.GetAttribute("aria-selected") == "true").TextContent.Trim();

        [Fact]
        public void Each_tab_is_in_the_strip_in_the_order_written_and_the_first_is_shown()
        {
            var cut = RenderThree();

            Names(cut).ShouldBe(["Output", "Problems", "Requests"]);
            Selected(cut).ShouldBe("Output");
            cut.Find("[role=tabpanel]").InnerHtml.ShouldContain("the output");
            cut.Markup.ShouldNotContain("the problems");
        }

        [Fact]
        public void The_strip_the_tabs_and_the_panel_say_what_they_are_to_a_screen_reader()
        {
            var cut = RenderThree();

            cut.Find("[role=tablist]").GetAttribute("aria-label").ShouldBe("What the program did");
            var tabs = cut.FindAll("[role=tab]");
            var panel = cut.Find("[role=tabpanel]");
            tabs.Select(tab => tab.GetAttribute("aria-selected")).ShouldBe(["true", "false", "false"]);
            tabs.Select(tab => tab.GetAttribute("tabindex")).ShouldBe(["0", "-1", "-1"]);
            tabs[0].GetAttribute("aria-controls").ShouldBe(panel.Id);
            panel.GetAttribute("aria-labelledby").ShouldBe(tabs[0].Id);
            tabs.Select(tab => tab.Id).ShouldBeUnique();
        }

        [Fact]
        public async Task Clicking_a_tab_shows_it_and_tells_whoever_is_bound_to_it()
        {
            var told = new List<string>();
            var cut = RenderThree(activeChanged: told.Add);

            await cut.FindAll("[role=tab]")[1].ClickAsync();

            Selected(cut).ShouldBe("Problems");
            cut.Find("[role=tabpanel]").InnerHtml.ShouldContain("the problems");
            cut.Markup.ShouldNotContain("the output");
            told.ShouldBe(["problems"]);
        }

        [Fact]
        public void The_tab_named_from_outside_is_the_one_shown()
        {
            var cut = RenderThree(active: "requests");

            Selected(cut).ShouldBe("Requests");
            cut.Find("[role=tabpanel]").InnerHtml.ShouldContain("the requests");
        }

        [Fact]
        public void Naming_another_tab_from_outside_switches_to_it()
        {
            var cut = RenderThree(active: "output");

            cut.Render(parameters => parameters.Add(tabs => tabs.Active, "problems"));

            Selected(cut).ShouldBe("Problems");
        }

        [Fact]
        public void A_name_that_no_tab_has_shows_the_first()
        {
            RenderThree(active: "nowhere").FindAll("[role=tab]")[0].GetAttribute("aria-selected").ShouldBe("true");
        }

        [Theory]
        [InlineData("output", "ArrowRight", "Problems")]
        [InlineData("requests", "ArrowRight", "Output")]
        [InlineData("output", "ArrowLeft", "Requests")]
        [InlineData("requests", "ArrowLeft", "Problems")]
        [InlineData("requests", "Home", "Output")]
        [InlineData("output", "End", "Requests")]
        public async Task The_arrow_home_and_end_keys_move_between_tabs(string from, string key, string to)
        {
            var told = new List<string>();
            var cut = RenderThree(active: from, activeChanged: told.Add);

            await cut.FindAll("[role=tab]").Single(tab => tab.GetAttribute("aria-selected") == "true").KeyDownAsync(new KeyboardEventArgs { Key = key });

            Selected(cut).ShouldBe(to);
            told.ShouldHaveSingleItem();
        }

        [Fact]
        public async Task Any_other_key_changes_nothing()
        {
            var told = new List<string>();
            var cut = RenderThree(activeChanged: told.Add);

            await cut.FindAll("[role=tab]")[0].KeyDownAsync(new KeyboardEventArgs { Key = "a" });

            Selected(cut).ShouldBe("Output");
            told.ShouldBeEmpty();
        }

        [Fact]
        public void A_count_is_shown_on_its_tab()
        {
            var cut = RenderThree(problems: 3);

            cut.FindAll("[role=tab]")[1].QuerySelector(".mud-badge")!.TextContent.Trim().ShouldBe("3");
        }

        [Fact]
        public void A_count_of_nothing_shows_no_number_and_takes_no_room_from_the_name()
        {
            var none = RenderThree(problems: 0);
            var some = RenderThree(problems: 2);

            none.FindAll("[role=tab]")[1].QuerySelector(".mud-badge").ShouldBeNull();
            none.FindAll("[role=tab]")[1].QuerySelector(".segmented-tab-count").ShouldBeNull();
            some.FindAll("[role=tab]")[1].QuerySelector(".segmented-tab-count").ShouldNotBeNull();
        }

        [Theory]
        [InlineData(3, "3")]
        [InlineData(42, "42")]
        [InlineData(250, "99+")]
        public void A_count_takes_the_room_its_number_needs_and_no_more(int problems, string written)
        {
            var count = RenderThree(problems: problems).FindAll("[role=tab]")[1].QuerySelector(".segmented-tab-count")!;

            count.QuerySelector(".segmented-tab-count-place")!.TextContent.ShouldBe(written);
            count.QuerySelector(".mud-badge")!.TextContent.Trim().ShouldBe(written);
        }

        [Fact]
        public void A_tab_is_still_called_by_its_name_alone_when_it_has_a_count()
        {
            RenderThree(problems: 12).FindAll("[role=tab]")[1].QuerySelector(".segmented-tab-text")!.TextContent.ShouldBe("Problems");
        }

        [Fact]
        public void A_tab_that_never_counts_anything_has_no_place_for_a_count()
        {
            RenderThree().FindAll("[role=tab]")[0].QuerySelector(".segmented-tab-count").ShouldBeNull();
        }

        [Fact]
        public void A_count_that_changes_is_shown_at_once()
        {
            var problems = 1;
            var cut = Render<CountingHost>(parameters => parameters.Add(host => host.Problems, problems));
            cut.Find(".mud-badge").TextContent.Trim().ShouldBe("1");

            cut.Render(parameters => parameters.Add(host => host.Problems, 4));

            cut.Find(".mud-badge").TextContent.Trim().ShouldBe("4");
        }

        [Fact]
        public void An_icon_is_drawn_before_the_tabs_name()
        {
            var requests = RenderThree().FindAll("[role=tab]")[2];

            requests.QuerySelector("svg").ShouldNotBeNull();
            requests.TextContent.Trim().ShouldBe("Requests");
        }

        [Fact]
        public void A_tab_that_goes_away_leaves_the_strip_and_the_first_tab_is_shown_if_it_was_the_one_open()
        {
            var cut = Render<CountingHost>(parameters => parameters.Add(host => host.Problems, 0).Add(host => host.Active, "second"));
            cut.FindAll("[role=tab]").Count.ShouldBe(2);

            cut.Render(parameters => parameters.Add(host => host.WithoutTheSecond, true));

            cut.FindAll("[role=tab]").Select(tab => tab.TextContent.Trim()).ShouldBe(["First"]);
            cut.FindAll("[role=tab]")[0].GetAttribute("aria-selected").ShouldBe("true");
        }

        [Fact]
        public void A_strip_with_room_shows_every_tab_and_no_menu()
        {
            _resizeObserver.Observe(Arg.Any<ElementReference>()).Returns(new BoundingClientRect { Width = 420 });

            var cut = RenderThree(compactBelow: 300);

            cut.WaitForAssertion(() => Names(cut).ShouldBe(["Output", "Problems", "Requests"]));
            cut.FindAll("[data-testid=more-tabs]").ShouldBeEmpty();
        }

        [Fact]
        public void A_strip_too_narrow_for_its_tabs_shows_the_open_one_and_a_menu_of_them_all()
        {
            _resizeObserver.Observe(Arg.Any<ElementReference>()).Returns(new BoundingClientRect { Width = 180 });

            var cut = RenderThree(active: "problems", compactBelow: 300);

            cut.WaitForAssertion(() => Names(cut).ShouldBe(["Problems"]));
            cut.Find("[role=tab]").GetAttribute("aria-selected").ShouldBe("true");
            cut.Find("[data-testid=more-tabs]").ShouldNotBeNull();
            cut.Find("[role=tabpanel]").InnerHtml.ShouldContain("the problems");
        }

        [Fact]
        public void A_narrow_strip_that_is_hidden_is_still_the_narrow_one_when_it_is_shown_again()
        {
            var strip = default(ElementReference);
            _resizeObserver.Observe(Arg.Do<ElementReference>(element => strip = element)).Returns(new BoundingClientRect { Width = 180 });
            var cut = RenderThree(compactBelow: 300);
            cut.WaitForAssertion(() => Names(cut).ShouldBe(["Output"]));

            // Whatever it is in has been hidden, and something hidden has no width at all.
            // That says nothing about how wide it will be when it is back.
            _resizeObserver.OnResized += Raise.Event<SizeChanged>(new Dictionary<ElementReference, BoundingClientRect> { [strip] = new BoundingClientRect { Width = 0 } });
            cut.Render();

            Names(cut).ShouldBe(["Output"]);
            cut.Find("[data-testid=more-tabs]").ShouldNotBeNull();
        }

        [Fact]
        public void A_strip_that_is_never_to_collapse_does_not_watch_its_width()
        {
            RenderThree(compactBelow: 0);

            _resizeObserver.DidNotReceive().Observe(Arg.Any<ElementReference>());
        }

        [Fact]
        public void A_tab_outside_a_strip_says_so()
        {
            Should.Throw<InvalidOperationException>(() =>
                Render<SegmentedTab>(parameters => parameters.Add(tab => tab.Id, "lost").Add(tab => tab.Text, "Lost")));
        }

        /// <summary>A parent whose state decides a tab's count and whether a tab is there at all.</summary>
        private sealed class CountingHost : ComponentBase
        {
            [Parameter]
            public int Problems { get; set; }

            [Parameter]
            public bool WithoutTheSecond { get; set; }

            [Parameter]
            public string? Active { get; set; }

            protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
            {
                builder.OpenComponent<SegmentedTabs>(0);
                builder.AddComponentParameter(1, nameof(SegmentedTabs.Active), Active);
                builder.AddComponentParameter(2, nameof(SegmentedTabs.ChildContent), (RenderFragment)(inner =>
                {
                    inner.OpenComponent<SegmentedTab>(0);
                    inner.AddComponentParameter(1, nameof(SegmentedTab.Id), "first");
                    inner.AddComponentParameter(2, nameof(SegmentedTab.Text), "First");
                    inner.AddComponentParameter(3, nameof(SegmentedTab.Count), (int?)Problems);
                    inner.AddComponentParameter(4, nameof(SegmentedTab.ChildContent), (RenderFragment)(content => content.AddContent(0, "one")));
                    inner.CloseComponent();

                    if (!WithoutTheSecond)
                    {
                        inner.OpenComponent<SegmentedTab>(10);
                        inner.AddComponentParameter(11, nameof(SegmentedTab.Id), "second");
                        inner.AddComponentParameter(12, nameof(SegmentedTab.Text), "Second");
                        inner.AddComponentParameter(13, nameof(SegmentedTab.ChildContent), (RenderFragment)(content => content.AddContent(0, "two")));
                        inner.CloseComponent();
                    }
                }));
                builder.CloseComponent();
            }
        }
    }
}
