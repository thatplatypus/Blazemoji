using System.Globalization;
using Blazemoji.Components.Shared;
using Blazemoji.Interop;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Blazemoji.Test.Components
{
    public sealed class SplitViewTests : BunitContext
    {
        private const string Script = "./_content/Blazemoji.Components/js/splitView.js";

        private readonly BunitJSModuleInterop _script;

        public SplitViewTests()
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            _script = JSInterop.SetupModule(Script);
            _script.Mode = JSRuntimeMode.Loose;
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            Services.AddScoped<SplitViewInterop>();
        }

        private IRenderedComponent<SplitView> RenderSplit(
            double share = 0.25,
            bool stacked = false,
            bool firstHidden = false,
            double defaultShare = 0.5,
            Action<double>? shareChanged = null,
            string? dividerLabel = null,
            int minPanelSize = 120)
        {
            return Render<SplitView>(parameters =>
            {
                parameters.Add(view => view.Share, share);
                parameters.Add(view => view.DefaultShare, defaultShare);
                parameters.Add(view => view.DividerLabel, dividerLabel);
                parameters.Add(view => view.MinPanelSize, minPanelSize);
                parameters.Add(view => view.Stacked, stacked);
                parameters.Add(view => view.FirstHidden, firstHidden);
                parameters.Add(view => view.First, "<p id=\"one\">the files</p>");
                parameters.Add(view => view.Second, "<p id=\"two\">the editor</p>");
                parameters.AddUnmatched("data-testid", "columns");
                if (shareChanged is not null)
                    parameters.Add(view => view.ShareChanged, shareChanged);
            });
        }

        /// <summary>The share as the style sheet is handed it, in the custom property it reads.</summary>
        private static string ShareInStyle(IRenderedComponent<SplitView> cut) =>
            System.Text.RegularExpressions.Regex.Match(cut.Find(".split-view").GetAttribute("style") ?? string.Empty, @"--split-share:\s*([^;]+)").Groups[1].Value.Trim();

        [Fact]
        public void Both_parts_are_drawn_the_first_before_the_second()
        {
            var cut = RenderSplit();

            cut.Find(".split-view-first #one").TextContent.ShouldBe("the files");
            cut.Find(".split-view-second #two").TextContent.ShouldBe("the editor");
            cut.Markup.IndexOf("the files", StringComparison.Ordinal).ShouldBeLessThan(cut.Markup.IndexOf("the editor", StringComparison.Ordinal));
        }

        [Fact]
        public void What_the_caller_adds_lands_on_the_element_around_both_parts()
        {
            var cut = RenderSplit();

            cut.Find("[data-testid=columns]").ClassList.ShouldContain("split-view");
        }

        [Fact]
        public void The_share_reaches_the_style_sheet_as_a_number_it_can_read_whatever_the_language()
        {
            var before = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            try
            {
                var cut = RenderSplit(share: 0.3125);

                ShareInStyle(cut).ShouldBe("0.3125");
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Side_by_side_or_one_above_the_other_as_asked(bool stacked)
        {
            var cut = RenderSplit(stacked: stacked);

            cut.FindComponent<MudSplitPanel>().Instance.Horizontal.ShouldBe(stacked);
            cut.Find(".split-view").ClassList.Contains("stacked").ShouldBe(stacked);
        }

        [Fact]
        public void A_double_click_is_left_to_this_component_so_that_it_goes_to_the_default_and_not_to_the_middle()
        {
            var cut = RenderSplit();

            cut.FindComponent<MudSplitPanel>().Instance.ResetOnDoubleClick.ShouldBeFalse();
        }

        [Fact]
        public void A_hidden_first_part_is_marked_as_hidden_and_stays_on_the_page()
        {
            var cut = RenderSplit(firstHidden: true);

            cut.Find(".split-view").ClassList.ShouldContain("first-hidden");
            cut.Find(".split-view-first #one").TextContent.ShouldBe("the files");
        }

        [Fact]
        public void A_shown_first_part_is_not_marked_as_hidden()
        {
            RenderSplit().Find(".split-view").ClassList.ShouldNotContain("first-hidden");
        }

        [Fact]
        public void After_it_is_drawn_the_script_is_told_to_watch_its_divider_once()
        {
            var cut = RenderSplit();
            cut.Render();

            _script.Invocations.Count(call => call.Identifier == "attach").ShouldBe(1);
        }

        [Fact]
        public void The_script_is_given_where_a_double_click_goes_and_what_the_divider_is_called()
        {
            RenderSplit(defaultShare: 0.3, dividerLabel: "Resize the sidebar");

            var attach = _script.Invocations.Single(call => call.Identifier == "attach");
            attach.Arguments[2].ShouldBe(0.3);
            attach.Arguments[3].ShouldBe("Resize the sidebar");
        }

        [Fact]
        public void The_smallest_a_part_may_be_reaches_the_style_sheet_as_well_as_the_panel()
        {
            var cut = RenderSplit(minPanelSize: 200);

            cut.Find(".split-view").GetAttribute("style").ShouldContain("--split-min:200px");
            cut.FindComponent<MudSplitPanel>().Instance.MinPanelSize.ShouldBe(200);
        }

        [Fact]
        public void New_content_from_the_caller_is_drawn_without_handing_the_panel_anything_new()
        {
            var cut = RenderSplit();
            var panel = cut.FindComponent<MudSplitPanel>().Instance;
            var handed = (panel.FirstPanel, panel.SecondPanel);

            cut.Render(parameters => parameters
                .Add(view => view.First, "<p id=\"one\">other files</p>")
                .Add(view => view.Second, "<p id=\"two\">another editor</p>"));

            cut.Find(".split-view-first #one").TextContent.ShouldBe("other files");
            cut.Find(".split-view-second #two").TextContent.ShouldBe("another editor");

            // The panel asks the page a question every time it is given parameters, and a
            // caller's content changes all the time. It is given new ones only when this
            // component's own element has to change.
            panel.FirstPanel.ShouldBeSameAs(handed.FirstPanel);
            panel.SecondPanel.ShouldBeSameAs(handed.SecondPanel);
        }

        [Fact]
        public void Hiding_and_showing_the_first_part_after_it_is_drawn_still_reaches_the_page()
        {
            var cut = RenderSplit();

            cut.Render(parameters => parameters.Add(view => view.FirstHidden, true));
            cut.Find(".split-view").ClassList.ShouldContain("first-hidden");

            cut.Render(parameters => parameters.Add(view => view.FirstHidden, false));
            cut.Find(".split-view").ClassList.ShouldNotContain("first-hidden");
        }

        [Fact]
        public async Task A_caller_handing_back_the_share_it_was_told_asks_nothing_more_of_the_script()
        {
            var cut = RenderSplit(share: 0.25);
            await cut.InvokeAsync(() => cut.Instance.DividerMovedAsync(0.4));

            cut.Render(parameters => parameters.Add(view => view.Share, 0.4));

            // The script has already put the divider there. Letting go of it now could undo a later drag.
            _script.Invocations.Count(call => call.Identifier == "release").ShouldBe(0);
        }

        [Fact]
        public async Task A_share_from_the_caller_that_the_script_did_not_report_takes_the_divider_back_from_the_script()
        {
            var cut = RenderSplit(share: 0.25);
            await cut.InvokeAsync(() => cut.Instance.DividerMovedAsync(0.99));

            // The caller keeps shares inside limits of its own, and hands back another number.
            cut.Render(parameters => parameters.Add(view => view.Share, 0.95));

            _script.Invocations.Count(call => call.Identifier == "release").ShouldBe(1);
            ShareInStyle(cut).ShouldBe("0.95");
        }

        [Fact]
        public void A_share_from_the_caller_when_nothing_was_dragged_takes_the_divider_too()
        {
            var cut = RenderSplit(share: 0.25);

            cut.Render(parameters => parameters.Add(view => view.Share, 0.4));

            _script.Invocations.Count(call => call.Identifier == "release").ShouldBe(1);
        }

        [Fact]
        public async Task A_divider_let_go_somewhere_new_tells_the_caller_the_share_and_takes_it()
        {
            var told = new List<double>();
            var cut = RenderSplit(share: 0.25, shareChanged: told.Add);

            await cut.InvokeAsync(() => cut.Instance.DividerMovedAsync(0.4));

            told.ShouldBe([0.4]);
            ShareInStyle(cut).ShouldBe("0.4");
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public async Task A_report_that_is_not_a_number_is_ignored(double reported)
        {
            var told = new List<double>();
            var cut = RenderSplit(share: 0.25, shareChanged: told.Add);

            await cut.InvokeAsync(() => cut.Instance.DividerMovedAsync(reported));

            told.ShouldBeEmpty();
            ShareInStyle(cut).ShouldBe("0.25");
        }

        [Fact]
        public async Task A_report_from_outside_what_a_share_can_be_is_brought_inside()
        {
            var told = new List<double>();
            var cut = RenderSplit(share: 0.25, shareChanged: told.Add);

            await cut.InvokeAsync(() => cut.Instance.DividerMovedAsync(1.7));

            told.ShouldBe([1.0]);
        }

        [Fact]
        public async Task A_double_click_on_the_divider_puts_it_back_at_the_default()
        {
            var told = new List<double>();
            var cut = RenderSplit(share: 0.6, defaultShare: 0.25, shareChanged: told.Add);

            await cut.InvokeAsync(() => cut.Instance.DividerResetAsync());

            told.ShouldBe([0.25]);
            ShareInStyle(cut).ShouldBe("0.25");
        }

        [Fact]
        public void A_new_share_from_the_caller_is_taken()
        {
            var cut = RenderSplit(share: 0.25);

            cut.Render(parameters => parameters.Add(view => view.Share, 0.5));

            ShareInStyle(cut).ShouldBe("0.5");
        }

        [Fact]
        public async Task When_it_goes_the_script_stops_watching_its_divider()
        {
            var cut = RenderSplit();

            await cut.Instance.DisposeAsync();

            _script.Invocations.Count(call => call.Identifier == "detach").ShouldBe(1);
        }
    }
}
