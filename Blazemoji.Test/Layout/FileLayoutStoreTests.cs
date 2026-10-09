using Blazemoji.Services.Layout;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Layout;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Layout
{
    public sealed class FileLayoutStoreTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "layout-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private FileLayoutStore Create(string? root = null) =>
            new(Options.Create(new FileProjectStoreOptions { Root = root ?? _root }), TimeProvider.System);

        private string LayoutFile => Path.Combine(_root, ".blazemoji", "layout.json");

        [Fact]
        public async Task With_nothing_kept_there_is_no_layout_and_no_folder_is_made_by_looking()
        {
            (await Create().LoadAsync()).ShouldBeNull();
            Directory.Exists(_root).ShouldBeFalse();
        }

        [Fact]
        public async Task A_layout_that_was_kept_is_read_back_by_another_store_on_the_same_folder()
        {
            var layout = new WorkspaceLayout(0.31, 0.55, true);

            await Create().SaveAsync(layout);

            (await Create().LoadAsync()).ShouldBe(layout);
        }

        [Fact]
        public async Task It_is_kept_as_one_small_file_in_the_apps_own_folder_that_a_person_can_read()
        {
            await Create().SaveAsync(new WorkspaceLayout(0.31, 0.55, true));

            var text = await File.ReadAllTextAsync(LayoutFile);
            text.ShouldContain("\"sidebarShare\": 0.31");
            text.ShouldContain("\"editorShare\": 0.55");
            text.ShouldContain("\"sidebarHidden\": true");
            Directory.GetFiles(Path.GetDirectoryName(LayoutFile)!).ShouldBe([LayoutFile]);
        }

        [Fact]
        public async Task A_later_layout_takes_the_place_of_the_earlier_one()
        {
            var store = Create();
            await store.SaveAsync(new WorkspaceLayout(0.31, 0.55, true));

            await store.SaveAsync(new WorkspaceLayout(0.2, 0.7, false));

            (await Create().LoadAsync()).ShouldBe(new WorkspaceLayout(0.2, 0.7, false));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json at all")]
        [InlineData("[1, 2, 3]")]
        [InlineData("null")]
        [InlineData("{ \"sidebarShare\": \"wide\" }")]
        public async Task A_file_that_is_not_a_layout_counts_as_no_layout(string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutFile)!);
            await File.WriteAllTextAsync(LayoutFile, text);

            (await Create().LoadAsync()).ShouldBeNull();
        }

        [Fact]
        public async Task A_file_with_only_some_of_it_is_filled_in_from_the_default()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutFile)!);
            await File.WriteAllTextAsync(LayoutFile, "{ \"sidebarHidden\": true }");

            (await Create().LoadAsync()).ShouldBe(WorkspaceLayout.Default with { SidebarHidden = true });
        }

        [Fact]
        public async Task What_is_read_is_mended_so_that_a_hand_edited_file_cannot_leave_a_panel_no_room()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutFile)!);
            await File.WriteAllTextAsync(LayoutFile, "{ \"sidebarShare\": 7, \"editorShare\": -2, \"sidebarHidden\": false }");

            (await Create().LoadAsync()).ShouldBe(new WorkspaceLayout(WorkspaceLayout.LargestShare, WorkspaceLayout.SmallestShare, false));
        }

        [Fact]
        public async Task A_folder_that_cannot_be_written_to_is_reported_as_the_store_failing()
        {
            // A file where the projects folder should be: nothing can be made under it.
            Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
            await File.WriteAllTextAsync(_root, "in the way");
            try
            {
                await Should.ThrowAsync<ProjectStoreException>(() => Create().SaveAsync(WorkspaceLayout.Default));
            }
            finally
            {
                File.Delete(_root);
            }
        }
    }
}
