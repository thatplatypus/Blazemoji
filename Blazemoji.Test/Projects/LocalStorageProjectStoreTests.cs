using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Projects;
using Blazored.LocalStorage;
using Microsoft.JSInterop;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.Projects
{
    public class LocalStorageProjectStoreTests
    {
        private readonly Dictionary<string, string> _browser = [];
        private readonly ILocalStorageService _localStorage = Substitute.For<ILocalStorageService>();

        private static readonly Project Todo = new(
            "abc123", "Todo API", ProjectKind.Server, "app/main.🍇",
            [new ProjectFile("app/main.🍇", "📦 grapevine 🏠\n🏁 🍇 🍉"), new ProjectFile("app/todos.🍇", "🐇 📒 🍇 🍉")]);

        public LocalStorageProjectStoreTests()
        {
            _localStorage.GetItemAsStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => new ValueTask<string?>(_browser.GetValueOrDefault(call.Arg<string>())));
            _localStorage.SetItemAsStringAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    _browser[call.ArgAt<string>(0)] = call.ArgAt<string>(1);
                    return ValueTask.CompletedTask;
                });
            _localStorage.RemoveItemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    _browser.Remove(call.Arg<string>());
                    return ValueTask.CompletedTask;
                });
        }

        private LocalStorageProjectStore CreateStore() => new(_localStorage);

        [Fact]
        public async Task A_browser_with_nothing_saved_has_no_projects()
        {
            var store = CreateStore();

            (await store.ListAsync()).ShouldBeEmpty();
            (await store.LoadAsync("abc123")).ShouldBeNull();
            (await store.GetLastOpenedAsync()).ShouldBeNull();
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
            loaded.Files.ShouldBe(Todo.Files);
        }

        [Fact]
        public async Task Saved_projects_are_listed_once_each_under_their_latest_name()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            await store.SaveAsync(new Project("def456", "Hello", ProjectKind.Program, "main.🍇", [new ProjectFile("main.🍇", "x")]));
            await store.SaveAsync(Todo with { Name = "Renamed" });

            (await CreateStore().ListAsync()).ShouldBe([new ProjectSummary("abc123", "Renamed"), new ProjectSummary("def456", "Hello")]);
        }

        [Fact]
        public async Task A_deleted_project_is_gone_from_the_list_and_from_storage()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            await store.SetLastOpenedAsync("abc123");

            await store.DeleteAsync("abc123");

            (await store.ListAsync()).ShouldBeEmpty();
            (await store.LoadAsync("abc123")).ShouldBeNull();
            (await store.GetLastOpenedAsync()).ShouldBeNull();
            _browser.Keys.ShouldBe(["blazemoji.projects"]);
        }

        [Fact]
        public async Task The_last_opened_project_is_remembered()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);

            await store.SetLastOpenedAsync("abc123");

            (await CreateStore().GetLastOpenedAsync()).ShouldBe("abc123");
        }

        [Fact]
        public async Task Everything_is_kept_under_this_apps_own_keys()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            await store.SetLastOpenedAsync("abc123");

            _browser.Keys.Order().ShouldBe(
            [
                "blazemoji.project.abc123",
                "blazemoji.project.abc123.file.app/main.🍇",
                "blazemoji.project.abc123.file.app/todos.🍇",
                "blazemoji.projects",
            ]);
        }

        [Fact]
        public async Task Each_file_is_kept_in_an_entry_of_its_own_as_the_text_it_is()
        {
            await CreateStore().SaveAsync(Todo);

            _browser["blazemoji.project.abc123.file.app/main.🍇"].ShouldBe("📦 grapevine 🏠\n🏁 🍇 🍉");
            _browser["blazemoji.project.abc123.file.app/todos.🍇"].ShouldBe("🐇 📒 🍇 🍉");
            _browser["blazemoji.project.abc123"].ShouldNotContain("grapevine");
        }

        [Fact]
        public async Task Nothing_read_back_in_one_piece_is_larger_than_the_largest_file()
        {
            // The page reads each entry back in one message, and a message has a size limit.
            // Emoji written as JSON escapes would be six times their size.
            var large = string.Concat(Enumerable.Repeat("🍇 🍉 😀 🔤hello🔤❗️\n", 600));
            var project = Todo with { Files = [new ProjectFile("app/main.🍇", large), new ProjectFile("app/todos.🍇", large)] };

            await CreateStore().SaveAsync(project);

            _browser.Values.Max(value => value.Length).ShouldBe(large.Length);
        }

        [Fact]
        public async Task Saving_after_one_file_changed_writes_that_file_and_nothing_else()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);
            _localStorage.ClearReceivedCalls();

            await store.SaveAsync(Todo with { Files = [Todo.Files[0], Todo.Files[1] with { Content = "changed" }] });

            await _localStorage.Received(1).SetItemAsStringAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            await _localStorage.Received(1).SetItemAsStringAsync("blazemoji.project.abc123.file.app/todos.🍇", "changed", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task The_same_project_open_twice_keeps_what_each_one_changed()
        {
            await CreateStore().SaveAsync(Todo);
            var first = CreateStore();
            var second = CreateStore();
            var inFirst = (await first.LoadAsync("abc123"))!;
            var inSecond = (await second.LoadAsync("abc123"))!;

            await first.SaveAsync(inFirst with { Files = [inFirst.Files[0] with { Content = "main, from the first" }, inFirst.Files[1]] });
            await second.SaveAsync(inSecond with { Files = [inSecond.Files[0], inSecond.Files[1] with { Content = "todos, from the second" }] });

            var loaded = (await CreateStore().LoadAsync("abc123"))!;
            loaded.Files.Select(file => file.Content).ShouldBe(["main, from the first", "todos, from the second"]);
        }

        [Fact]
        public async Task A_file_that_left_the_project_leaves_storage()
        {
            var store = CreateStore();
            await store.SaveAsync(Todo);

            await store.SaveAsync(Todo with { Files = [Todo.Files[0]] });

            _browser.Keys.ShouldNotContain("blazemoji.project.abc123.file.app/todos.🍇");
            (await CreateStore().LoadAsync("abc123"))!.Files.ShouldBe([Todo.Files[0]]);
        }

        [Fact]
        public async Task A_file_that_left_the_project_leaves_storage_even_when_this_page_never_loaded_the_project()
        {
            await CreateStore().SaveAsync(Todo);

            await CreateStore().SaveAsync(Todo with { Files = [Todo.Files[0]] });

            _browser.Keys.ShouldNotContain("blazemoji.project.abc123.file.app/todos.🍇");
        }

        [Fact]
        public async Task A_project_kept_the_earlier_way_with_its_files_inside_still_opens_and_is_moved_over_when_saved()
        {
            _browser["blazemoji.projects"] = """{"version":1,"lastOpened":"abc123","projects":[{"id":"abc123","name":"Todo API"}]}""";
            _browser["blazemoji.project.abc123"] =
                """{"id":"abc123","name":"Todo API","kind":"server","entry":"app/main.\uD83C\uDF47","files":[{"path":"app/main.\uD83C\uDF47","content":"\uD83C\uDFC1 old"},{"path":"app/todos.\uD83C\uDF47","content":"todos"}]}""";
            var store = CreateStore();

            var loaded = (await store.LoadAsync("abc123"))!;
            await store.SaveAsync(loaded);

            loaded.Files.ShouldBe([new ProjectFile("app/main.🍇", "🏁 old"), new ProjectFile("app/todos.🍇", "todos")]);
            _browser["blazemoji.project.abc123.file.app/main.🍇"].ShouldBe("🏁 old");
            _browser["blazemoji.project.abc123"].ShouldNotContain("old");
            (await CreateStore().LoadAsync("abc123"))!.Files.ShouldBe(loaded.Files);
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("[1,2,3]")]
        [InlineData("{\"projects\":\"nope\"}")]
        [InlineData("null")]
        public async Task An_index_that_cannot_be_read_counts_as_empty(string stored)
        {
            _browser["blazemoji.projects"] = stored;

            (await CreateStore().ListAsync()).ShouldBeEmpty();
            (await CreateStore().GetLastOpenedAsync()).ShouldBeNull();
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("{\"id\":\"abc123\"}")]
        [InlineData("null")]
        public async Task A_project_that_cannot_be_read_counts_as_missing(string stored)
        {
            _browser["blazemoji.project.abc123"] = stored;

            (await CreateStore().LoadAsync("abc123")).ShouldBeNull();
        }

        [Fact]
        public async Task A_browser_that_refuses_to_store_is_reported_as_a_store_failure()
        {
            _localStorage.SetItemAsStringAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Throws(new JSException("QuotaExceededError"));

            await Should.ThrowAsync<ProjectStoreException>(() => CreateStore().SaveAsync(Todo));
        }

        [Fact]
        public async Task A_browser_that_cannot_be_reached_is_reported_as_a_store_failure()
        {
            _localStorage.GetItemAsStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Throws(new InvalidOperationException("JavaScript interop calls cannot be issued at this time."));

            await Should.ThrowAsync<ProjectStoreException>(() => CreateStore().ListAsync());
            await Should.ThrowAsync<ProjectStoreException>(() => CreateStore().LoadAsync("abc123"));
        }
    }
}
