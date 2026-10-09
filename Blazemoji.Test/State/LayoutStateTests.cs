using Blazemoji.Services.Layout;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Layout;
using Blazemoji.Shared.State;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.State
{
    public class LayoutStateTests
    {
        private static readonly WorkspaceLayout Kept = new(0.3, 0.5, true);

        private readonly ILayoutStore _store = Substitute.For<ILayoutStore>();
        private readonly RecordingLogger<LayoutState> _logger = new();
        private int _announced;

        private LayoutState CreateState(bool withStore = true)
        {
            var state = new LayoutState(_logger, withStore ? _store : null);
            state.StateChanged += () => _announced++;
            return state;
        }

        private static IOException DiskFull() => new("No space left on device");

        [Fact]
        public void Before_anything_is_loaded_the_layout_is_the_default()
        {
            CreateState().Current.ShouldBe(WorkspaceLayout.Default);
        }

        [Fact]
        public async Task Loading_takes_the_layout_that_was_kept_and_says_so_once()
        {
            _store.LoadAsync().Returns(Kept);
            var state = CreateState();

            await state.LoadAsync();

            state.Current.ShouldBe(Kept);
            _announced.ShouldBe(1);
        }

        [Fact]
        public async Task Loading_with_nothing_kept_leaves_the_default_and_announces_nothing()
        {
            _store.LoadAsync().Returns((WorkspaceLayout?)null);
            var state = CreateState();

            await state.LoadAsync();

            state.Current.ShouldBe(WorkspaceLayout.Default);
            _announced.ShouldBe(0);
        }

        [Fact]
        public async Task What_is_loaded_is_mended_whatever_the_store_hands_back()
        {
            _store.LoadAsync().Returns(new WorkspaceLayout(4, double.NaN, false));
            var state = CreateState();

            await state.LoadAsync();

            state.Current.ShouldBe(new WorkspaceLayout(WorkspaceLayout.LargestShare, WorkspaceLayout.Default.EditorShare, false));
        }

        [Fact]
        public async Task A_store_that_cannot_be_read_leaves_the_default_and_is_logged_not_thrown()
        {
            _store.LoadAsync().ThrowsAsync(new ProjectStoreException("The browser's storage could not be used.", DiskFull()));
            var state = CreateState();

            await state.LoadAsync();

            state.Current.ShouldBe(WorkspaceLayout.Default);
            _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task A_kept_layout_that_arrives_after_a_divider_was_moved_keeps_the_move_and_the_rest_of_what_was_kept()
        {
            var answer = new TaskCompletionSource<WorkspaceLayout?>();
            _store.LoadAsync().Returns(answer.Task);
            var state = CreateState();
            var loading = state.LoadAsync();

            await state.ResizeSidebarAsync(0.4);
            answer.SetResult(Kept);
            await loading;

            // The move is newer than what was kept. The editor's share and the hidden sidebar
            // were not touched, so they are still what was kept.
            var joined = Kept with { SidebarShare = 0.4 };
            state.Current.ShouldBe(joined);
            await _store.Received(1).SaveAsync(joined);
        }

        [Fact]
        public async Task Nothing_is_kept_while_the_kept_layout_is_still_on_its_way_so_it_cannot_be_written_over_half_known()
        {
            var answer = new TaskCompletionSource<WorkspaceLayout?>();
            _store.LoadAsync().Returns(answer.Task);
            var state = CreateState();
            var loading = state.LoadAsync();

            await state.ToggleSidebarAsync();

            state.Current.SidebarHidden.ShouldBeTrue();
            await _store.DidNotReceive().SaveAsync(Arg.Any<WorkspaceLayout>());
            answer.SetResult(null);
            await loading;
            await _store.Received(1).SaveAsync(WorkspaceLayout.Default with { SidebarHidden = true });
        }

        [Fact]
        public async Task Changes_made_while_a_store_fails_to_load_are_kept_once_it_has_failed()
        {
            var answer = new TaskCompletionSource<WorkspaceLayout?>();
            _store.LoadAsync().Returns(answer.Task);
            var state = CreateState();
            var loading = state.LoadAsync();

            await state.ResizeEditorAsync(0.5);
            answer.SetException(new ProjectStoreException("The browser's storage could not be used.", DiskFull()));
            await loading;

            state.Current.ShouldBe(WorkspaceLayout.Default with { EditorShare = 0.5 });
            await _store.Received(1).SaveAsync(WorkspaceLayout.Default with { EditorShare = 0.5 });
        }

        [Fact]
        public async Task A_change_after_the_kept_layout_has_arrived_is_kept_straight_away()
        {
            _store.LoadAsync().Returns(Kept);
            var state = CreateState();
            await state.LoadAsync();

            await state.ResizeSidebarAsync(0.4);

            await _store.Received(1).SaveAsync(Kept with { SidebarShare = 0.4 });
        }

        [Fact]
        public async Task Asking_for_the_kept_layout_again_while_it_is_on_its_way_reads_the_store_once()
        {
            var answer = new TaskCompletionSource<WorkspaceLayout?>();
            _store.LoadAsync().Returns(answer.Task);
            var state = CreateState();

            var first = state.LoadAsync();
            var second = state.LoadAsync();
            answer.SetResult(Kept);
            await first;
            await second;

            await _store.Received(1).LoadAsync();
            state.Current.ShouldBe(Kept);
        }

        [Fact]
        public async Task Resizing_the_sidebar_changes_only_its_share_announces_it_and_keeps_the_whole_layout()
        {
            var state = CreateState();

            await state.ResizeSidebarAsync(0.4);

            var expected = WorkspaceLayout.Default with { SidebarShare = 0.4 };
            state.Current.ShouldBe(expected);
            _announced.ShouldBe(1);
            await _store.Received(1).SaveAsync(expected);
        }

        [Fact]
        public async Task Resizing_the_editor_changes_only_its_share_announces_it_and_keeps_the_whole_layout()
        {
            var state = CreateState();

            await state.ResizeEditorAsync(0.5);

            var expected = WorkspaceLayout.Default with { EditorShare = 0.5 };
            state.Current.ShouldBe(expected);
            _announced.ShouldBe(1);
            await _store.Received(1).SaveAsync(expected);
        }

        [Fact]
        public async Task A_share_that_would_leave_a_panel_no_room_is_mended_before_it_is_kept()
        {
            var state = CreateState();

            await state.ResizeSidebarAsync(1.5);
            await state.ResizeEditorAsync(double.NaN);

            state.Current.SidebarShare.ShouldBe(WorkspaceLayout.LargestShare);
            state.Current.EditorShare.ShouldBe(WorkspaceLayout.Default.EditorShare);
        }

        [Fact]
        public async Task Resizing_to_where_it_already_is_announces_nothing_and_keeps_nothing()
        {
            var state = CreateState();

            await state.ResizeSidebarAsync(WorkspaceLayout.Default.SidebarShare);
            await state.ResizeEditorAsync(WorkspaceLayout.Default.EditorShare);

            _announced.ShouldBe(0);
            await _store.DidNotReceive().SaveAsync(Arg.Any<WorkspaceLayout>());
        }

        [Fact]
        public async Task Toggling_the_sidebar_hides_it_and_toggling_again_shows_it_keeping_each()
        {
            var state = CreateState();

            await state.ToggleSidebarAsync();
            state.Current.SidebarHidden.ShouldBeTrue();
            await state.ToggleSidebarAsync();
            state.Current.SidebarHidden.ShouldBeFalse();

            _announced.ShouldBe(2);
            await _store.Received(1).SaveAsync(WorkspaceLayout.Default with { SidebarHidden = true });
            await _store.Received(1).SaveAsync(WorkspaceLayout.Default);
        }

        [Fact]
        public async Task A_store_that_cannot_keep_the_layout_does_not_stop_the_layout_changing()
        {
            _store.SaveAsync(Arg.Any<WorkspaceLayout>()).ThrowsAsync(new ProjectStoreException("The projects folder could not be used.", DiskFull()));
            var state = CreateState();

            await state.ResizeSidebarAsync(0.4);

            state.Current.SidebarShare.ShouldBe(0.4);
            _announced.ShouldBe(1);
            _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task Without_a_store_the_layout_still_changes_for_as_long_as_the_page_lasts()
        {
            var state = CreateState(withStore: false);

            await state.LoadAsync();
            await state.ResizeSidebarAsync(0.4);
            await state.ToggleSidebarAsync();

            state.Current.ShouldBe(new WorkspaceLayout(0.4, WorkspaceLayout.Default.EditorShare, true));
            _announced.ShouldBe(2);
        }
    }
}
