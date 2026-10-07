using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Shared.State;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.State
{
    public class ProjectStateTests
    {
        private static readonly ProjectTemplate Hello = new(
            "hello-world", "Hello World", "One file.", ProjectKind.Program, "main.🍇",
            [new ProjectFile("main.🍇", "🏁 🍇 🍉")]);

        private static readonly ProjectTemplate Api = new(
            "api", "An API", "A server.", ProjectKind.Server, "app/main.🍇",
            [new ProjectFile("app/main.🍇", "main"), new ProjectFile("app/routes.🍇", "routes"), new ProjectFile("shared/util.🍇", "util")]);

        private readonly InMemoryProjectStore _store = new();
        private readonly IProjectTemplates _templates = Substitute.For<IProjectTemplates>();
        private readonly RecordingLogger<ProjectState> _logger = new();

        public ProjectStateTests()
        {
            _templates.All.Returns([Hello, Api]);
        }

        private ProjectState CreateState() => new(_store, _templates, _logger);

        private async Task<ProjectState> LoadedAsync()
        {
            var state = CreateState();
            await state.LoadAsync();
            return state;
        }

        private async Task<ProjectState> WithApiProjectAsync()
        {
            var state = await LoadedAsync();
            (await state.CreateAsync("Todo", "api")).ShouldBeNull();
            return state;
        }

        [Fact]
        public void Before_anything_is_loaded_there_is_already_a_project_to_show()
        {
            var state = CreateState();

            state.Loaded.ShouldBeFalse();
            state.Current.Files.ShouldHaveSingleItem().Path.ShouldBe("main.🍇");
            state.OpenFile.Content.ShouldBe("🏁 🍇 🍉");
        }

        [Fact]
        public async Task A_browser_with_nothing_saved_gets_a_hello_world_project_and_it_is_saved()
        {
            var state = await LoadedAsync();

            state.Loaded.ShouldBeTrue();
            state.Current.Name.ShouldBe("Hello World");
            state.Current.Entry.ShouldBe("main.🍇");
            state.Projects.ShouldHaveSingleItem().ShouldBe(new ProjectSummary(state.Current.Id, "Hello World"));
            _store.Saved[state.Current.Id].ShouldBe(state.Current);
            _store.LastOpened.ShouldBe(state.Current.Id);
        }

        [Fact]
        public async Task Loading_opens_the_project_that_was_open_last()
        {
            var first = new Project("p1", "First", ProjectKind.Program, "main.🍇", [new ProjectFile("main.🍇", "one")]);
            var second = new Project("p2", "Second", ProjectKind.Server, "b.🍇", [new ProjectFile("a.🍇", "a"), new ProjectFile("b.🍇", "b")]);
            _store.Seed(first, second);
            _store.LastOpened = "p2";

            var state = await LoadedAsync();

            state.Current.Id.ShouldBe("p2");
            state.Current.Kind.ShouldBe(ProjectKind.Server);
            state.Current.Files.ShouldBe(second.Files);
            state.OpenPath.ShouldBe("b.🍇");
            state.Projects.Select(p => p.Name).ShouldBe(["First", "Second"]);
        }

        [Fact]
        public async Task Loading_falls_back_to_the_first_project_when_the_last_one_is_gone()
        {
            _store.Seed(new Project("p1", "First", ProjectKind.Program, "main.🍇", [new ProjectFile("main.🍇", "one")]));
            _store.LastOpened = "deleted-elsewhere";

            var state = await LoadedAsync();

            state.Current.Id.ShouldBe("p1");
        }

        [Fact]
        public async Task Loading_tells_subscribers()
        {
            var state = CreateState();
            var changes = 0;
            state.StateChanged += () => changes++;

            await state.LoadAsync();

            changes.ShouldBeGreaterThan(0);
        }

        [Fact]
        public async Task A_project_made_from_a_template_has_its_files_kind_and_entry_and_becomes_current()
        {
            var state = await LoadedAsync();

            var refusal = await state.CreateAsync("  Todo API  ", "api");

            refusal.ShouldBeNull();
            state.Current.Name.ShouldBe("Todo API");
            state.Current.Kind.ShouldBe(ProjectKind.Server);
            state.Current.Entry.ShouldBe("app/main.🍇");
            state.Current.Files.Select(f => f.Path).ShouldBe(["app/main.🍇", "app/routes.🍇", "shared/util.🍇"]);
            state.OpenPath.ShouldBe("app/main.🍇");
            state.Projects.Select(p => p.Name).ShouldBe(["Hello World", "Todo API"]);
            _store.Saved[state.Current.Id].ShouldBe(state.Current);
            _store.LastOpened.ShouldBe(state.Current.Id);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task A_project_needs_a_name(string name)
        {
            var state = await LoadedAsync();

            (await state.CreateAsync(name, "api")).ShouldBe("Give the project a name.");
            state.Projects.Count.ShouldBe(1);
        }

        [Fact]
        public async Task A_project_cannot_be_made_from_a_template_that_does_not_exist()
        {
            var state = await LoadedAsync();

            (await state.CreateAsync("X", "no-such-template")).ShouldBe("That template is not available.");
        }

        [Fact]
        public async Task Opening_another_project_makes_it_current_and_remembers_it()
        {
            var state = await WithApiProjectAsync();
            var hello = state.Projects.Single(p => p.Name == "Hello World");

            await state.OpenAsync(hello.Id);

            state.Current.Id.ShouldBe(hello.Id);
            state.OpenPath.ShouldBe("main.🍇");
            _store.LastOpened.ShouldBe(hello.Id);
        }

        [Fact]
        public async Task Renaming_the_project_changes_its_name_everywhere()
        {
            var state = await LoadedAsync();

            (await state.RenameProjectAsync("Greeter")).ShouldBeNull();

            state.Current.Name.ShouldBe("Greeter");
            state.Projects.ShouldHaveSingleItem().Name.ShouldBe("Greeter");
            _store.Saved[state.Current.Id].Name.ShouldBe("Greeter");
        }

        [Fact]
        public async Task Deleting_the_current_project_opens_another()
        {
            var state = await WithApiProjectAsync();
            var deleted = state.Current.Id;

            await state.DeleteProjectAsync(deleted);

            state.Current.Name.ShouldBe("Hello World");
            state.Projects.ShouldHaveSingleItem().Name.ShouldBe("Hello World");
            _store.Saved.ShouldNotContainKey(deleted);
        }

        [Fact]
        public async Task Deleting_the_only_project_leaves_a_fresh_hello_world()
        {
            var state = await LoadedAsync();
            var deleted = state.Current.Id;

            await state.DeleteProjectAsync(deleted);

            state.Current.Id.ShouldNotBe(deleted);
            state.Current.Name.ShouldBe("Hello World");
            state.Projects.ShouldHaveSingleItem().Id.ShouldBe(state.Current.Id);
        }

        [Fact]
        public async Task A_new_file_is_added_opened_and_saved()
        {
            var state = await LoadedAsync();

            (await state.AddFileAsync("lib/greeter.🍇")).ShouldBeNull();

            state.Current.Files.Select(f => f.Path).ShouldBe(["lib/greeter.🍇", "main.🍇"]);
            state.OpenPath.ShouldBe("lib/greeter.🍇");
            state.OpenFile.Content.ShouldBe(string.Empty);
            state.Current.Entry.ShouldBe("main.🍇");
            _store.Saved[state.Current.Id].ShouldBe(state.Current);
        }

        [Fact]
        public async Task A_file_name_without_an_extension_is_given_the_emojicode_one()
        {
            var state = await LoadedAsync();

            (await state.AddFileAsync("greeter")).ShouldBeNull();

            state.OpenPath.ShouldBe("greeter.🍇");
        }

        [Theory]
        [InlineData("", "Give the file a name.")]
        [InlineData("main.🍇", "A file with that name already exists.")]
        [InlineData("../escape.🍇", ProjectState.FileNameRule)]
        [InlineData("/rooted.🍇", ProjectState.FileNameRule)]
        [InlineData("two words.🍇", ProjectState.FileNameRule)]
        [InlineData("semi;colon.🍇", ProjectState.FileNameRule)]
        [InlineData("-option.🍇", ProjectState.FileNameRule)]
        [InlineData("main.🍇/inside-a-file.🍇", "A file cannot be inside another file.")]
        public async Task A_file_name_that_cannot_be_used_is_refused_with_a_reason(string path, string reason)
        {
            var state = await LoadedAsync();

            (await state.AddFileAsync(path)).ShouldBe(reason);

            state.Current.Files.ShouldHaveSingleItem();
        }

        [Fact]
        public async Task A_folder_cannot_take_the_name_of_a_file_nor_a_file_the_name_of_a_folder()
        {
            var state = await WithApiProjectAsync();

            (await state.AddFileAsync("v1.0/notes.🍇")).ShouldBeNull();

            (await state.AddFileAsync("v1.0")).ShouldBe("A folder with that name already exists.");
            (await state.AddFileAsync("v1.0/notes.🍇/more.🍇")).ShouldBe("A file cannot be inside another file.");
        }

        [Fact]
        public async Task Renaming_a_file_keeps_its_content_and_follows_it_if_it_was_open_or_the_entry()
        {
            var state = await WithApiProjectAsync();

            (await state.RenameFileAsync("app/main.🍇", "app/start.🍇")).ShouldBeNull();

            state.Current.Files.Select(f => f.Path).ShouldBe(["app/routes.🍇", "app/start.🍇", "shared/util.🍇"]);
            state.Current.Find("app/start.🍇")!.Content.ShouldBe("main");
            state.Current.Entry.ShouldBe("app/start.🍇");
            state.OpenPath.ShouldBe("app/start.🍇");
        }

        [Fact]
        public async Task Renaming_a_file_onto_another_is_refused()
        {
            var state = await WithApiProjectAsync();

            (await state.RenameFileAsync("app/main.🍇", "app/routes.🍇")).ShouldBe("A file with that name already exists.");
        }

        [Fact]
        public async Task Deleting_the_entry_file_makes_another_file_the_entry()
        {
            var state = await WithApiProjectAsync();

            (await state.DeleteFileAsync("app/main.🍇")).ShouldBeNull();

            state.Current.Files.Select(f => f.Path).ShouldBe(["app/routes.🍇", "shared/util.🍇"]);
            state.Current.Entry.ShouldBe("app/routes.🍇");
            state.OpenPath.ShouldBe("app/routes.🍇");
        }

        [Fact]
        public async Task The_last_file_cannot_be_deleted()
        {
            var state = await LoadedAsync();

            (await state.DeleteFileAsync("main.🍇")).ShouldBe("A project needs at least one file.");

            state.Current.Files.ShouldHaveSingleItem();
        }

        [Fact]
        public async Task Another_file_can_be_made_the_entry()
        {
            var state = await WithApiProjectAsync();

            await state.SetEntryAsync("app/routes.🍇");

            state.Current.Entry.ShouldBe("app/routes.🍇");
            _store.Saved[state.Current.Id].Entry.ShouldBe("app/routes.🍇");
        }

        [Fact]
        public async Task The_kind_of_project_can_be_changed()
        {
            var state = await LoadedAsync();

            await state.SetKindAsync(ProjectKind.Server);

            state.Current.Kind.ShouldBe(ProjectKind.Server);
            _store.Saved[state.Current.Id].Kind.ShouldBe(ProjectKind.Server);
        }

        [Fact]
        public async Task Selecting_a_file_opens_it_without_saving_anything()
        {
            var state = await WithApiProjectAsync();
            var saves = _store.SaveCount;
            var changes = 0;
            state.StateChanged += () => changes++;

            state.SelectFile("shared/util.🍇");

            state.OpenPath.ShouldBe("shared/util.🍇");
            state.OpenFile.Content.ShouldBe("util");
            changes.ShouldBe(1);
            _store.SaveCount.ShouldBe(saves);
        }

        [Fact]
        public async Task Selecting_a_file_that_does_not_exist_changes_nothing()
        {
            var state = await WithApiProjectAsync();

            state.SelectFile("nope.🍇");

            state.OpenPath.ShouldBe("app/main.🍇");
        }

        [Fact]
        public async Task Edited_content_is_kept_and_saved_without_redrawing_the_tree()
        {
            var state = await WithApiProjectAsync();
            var changes = 0;
            state.StateChanged += () => changes++;

            await state.UpdateContentAsync("app/routes.🍇", "new routes");

            state.Current.Find("app/routes.🍇")!.Content.ShouldBe("new routes");
            _store.Saved[state.Current.Id].Find("app/routes.🍇")!.Content.ShouldBe("new routes");
            changes.ShouldBe(0);
        }

        [Fact]
        public async Task Content_that_has_not_changed_is_not_saved_again()
        {
            var state = await WithApiProjectAsync();
            var saves = _store.SaveCount;

            await state.UpdateContentAsync("app/routes.🍇", "routes");

            _store.SaveCount.ShouldBe(saves);
        }

        [Fact]
        public async Task Content_for_a_file_that_is_gone_is_dropped()
        {
            var state = await WithApiProjectAsync();

            await Should.NotThrowAsync(() => state.UpdateContentAsync("deleted.🍇", "late edit"));

            state.Current.Find("deleted.🍇").ShouldBeNull();
        }

        [Fact]
        public async Task A_save_that_fails_is_shown_and_does_not_lose_the_change()
        {
            var state = await LoadedAsync();
            var changes = 0;
            state.StateChanged += () => changes++;
            _store.FailWrites = true;

            await state.UpdateContentAsync("main.🍇", "edited while storage is full");

            state.SaveFailed.ShouldBeTrue();
            state.OpenFile.Content.ShouldBe("edited while storage is full");
            changes.ShouldBe(1);
            _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task A_save_that_works_again_clears_the_warning()
        {
            var state = await LoadedAsync();
            _store.FailWrites = true;
            await state.UpdateContentAsync("main.🍇", "one");
            _store.FailWrites = false;

            await state.UpdateContentAsync("main.🍇", "two");

            state.SaveFailed.ShouldBeFalse();
        }

        [Fact]
        public async Task What_was_typed_while_saving_failed_is_still_there_after_looking_at_another_project()
        {
            var state = await LoadedAsync();
            var first = state.Current.Id;
            await state.CreateAsync("Second", "api");
            var second = state.Current.Id;
            _store.FailWrites = true;
            await state.UpdateContentAsync("app/main.🍇", "typed while storage is full");

            await state.OpenAsync(first);
            await state.OpenAsync(second);

            state.Current.Id.ShouldBe(second);
            state.Current.Find("app/main.🍇")!.Content.ShouldBe("typed while storage is full");
        }

        [Fact]
        public async Task A_project_made_while_saving_failed_can_be_opened_again()
        {
            var state = await LoadedAsync();
            var first = state.Current.Id;
            _store.FailWrites = true;
            await state.CreateAsync("Never saved", "api");
            var made = state.Current.Id;

            await state.OpenAsync(first);
            await state.OpenAsync(made);

            state.Current.Name.ShouldBe("Never saved");
            state.Projects.Select(project => project.Name).ShouldBe(["Hello World", "Never saved"]);
        }

        [Fact]
        public async Task When_saving_works_again_everything_that_was_only_in_memory_is_saved()
        {
            var state = await LoadedAsync();
            var first = state.Current.Id;
            _store.FailWrites = true;
            await state.UpdateContentAsync("main.🍇", "first, typed while storage is full");
            await state.CreateAsync("Second", "api");
            var second = state.Current.Id;
            _store.FailWrites = false;

            await state.UpdateContentAsync("app/main.🍇", "second, typed once storage is back");

            state.SaveFailed.ShouldBeFalse();
            _store.Saved[first].Find("main.🍇")!.Content.ShouldBe("first, typed while storage is full");
            _store.Saved[second].Find("app/main.🍇")!.Content.ShouldBe("second, typed once storage is back");
        }

        [Fact]
        public async Task Deleting_the_open_project_while_saving_fails_still_moves_on_to_another()
        {
            var state = await LoadedAsync();
            var first = state.Current.Id;
            await state.CreateAsync("Second", "api");
            var second = state.Current.Id;
            _store.FailWrites = true;

            await state.DeleteProjectAsync(second);

            state.Current.Id.ShouldBe(first);
            state.Projects.ShouldHaveSingleItem().Id.ShouldBe(first);
            state.SaveFailed.ShouldBeTrue();
        }

        [Fact]
        public async Task With_storage_that_cannot_be_read_the_first_project_can_still_be_come_back_to()
        {
            _store.FailReads = true;
            var state = await LoadedAsync();
            var first = state.Current.Id;
            await state.UpdateContentAsync("main.🍇", "typed with no storage at all");
            await state.CreateAsync("Second", "api");

            await state.OpenAsync(first);

            state.Current.Id.ShouldBe(first);
            state.OpenFile.Content.ShouldBe("typed with no storage at all");
        }

        [Fact]
        public async Task Storage_that_cannot_be_read_leaves_a_working_project_in_memory()
        {
            _store.FailReads = true;

            var state = await LoadedAsync();

            state.Loaded.ShouldBeTrue();
            state.SaveFailed.ShouldBeTrue();
            state.Current.Name.ShouldBe("Hello World");
            (await state.AddFileAsync("more.🍇")).ShouldBeNull();
            state.Current.Files.Count.ShouldBe(2);
        }

        [Fact]
        public async Task A_stored_project_whose_entry_is_missing_is_repaired_when_opened()
        {
            _store.Seed(new Project("p1", "Odd", ProjectKind.Program, "gone.🍇", [new ProjectFile("b.🍇", "b"), new ProjectFile("a.🍇", "a")]));

            var state = await LoadedAsync();

            state.Current.Entry.ShouldBe("a.🍇");
            state.Current.Files.Select(f => f.Path).ShouldBe(["a.🍇", "b.🍇"]);
        }

        [Fact]
        public async Task A_stored_project_with_no_files_is_skipped()
        {
            _store.Seed(new Project("p1", "Empty", ProjectKind.Program, "main.🍇", []));

            var state = await LoadedAsync();

            state.Current.Name.ShouldBe("Hello World");
        }

        [Fact]
        public async Task The_files_and_entry_to_compile_come_from_the_current_project()
        {
            var state = await WithApiProjectAsync();

            state.Current.Files.ToDictionary(f => f.Path, f => f.Content).ShouldBe(new Dictionary<string, string>
            {
                ["app/main.🍇"] = "main",
                ["app/routes.🍇"] = "routes",
                ["shared/util.🍇"] = "util",
            });
        }

        private sealed class InMemoryProjectStore : IProjectStore
        {
            public Dictionary<string, Project> Saved { get; } = [];

            public string? LastOpened { get; set; }

            public bool FailWrites { get; set; }

            public bool FailReads { get; set; }

            public int SaveCount { get; private set; }

            public void Seed(params Project[] projects)
            {
                foreach (var project in projects)
                    Saved[project.Id] = project;
            }

            public Task<IReadOnlyList<ProjectSummary>> ListAsync()
            {
                ThrowIf(FailReads);
                return Task.FromResult<IReadOnlyList<ProjectSummary>>(Saved.Values.Select(p => new ProjectSummary(p.Id, p.Name)).ToList());
            }

            public Task<Project?> LoadAsync(string projectId)
            {
                ThrowIf(FailReads);
                return Task.FromResult(Saved.GetValueOrDefault(projectId));
            }

            public Task SaveAsync(Project project)
            {
                ThrowIf(FailWrites || FailReads);
                SaveCount++;
                Saved[project.Id] = project;
                return Task.CompletedTask;
            }

            public Task DeleteAsync(string projectId)
            {
                ThrowIf(FailWrites || FailReads);
                Saved.Remove(projectId);
                return Task.CompletedTask;
            }

            public Task<string?> GetLastOpenedAsync()
            {
                ThrowIf(FailReads);
                return Task.FromResult(LastOpened);
            }

            public Task SetLastOpenedAsync(string projectId)
            {
                ThrowIf(FailWrites || FailReads);
                LastOpened = projectId;
                return Task.CompletedTask;
            }

            private static void ThrowIf(bool fail)
            {
                if (fail)
                    throw new ProjectStoreException("The storage is not available.", new InvalidOperationException("quota"));
            }
        }
    }
}
