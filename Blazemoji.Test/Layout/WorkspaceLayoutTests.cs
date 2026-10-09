using Blazemoji.Shared.Models.Layout;

namespace Blazemoji.Test.Layout
{
    public class WorkspaceLayoutTests
    {
        [Fact]
        public void The_default_gives_the_sidebar_a_quarter_and_the_editor_most_of_the_height_and_shows_the_sidebar()
        {
            WorkspaceLayout.Default.SidebarShare.ShouldBe(0.25);
            WorkspaceLayout.Default.EditorShare.ShouldBeInRange(0.6, 0.75);
            WorkspaceLayout.Default.SidebarHidden.ShouldBeFalse();
        }

        [Fact]
        public void A_layout_that_makes_sense_is_left_as_it_is()
        {
            var layout = new WorkspaceLayout(0.31, 0.5, true);

            layout.Mended().ShouldBe(layout);
        }

        [Theory]
        [InlineData(-1.0, WorkspaceLayout.SmallestShare)]
        [InlineData(0.0, WorkspaceLayout.SmallestShare)]
        [InlineData(1.0, WorkspaceLayout.LargestShare)]
        [InlineData(250.0, WorkspaceLayout.LargestShare)]
        public void A_share_that_would_leave_a_panel_no_room_is_brought_back_inside(double kept, double mended)
        {
            var layout = new WorkspaceLayout(kept, kept, false).Mended();

            layout.SidebarShare.ShouldBe(mended);
            layout.EditorShare.ShouldBe(mended);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void A_share_that_is_not_a_number_becomes_the_default(double kept)
        {
            var layout = new WorkspaceLayout(kept, kept, true).Mended();

            layout.SidebarShare.ShouldBe(WorkspaceLayout.Default.SidebarShare);
            layout.EditorShare.ShouldBe(WorkspaceLayout.Default.EditorShare);
            layout.SidebarHidden.ShouldBeTrue();
        }
    }
}
