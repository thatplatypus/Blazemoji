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

            _browser.Keys.Order().ShouldBe(["blazemoji.project.abc123", "blazemoji.projects"]);
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
