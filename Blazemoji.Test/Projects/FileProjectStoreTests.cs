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

        private static string[] NamesIn(string folder) =>
            Directory.GetFileSystemEntries(folder).Select(entry => Path.GetFileName(entry)!).Where(name => name != ".blazemoji").Order(StringComparer.Ordinal).ToArray();

        [Fact]
        public async Task A_file_renamed_only_in_the_case_of_its_letters_is_still_there_under_its_new_name()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);

            await store.SaveAsync(Todo with { Entry = "app/Main.🍇", Files = [Todo.Files[0] with { Path = "app/Main.🍇" }, Todo.Files[1]] });

            NamesIn(InRoot("Todo API", "app")).ShouldBe(["Main.🍇", "todos.🍇"]);
            (await CreateStore().LoadAsync("abc123"))!.Find("app/Main.🍇")!.Content.ShouldBe("📦 grapevine 🏠\n🏁 🍇 🍉");
        }

        [Fact]
        public async Task A_file_can_take_the_name_a_folder_had_and_a_folder_the_name_a_file_had()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);

            await store.SaveAsync(Todo with { Entry = "app", Files = [new ProjectFile("app", "now a file")] });
            File.ReadAllText(InRoot("Todo API", "app")).ShouldBe("now a file");

            await store.SaveAsync(Todo with { Entry = "app/main.🍇", Files = [new ProjectFile("app/main.🍇", "a folder again")] });
            File.ReadAllText(InRoot("Todo API", "app", "main.🍇")).ShouldBe("a folder again");
        }

        [Fact]
        public async Task A_file_that_is_not_text_and_is_where_a_new_file_goes_is_kept_in_the_trash()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            byte[] notUtf8 = [0x63, 0x61, 0x66, 0xE9];
            File.WriteAllBytes(InRoot("Todo API", "notes.txt"), notUtf8);

            await store.SaveAsync(Todo with { Files = [.. Todo.Files, new ProjectFile("notes.txt", "mine")] });

            File.ReadAllText(InRoot("Todo API", "notes.txt")).ShouldBe("mine");
            File.ReadAllBytes(InRoot(".blazemoji", "trash", "2026-10-07 213000 Todo API", "notes.txt")).ShouldBe(notUtf8);
        }

        [Fact]
        public async Task Text_the_app_wrote_is_not_taken_for_something_elses_change_because_it_holds_an_odd_character()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo with { Files = [Todo.Files[0] with { Content = "a\0b" }, Todo.Files[1] with { Content = "\uFEFFstarts oddly" }] });

            await store.SaveAsync(Todo with { Files = [Todo.Files[0] with { Content = "a\0c" }, Todo.Files[1] with { Content = "\uFEFFstill does" }] });

            Trashed().ShouldBeEmpty();
        }

        [Fact]
        public async Task Looking_for_projects_from_two_places_at_once_finds_each_once_and_changes_nothing()
        {
            await CreateStore().SaveAsync(Todo);
            await CreateStore().SaveAsync(Todo with { Id = "def456", Name = "Hello" });
            var before = File.ReadAllText(InRoot("Hello", FileProjectStore.DescriptionFile));
            var store = CreateStore();

            var listing = store.ListAsync();
            var again = store.ListAsync();
            var loading = store.LoadAsync("abc123");
            await Task.WhenAll(listing, again, loading);

            (await listing).Select(project => project.Id).Order().ShouldBe(["abc123", "def456"]);
            (await again).Select(project => project.Id).Order().ShouldBe(["abc123", "def456"]);
            (await loading).ShouldNotBeNull();
            File.ReadAllText(InRoot("Hello", FileProjectStore.DescriptionFile)).ShouldBe(before);
        }

        [Fact]
        public async Task Saves_made_faster_than_the_disk_takes_them_land_in_the_order_they_were_made()
        {
            var store = CreateStore();
            var large = new string('x', 400_000);

            var saves = Enumerable.Range(1, 12)
                .Select(turn => store.SaveAsync(Todo with { Files = [Todo.Files[0] with { Content = large + turn }, Todo.Files[1]] }))
                .ToList();
            await Task.WhenAll(saves);

            File.ReadAllText(InRoot("Todo API", "app", "main.🍇")).ShouldBe(large + 12);
            (await CreateStore().LoadAsync("abc123"))!.Find("app/main.🍇")!.Content.ShouldBe(large + 12);
            Trashed().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_save_still_on_its_way_when_the_project_is_deleted_does_not_bring_the_folder_back()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);

            var saving = store.SaveAsync(Todo with { Files = [Todo.Files[0] with { Content = new string('x', 400_000) }, Todo.Files[1]] });
            var deleting = store.DeleteAsync("abc123");
            await Task.WhenAll(saving, deleting);

            Directory.Exists(InRoot("Todo API")).ShouldBeFalse();
            (await CreateStore().ListAsync()).ShouldBeEmpty();
        }

        [Fact]
        public async Task A_project_is_never_given_the_folder_that_saved_snippets_are_kept_in()
        {
            var store = CreateStore();

            await store.SaveAsync(Todo with { Name = "Snippets" });
            await store.SaveAsync(Todo with { Id = "def456", Name = "snippets" });

            NamesIn(_root).ShouldNotContain("Snippets");
            NamesIn(_root).ShouldNotContain("snippets");
            (await CreateStore().ListAsync()).Count.ShouldBe(2);
        }

        [Fact]
        public async Task The_folder_that_saved_snippets_are_kept_in_is_never_taken_for_a_project()
        {
            await CreateStore().SaveAsync(Todo);
            Directory.Move(InRoot("Todo API"), InRoot("Snippets"));

            (await CreateStore().ListAsync()).ShouldBeEmpty();
        }

        [Fact]
        public async Task A_file_named_as_the_description_is_in_any_case_of_letters_never_takes_its_place()
        {
            var store = CreateStore();

            await store.SaveAsync(Todo with { Files = [.. Todo.Files, new ProjectFile("Blazemoji.json", "not a description")] });
            await store.SaveAsync(Todo with { Files = [.. Todo.Files, new ProjectFile("Blazemoji.json", "still not one")] });

            (await CreateStore().LoadAsync("abc123"))!.Name.ShouldBe("Todo API");
            File.ReadAllText(InRoot("Todo API", FileProjectStore.DescriptionFile)).ShouldContain("\"name\": \"Todo API\"");
            Trashed().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_file_and_a_folder_that_cannot_be_read_are_left_out_and_the_rest_of_the_project_opens()
        {
            await CreateStore().SaveAsync(Todo);
            File.WriteAllText(InRoot("Todo API", "locked.🍇"), "cannot be read");
            Directory.CreateDirectory(InRoot("Todo API", "shut"));
            File.WriteAllText(InRoot("Todo API", "shut", "inside.🍇"), "cannot be reached");
            File.SetUnixFileMode(InRoot("Todo API", "locked.🍇"), UnixFileMode.None);
            File.SetUnixFileMode(InRoot("Todo API", "shut"), UnixFileMode.None);
            try
            {
                var loaded = await CreateStore().LoadAsync("abc123");

                loaded.ShouldNotBeNull();
                loaded.Files.Select(file => file.Path).ShouldContain("app/main.🍇");
                loaded.Files.Select(file => file.Path).ShouldContain("app/todos.🍇");
                // Whoever runs the tests as root can read anything, and then these two are simply there.
                if (!Environment.IsPrivilegedProcess)
                    loaded.Files.Count.ShouldBe(2);
            }
            finally
            {
                File.SetUnixFileMode(InRoot("Todo API", "shut"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        [Fact]
        public async Task A_name_the_disk_cannot_spell_is_reported_as_a_store_failure()
        {
            await Should.ThrowAsync<ProjectStoreException>(() => CreateStore().SaveAsync(Todo with { Name = "Half an emoji \uD83C" }));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void A_root_that_was_set_to_nothing_is_the_usual_one(string root)
        {
            Should.NotThrow(() => CreateStore(root));
        }

        [Fact]
        public async Task A_write_that_fails_leaves_nothing_of_its_own_behind()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            Directory.CreateDirectory(InRoot("Todo API", "taken"));
            File.WriteAllBytes(InRoot("Todo API", "taken", "logo.png"), [0xFF, 0xFE]);

            await Should.ThrowAsync<ProjectStoreException>(() => store.SaveAsync(Todo with { Files = [.. Todo.Files, new ProjectFile("taken", "a file where a folder is")] }));

            Directory.GetFiles(InRoot("Todo API"), "*.tmp", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }).ShouldBeEmpty();
            File.Exists(InRoot("Todo API", "taken", "logo.png")).ShouldBeTrue();
        }

        [Fact]
        public async Task A_folder_that_is_a_link_to_somewhere_else_is_never_written_through()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            var elsewhere = _root + "-elsewhere";
            Directory.CreateDirectory(elsewhere);
            try
            {
                Directory.CreateSymbolicLink(InRoot("Todo API", "lib"), elsewhere);

                await Should.ThrowAsync<ProjectStoreException>(() => store.SaveAsync(Todo with { Files = [.. Todo.Files, new ProjectFile("lib/x.🍇", "x")] }));

                Directory.GetFileSystemEntries(elsewhere).ShouldBeEmpty();
            }
            finally
            {
                Directory.Delete(InRoot("Todo API", "lib"));
                Directory.Delete(elsewhere, recursive: true);
            }
        }

        [Fact]
        public async Task A_file_that_is_a_link_to_somewhere_else_is_left_out_and_left_as_it_is()
        {
            await CreateStore().SaveAsync(Todo);
            var elsewhere = _root + "-elsewhere.🍇";
            File.WriteAllText(elsewhere, "someone else's");
            try
            {
                File.CreateSymbolicLink(InRoot("Todo API", "linked.🍇"), elsewhere);
                var store = CreateStore();

                var loaded = await store.LoadAsync("abc123");
                await Should.ThrowAsync<ProjectStoreException>(() => store.SaveAsync(Todo with { Files = [.. Todo.Files, new ProjectFile("linked.🍇", "mine")] }));

                loaded!.Files.Select(file => file.Path).ShouldNotContain("linked.🍇");
                File.ReadAllText(elsewhere).ShouldBe("someone else's");
            }
            finally
            {
                File.Delete(elsewhere);
            }
        }

        [Fact]
        public async Task A_project_whose_folder_was_renamed_by_hand_is_found_again_by_what_it_says_it_is()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            Directory.Move(InRoot("Todo API"), InRoot("Renamed by hand"));

            await store.SaveAsync(Todo with { Files = [Todo.Files[0], Todo.Files[1] with { Content = "changed" }] });

            NamesIn(_root).ShouldBe(["Renamed by hand"]);
            File.ReadAllText(InRoot("Renamed by hand", "app", "todos.🍇")).ShouldBe("changed");
        }

        [Fact]
        public async Task A_project_renamed_only_in_the_case_of_its_letters_keeps_one_folder_under_the_new_name()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);

            await store.SaveAsync(Todo with { Name = "todo api" });

            NamesIn(_root).ShouldBe(["todo api"]);
            (await CreateStore().LoadAsync("abc123"))!.Files.Count.ShouldBe(2);
        }

        [Fact]
        public async Task A_project_with_a_very_long_name_can_still_be_deleted()
        {
            var store = CreateStore();
            var longName = new string('界', 80);
            await store.SaveAsync(Todo with { Name = longName });

            await store.DeleteAsync("abc123");

            NamesIn(_root).ShouldBeEmpty();
            Directory.GetDirectories(InRoot(".blazemoji", "trash")).Length.ShouldBe(1);
        }

        [Fact]
        public void Projects_are_kept_in_the_users_documents_unless_the_host_says_otherwise()
        {
            new FileProjectStoreOptions().Root.ShouldBe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Blazemoji"));
        }
    }
}
