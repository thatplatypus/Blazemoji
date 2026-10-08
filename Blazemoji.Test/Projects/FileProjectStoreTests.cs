using System.Text;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Projects;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Blazemoji.Test.Projects
{
    public sealed class FileProjectStoreTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "projects-" + Guid.NewGuid().ToString("N"));
        private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 7, 21, 30, 0, TimeSpan.Zero));

        private static readonly Project Todo = new(
            "abc123", "Todo API", ProjectKind.Server, "app/main.🍇",
            [new ProjectFile("app/main.🍇", "📦 grapevine 🏠\n🏁 🍇 🍉"), new ProjectFile("app/todos.🍇", "🐇 📒 🍇 🍉")]);

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private FileProjectStore CreateStore(string? root = null) =>
            new(Options.Create(new FileProjectStoreOptions { Root = root ?? _root }), _clock);

        private string InRoot(params string[] parts) => Path.Combine([_root, .. parts]);

        private string[] Trashed() =>
            Directory.Exists(InRoot(".blazemoji", "trash"))
                ? Directory.GetFiles(InRoot(".blazemoji", "trash"), "*", SearchOption.AllDirectories)
                : [];

        [Fact]
        public async Task A_folder_that_is_not_there_yet_has_no_projects_and_is_not_made_by_looking()
        {
            var store = CreateStore();

            (await store.ListAsync()).ShouldBeEmpty();
            (await store.LoadAsync("abc123")).ShouldBeNull();
            (await store.GetLastOpenedAsync()).ShouldBeNull();
            Directory.Exists(_root).ShouldBeFalse();
        }

        [Fact]
        public async Task A_saved_project_comes_back_as_it_was()
        {
            await CreateStore().SaveAsync(Todo);

            var loaded = await CreateStore().LoadAsync("abc123");

            loaded.ShouldNotBeNull();
            loaded.Id.ShouldBe("abc123");
            loaded.Name.ShouldBe("Todo API");
            loaded.Kind.ShouldBe(ProjectKind.Server);
            loaded.Entry.ShouldBe("app/main.🍇");
            loaded.Files.OrderBy(file => file.Path, StringComparer.Ordinal).ShouldBe(Todo.Files);
        }

        [Fact]
        public async Task A_project_is_a_folder_named_after_it_with_each_file_in_it_as_the_text_it_is()
        {
            await CreateStore().SaveAsync(Todo);

            File.ReadAllBytes(InRoot("Todo API", "app", "main.🍇")).ShouldBe(Encoding.UTF8.GetBytes("📦 grapevine 🏠\n🏁 🍇 🍉"));
            File.ReadAllText(InRoot("Todo API", "app", "todos.🍇")).ShouldBe("🐇 📒 🍇 🍉");
        }

        [Fact]
        public async Task What_a_project_is_called_and_how_it_starts_is_written_in_the_folder_for_a_person_to_read()
        {
            await CreateStore().SaveAsync(Todo);

            var description = File.ReadAllText(InRoot("Todo API", FileProjectStore.DescriptionFile));

            description.ShouldContain("\"name\": \"Todo API\"");
            description.ShouldContain("\"kind\": \"server\"");
            description.ShouldContain("\"entry\": \"app/main.🍇\"");
            description.ShouldContain("\"id\": \"abc123\"");
        }

        [Fact]
        public async Task Saved_projects_are_listed_once_each_under_their_latest_name()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            await store.SaveAsync(Todo with { Name = "Todo API v2" });
            await store.SaveAsync(Todo with { Id = "def456", Name = "Hello" });

            (await CreateStore().ListAsync()).OrderBy(project => project.Id).ShouldBe([new ProjectSummary("abc123", "Todo API v2"), new ProjectSummary("def456", "Hello")]);
        }

        [Fact]
        public async Task A_renamed_project_moves_to_a_folder_with_its_new_name_and_takes_everything_with_it()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            File.WriteAllText(InRoot("Todo API", "README.md"), "notes");

            await store.SaveAsync(Todo with { Name = "Todo API v2" });

            Directory.Exists(InRoot("Todo API")).ShouldBeFalse();
            File.ReadAllText(InRoot("Todo API v2", "app", "todos.🍇")).ShouldBe("🐇 📒 🍇 🍉");
            File.ReadAllText(InRoot("Todo API v2", "README.md")).ShouldBe("notes");
        }

        [Fact]
        public async Task Two_projects_with_the_same_name_have_a_folder_each()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            await store.SaveAsync(Todo with { Id = "def456", Files = [new ProjectFile("app/main.🍇", "the other")] });

            (await store.LoadAsync("abc123"))!.Find("app/main.🍇")!.Content.ShouldBe("📦 grapevine 🏠\n🏁 🍇 🍉");
            (await store.LoadAsync("def456"))!.Find("app/main.🍇")!.Content.ShouldBe("the other");
            Directory.GetDirectories(_root).Select(Path.GetFileName).Where(name => name != ".blazemoji").Order().ShouldBe(["Todo API", "Todo API 2"]);
        }

        [Theory]
        [InlineData("a/b\\c:d", "a-b-c-d")]
        [InlineData("What? <now>", "What- -now-")]
        [InlineData("..", "Project")]
        [InlineData(".hidden", "hidden")]
        [InlineData("ends with a dot.", "ends with a dot")]
        [InlineData("🔥 Fire", "🔥 Fire")]
        public async Task A_name_that_cannot_name_a_folder_is_made_into_one_that_can_and_the_project_keeps_its_name(string name, string folder)
        {
            await CreateStore().SaveAsync(Todo with { Name = name });

            Directory.Exists(InRoot(folder)).ShouldBeTrue();
            (await CreateStore().ListAsync()).Single().Name.ShouldBe(name);
        }

        [Fact]
        public async Task A_deleted_project_is_gone_from_the_list_and_its_folder_is_in_the_trash_with_everything_in_it()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            File.WriteAllText(InRoot("Todo API", "README.md"), "notes");

            await store.DeleteAsync("abc123");

            (await store.ListAsync()).ShouldBeEmpty();
            (await CreateStore().LoadAsync("abc123")).ShouldBeNull();
            Directory.Exists(InRoot("Todo API")).ShouldBeFalse();
            File.ReadAllText(InRoot(".blazemoji", "trash", "2026-10-07 213000 Todo API", "app", "todos.🍇")).ShouldBe("🐇 📒 🍇 🍉");
            File.ReadAllText(InRoot(".blazemoji", "trash", "2026-10-07 213000 Todo API", "README.md")).ShouldBe("notes");
        }

        [Fact]
        public async Task Deleting_a_project_that_is_not_there_does_nothing()
        {
            await CreateStore().DeleteAsync("nobody");

            Directory.Exists(_root).ShouldBeFalse();
        }

        [Fact]
        public async Task The_last_opened_project_is_remembered()
        {
            await CreateStore().SetLastOpenedAsync("abc123");

            (await CreateStore().GetLastOpenedAsync()).ShouldBe("abc123");
        }

        [Fact]
        public async Task Saving_after_one_file_changed_writes_that_file_and_nothing_else()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            var longAgo = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            foreach (var path in Directory.GetFiles(InRoot("Todo API"), "*", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(path, longAgo);

            await store.SaveAsync(Todo with { Files = [Todo.Files[0], Todo.Files[1] with { Content = "changed" }] });

            File.ReadAllText(InRoot("Todo API", "app", "todos.🍇")).ShouldBe("changed");
            File.GetLastWriteTimeUtc(InRoot("Todo API", "app", "todos.🍇")).ShouldNotBe(longAgo);
            File.GetLastWriteTimeUtc(InRoot("Todo API", "app", "main.🍇")).ShouldBe(longAgo);
            File.GetLastWriteTimeUtc(InRoot("Todo API", FileProjectStore.DescriptionFile)).ShouldBe(longAgo);
            Trashed().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_file_that_left_the_project_leaves_its_folder_for_the_trash()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);

            await store.SaveAsync(Todo with { Files = [Todo.Files[0]] });

            File.Exists(InRoot("Todo API", "app", "todos.🍇")).ShouldBeFalse();
            File.ReadAllText(InRoot(".blazemoji", "trash", "2026-10-07 213000 Todo API", "app", "todos.🍇")).ShouldBe("🐇 📒 🍇 🍉");
            (await CreateStore().LoadAsync("abc123"))!.Files.Select(file => file.Path).ShouldBe(["app/main.🍇"]);
        }

        [Fact]
        public async Task A_folder_left_empty_by_a_file_that_moved_is_removed()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);

            await store.SaveAsync(Todo with { Entry = "main.🍇", Files = [new ProjectFile("main.🍇", "🏁 🍇 🍉")] });

            Directory.Exists(InRoot("Todo API", "app")).ShouldBeFalse();
            File.Exists(InRoot("Todo API", "main.🍇")).ShouldBeTrue();
        }

        [Fact]
        public async Task What_is_in_the_folder_and_was_never_the_projects_is_left_alone_and_is_not_shown()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            Directory.CreateDirectory(InRoot("Todo API", ".git"));
            File.WriteAllText(InRoot("Todo API", ".git", "config"), "[core]");
            File.WriteAllBytes(InRoot("Todo API", "logo.png"), [0x89, 0x50, 0x4E, 0x47, 0xFF, 0xFE, 0x00, 0x01]);
            File.WriteAllText(InRoot("Todo API", "notes about it.txt"), "a name the compiler would not take");

            var loaded = await CreateStore().LoadAsync("abc123");
            await store.SaveAsync(Todo with { Files = [Todo.Files[0]] });
            await store.SaveAsync(Todo);

            loaded!.Files.Select(file => file.Path).Order(StringComparer.Ordinal).ShouldBe(["app/main.🍇", "app/todos.🍇"]);
            File.ReadAllText(InRoot("Todo API", ".git", "config")).ShouldBe("[core]");
            File.ReadAllBytes(InRoot("Todo API", "logo.png")).Length.ShouldBe(8);
            File.Exists(InRoot("Todo API", "notes about it.txt")).ShouldBeTrue();
        }

        [Fact]
        public async Task A_text_file_put_in_the_folder_by_something_else_is_part_of_the_project_when_it_is_next_opened()
        {
            await CreateStore().SaveAsync(Todo);
            File.WriteAllText(InRoot("Todo API", "app", "routes.🍇"), "🐇 🛣 🍇 🍉");
            File.WriteAllText(InRoot("Todo API", "app", "main.🍇"), "edited elsewhere");

            var loaded = await CreateStore().LoadAsync("abc123");

            loaded!.Find("app/routes.🍇")!.Content.ShouldBe("🐇 🛣 🍇 🍉");
            loaded.Find("app/main.🍇")!.Content.ShouldBe("edited elsewhere");
        }

        [Fact]
        public async Task A_file_that_something_else_changed_since_it_was_read_is_kept_in_the_trash_when_it_is_written_over()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            File.WriteAllText(InRoot("Todo API", "app", "todos.🍇"), "edited elsewhere");

            await store.SaveAsync(Todo with { Files = [Todo.Files[0], Todo.Files[1] with { Content = "edited here" }] });

            File.ReadAllText(InRoot("Todo API", "app", "todos.🍇")).ShouldBe("edited here");
            File.ReadAllText(InRoot(".blazemoji", "trash", "2026-10-07 213000 Todo API", "app", "todos.🍇")).ShouldBe("edited elsewhere");
        }

        [Fact]
        public async Task A_file_that_something_else_changed_is_not_touched_while_nobody_changes_it_here()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            File.WriteAllText(InRoot("Todo API", "app", "todos.🍇"), "edited elsewhere");

            await store.SaveAsync(Todo with { Files = [Todo.Files[0] with { Content = "edited here" }, Todo.Files[1]] });

            File.ReadAllText(InRoot("Todo API", "app", "todos.🍇")).ShouldBe("edited elsewhere");
            Trashed().ShouldBeEmpty();
        }

        [Fact]
        public async Task Two_things_that_go_to_the_trash_under_one_name_in_one_second_are_both_kept()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            await store.SaveAsync(Todo with { Files = [Todo.Files[0]] });
            await store.SaveAsync(Todo);

            await store.SaveAsync(Todo with { Files = [Todo.Files[0]] });

            Trashed().Length.ShouldBe(2);
        }

        [Fact]
        public async Task A_project_folder_that_was_copied_is_a_project_of_its_own()
        {
            await CreateStore().SaveAsync(Todo);
            Directory.CreateDirectory(InRoot("Todo API copy", "app"));
            foreach (var path in Directory.GetFiles(InRoot("Todo API"), "*", SearchOption.AllDirectories))
                File.Copy(path, path.Replace(InRoot("Todo API"), InRoot("Todo API copy")));

            var store = CreateStore();
            var listed = await store.ListAsync();

            listed.Count.ShouldBe(2);
            listed.Select(project => project.Id).Distinct().Count().ShouldBe(2);
            foreach (var project in listed)
                (await store.LoadAsync(project.Id))!.Files.Count.ShouldBe(2);
            (await CreateStore().ListAsync()).Select(project => project.Id).Order().ShouldBe(listed.Select(project => project.Id).Order());
        }

        [Fact]
        public async Task A_folder_with_no_description_in_it_is_not_a_project()
        {
            Directory.CreateDirectory(InRoot("Just a folder"));
            File.WriteAllText(InRoot("Just a folder", "main.🍇"), "🏁 🍇 🍉");

            (await CreateStore().ListAsync()).ShouldBeEmpty();
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("{ }")]
        [InlineData("""{ "id": "abc123" }""")]
        public async Task A_description_that_cannot_be_read_counts_as_no_project(string description)
        {
            Directory.CreateDirectory(InRoot("Broken"));
            File.WriteAllText(InRoot("Broken", FileProjectStore.DescriptionFile), description);

            var store = CreateStore();

            (await store.ListAsync()).ShouldBeEmpty();
            (await store.LoadAsync("abc123")).ShouldBeNull();
            (await store.GetLastOpenedAsync()).ShouldBeNull();
        }

        [Fact]
        public async Task A_file_whose_path_would_leave_the_project_or_that_the_compiler_would_not_take_is_never_written()
        {
            var store = CreateStore();
            var somewhereElse = _root + "-elsewhere.🍇";
            try
            {
                await store.SaveAsync(Todo with { Files = [Todo.Files[0], new ProjectFile("../outside.🍇", "x"), new ProjectFile(somewhereElse, "x"), new ProjectFile(FileProjectStore.DescriptionFile, "not a description")] });

                File.Exists(InRoot("outside.🍇")).ShouldBeFalse();
                File.Exists(somewhereElse).ShouldBeFalse();
                (await CreateStore().LoadAsync("abc123"))!.Files.Select(file => file.Path).ShouldBe(["app/main.🍇"]);
            }
            finally
            {
                File.Delete(somewhereElse);
            }
        }

        [Fact]
        public async Task A_file_too_large_to_be_source_is_left_out_and_left_alone()
        {
            await CreateStore().SaveAsync(Todo);
            File.WriteAllText(InRoot("Todo API", "huge.🍇"), new string('x', FileProjectStore.LargestFile + 1));

            var loaded = await CreateStore().LoadAsync("abc123");

            loaded!.Files.Select(file => file.Path).ShouldNotContain("huge.🍇");
            File.Exists(InRoot("Todo API", "huge.🍇")).ShouldBeTrue();
        }

        [Fact]
        public async Task A_project_whose_folder_was_taken_away_is_written_out_whole_again()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            Directory.Delete(InRoot("Todo API"), recursive: true);

            await store.SaveAsync(Todo);

            (await CreateStore().LoadAsync("abc123"))!.Files.Count.ShouldBe(2);
        }

        [Fact]
        public async Task A_disk_that_cannot_be_written_to_is_reported_as_a_store_failure()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
            var aFile = _root + ".txt";
            File.WriteAllText(aFile, "in the way");
            try
            {
                var store = CreateStore(Path.Combine(aFile, "projects"));

                await Should.ThrowAsync<ProjectStoreException>(() => store.SaveAsync(Todo));
                await Should.ThrowAsync<ProjectStoreException>(() => store.SetLastOpenedAsync("abc123"));
            }
            finally
            {
                File.Delete(aFile);
            }
        }

        [Fact]
        public void Projects_are_kept_in_the_users_documents_unless_the_host_says_otherwise()
        {
            new FileProjectStoreOptions().Root.ShouldBe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Blazemoji"));
        }
    }
}
