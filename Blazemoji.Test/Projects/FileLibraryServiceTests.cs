using Blazemoji.Services.Library;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Library;
using Blazemoji.Shared.Models.Projects;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Blazemoji.Test.Projects
{
    public sealed class FileLibraryServiceTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "library-" + Guid.NewGuid().ToString("N"));
        private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 7, 21, 30, 0, TimeSpan.Zero));

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private IOptions<FileProjectStoreOptions> Options(string? root = null) =>
            Microsoft.Extensions.Options.Options.Create(new FileProjectStoreOptions { Root = root ?? _root });

        private FileLibraryService Create(string? root = null) => new(Options(root), _clock);

        private string InRoot(params string[] parts) => Path.Combine([_root, .. parts]);

        [Fact]
        public async Task With_nothing_saved_there_is_nothing_listed_and_no_folder_is_made_by_looking()
        {
            (await Create().GetSavedAsync()).ShouldBeEmpty();
            Directory.Exists(_root).ShouldBeFalse();
        }

        [Fact]
        public async Task A_saved_file_is_a_file_of_that_name_among_the_snippets_and_is_listed_with_its_text()
        {
            await Create().SaveAsync(new EmojicFile { Name = "Loop.🍇", Code = "🏁 🍇 🔁 👍 🍇 🍉 🍉" });
            await Create().SaveAsync(new EmojicFile { Name = "Hello.🍇", Code = "🏁 🍇 🍉" });

            var saved = await Create().GetSavedAsync();

            File.ReadAllText(InRoot(FileLibraryService.Folder, "Loop.🍇")).ShouldBe("🏁 🍇 🔁 👍 🍇 🍉 🍉");
            saved.Select(file => file.Name).ShouldBe(["Hello.🍇", "Loop.🍇"]);
            saved[1].Code.ShouldBe("🏁 🍇 🔁 👍 🍇 🍉 🍉");
        }

        [Fact]
        public async Task Saving_under_a_name_again_replaces_the_file_and_keeps_the_earlier_one_in_the_trash()
        {
            var library = Create();
            await library.SaveAsync(new EmojicFile { Name = "Hello.🍇", Code = "first" });

            await library.SaveAsync(new EmojicFile { Name = "Hello.🍇", Code = "second" });

            (await library.GetSavedAsync()).Single().Code.ShouldBe("second");
            File.ReadAllText(InRoot(".blazemoji", "trash", "2026-10-07 213000 " + FileLibraryService.Folder, "Hello.🍇")).ShouldBe("first");
        }

        [Theory]
        [InlineData("a/b.🍇", "a-b.🍇")]
        [InlineData("../up.🍇", "-up.🍇")]
        [InlineData("..", "Untitled.🍇")]
        public async Task A_name_that_cannot_name_a_file_is_made_into_one_that_can_and_stays_among_the_snippets(string name, string onDisk)
        {
            await Create().SaveAsync(new EmojicFile { Name = name, Code = "🏁 🍇 🍉" });

            File.Exists(InRoot(FileLibraryService.Folder, onDisk)).ShouldBeTrue();
            Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length.ShouldBe(1);
        }

        [Fact]
        public async Task Clearing_moves_every_saved_file_to_the_trash_and_leaves_projects_alone()
        {
            var projects = new FileProjectStore(Options(), _clock);
            await projects.SaveAsync(new Project("abc123", "Hello", ProjectKind.Program, "main.🍇", [new ProjectFile("main.🍇", "🏁 🍇 🍉")]));
            var library = Create();
            await library.SaveAsync(new EmojicFile { Name = "One.🍇", Code = "1" });
            await library.SaveAsync(new EmojicFile { Name = "Two.🍇", Code = "2" });

            await library.ClearSavedAsync();

            (await library.GetSavedAsync()).ShouldBeEmpty();
            File.ReadAllText(InRoot(".blazemoji", "trash", "2026-10-07 213000 " + FileLibraryService.Folder, "One.🍇")).ShouldBe("1");
            File.ReadAllText(InRoot(".blazemoji", "trash", "2026-10-07 213000 " + FileLibraryService.Folder, "Two.🍇")).ShouldBe("2");
            (await new FileProjectStore(Options(), _clock).LoadAsync("abc123")).ShouldNotBeNull();
        }

        [Fact]
        public async Task Clearing_leaves_alone_what_was_never_listed_as_saved()
        {
            var library = Create();
            await library.SaveAsync(new EmojicFile { Name = "One.🍇", Code = "1" });
            File.WriteAllBytes(InRoot(FileLibraryService.Folder, "logo.png"), [0x89, 0x50, 0xFF, 0xFE, 0x00]);
            File.WriteAllText(InRoot(FileLibraryService.Folder, ".hidden"), "not listed");

            await library.ClearSavedAsync();

            File.Exists(InRoot(FileLibraryService.Folder, "logo.png")).ShouldBeTrue();
            File.Exists(InRoot(FileLibraryService.Folder, ".hidden")).ShouldBeTrue();
            File.Exists(InRoot(FileLibraryService.Folder, "One.🍇")).ShouldBeFalse();
        }

        [Fact]
        public async Task The_folder_of_snippets_is_not_taken_for_a_project()
        {
            await Create().SaveAsync(new EmojicFile { Name = "One.🍇", Code = "1" });

            (await new FileProjectStore(Options(), _clock).ListAsync()).ShouldBeEmpty();
        }

        [Fact]
        public async Task What_is_among_the_snippets_and_is_not_text_is_not_listed()
        {
            await Create().SaveAsync(new EmojicFile { Name = "One.🍇", Code = "1" });
            File.WriteAllBytes(InRoot(FileLibraryService.Folder, "logo.png"), [0x89, 0x50, 0xFF, 0xFE, 0x00]);

            (await Create().GetSavedAsync()).Select(file => file.Name).ShouldBe(["One.🍇"]);
        }

        [Fact]
        public async Task A_disk_that_cannot_be_written_to_is_reported_as_a_store_failure()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
            var aFile = _root + ".txt";
            File.WriteAllText(aFile, "in the way");
            try
            {
                await Should.ThrowAsync<ProjectStoreException>(() => Create(Path.Combine(aFile, "projects")).SaveAsync(new EmojicFile { Name = "One.🍇", Code = "1" }));
            }
            finally
            {
                File.Delete(aFile);
            }
        }
    }
}
