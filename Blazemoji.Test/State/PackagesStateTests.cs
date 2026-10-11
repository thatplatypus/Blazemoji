using System.Diagnostics;
using Blazemoji.Services.Packages;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Shared.State;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.State
{
    public sealed class PackagesStateTests
    {
        private const string Manifest = "the manifest";
        private const string Lockfile = "the lockfile";

        private static readonly LockedPackage Words = new("@thatplatypus/hello_words", "0.1.0", new string('a', 64));
        private static readonly LockedPackage Greeter = new("@thatplatypus/hello_greeter", "0.1.0", new string('b', 64));

        private static readonly ProjectTemplate Hello = new(
            "hello-world", "Hello World", "One file.", ProjectKind.Program, "main.🍇", [new ProjectFile("main.🍇", "🏁 🍇 🍉")]);

        private readonly IPackageManager _manager = Substitute.For<IPackageManager>();
        private readonly IToolchainPackages _toolchain = Substitute.For<IToolchainPackages>();
        private readonly IProjectStore _store = Substitute.For<IProjectStore>();
        private readonly IProjectTemplates _templates = Substitute.For<IProjectTemplates>();
        private readonly FakeTimeProvider _time = new();

        public PackagesStateTests()
        {
            _templates.All.Returns([Hello]);
        }

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static Project Stored(string id = "p1", string? manifest = Manifest, string? lockfile = Lockfile)
        {
            var files = new List<ProjectFile> { new("main.🍇", "🏁 🍇 🍉") };
            if (manifest is not null)
                files.Add(new ProjectFile(PackagesState.ManifestPath, manifest));
            if (lockfile is not null)
                files.Add(new ProjectFile(PackagesState.LockfilePath, lockfile));

            return new Project(id, "App " + id, ProjectKind.Program, "main.🍇", files);
        }

        private async Task<ProjectState> ProjectsAsync(params Project[] stored)
        {
            _store.ListAsync().Returns(stored.Select(project => new ProjectSummary(project.Id, project.Name)).ToList());
            _store.GetLastOpenedAsync().Returns(stored[0].Id);
            foreach (var project in stored)
                _store.LoadAsync(project.Id).Returns(project);

            var state = new ProjectState(_store, _templates, NullLogger<ProjectState>.Instance);
            await state.LoadAsync();
            return state;
        }

        private PackagesState State(ProjectState projects) =>
            new(_manager, _toolchain, projects, new PackageUploads(), _time, NullLogger<PackagesState>.Instance);

        private void Restores(params LockedPackage[] packages) =>
            _manager.RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new PackageRestore.Restored(packages));

        private void Builds(string id = "set-1") =>
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>())
                .Returns(new DependencyAnswer.Ready(id, ["hello_greeter", "hello_words"]));

        private static PackageProblem Problem(string code) => new(PackageProblem.Error, code, "The message.", "the reason", "the fix");

        /// <summary>Lets the state's own waits pass: each turn moves the clock on by one ask.</summary>
        private async Task<T> EventuallyAsync<T>(Task<T> work, TimeSpan? step = null)
        {
            var waited = Stopwatch.StartNew();
            while (!work.IsCompleted)
            {
                waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "the state never finished");
                _time.Advance(step ?? PackagesState.AskAgainAfter);
                await Task.Delay(5, Cancellation);
            }

            return await work;
        }

        private async Task EventuallyAsync(Task work, TimeSpan? step = null) =>
            await EventuallyAsync(Wrapped(work), step);

        private static async Task<bool> Wrapped(Task work)
        {
            await work;
            return true;
        }

        [Fact]
        public async Task A_project_with_no_manifest_asks_nothing_of_anyone()
        {
            var projects = await ProjectsAsync(Stored(manifest: null, lockfile: null));
            var state = State(projects);

            await state.EnsureRestoredAsync(projects.Current);
            var mayRun = await state.PrepareForRunAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.None);
            state.Dependencies.ShouldBeNull();
            state.AllowsACheck.ShouldBeTrue();
            mayRun.ShouldBeTrue();
            _manager.ReceivedCalls().ShouldBeEmpty();
            _toolchain.ReceivedCalls().ShouldBeEmpty();
        }

        [Fact]
        public async Task What_is_locked_is_fetched_and_the_toolchain_builds_the_set()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Greeter, Words);
            Builds("set-1");
            var seen = new List<PackagesStatus>();
            state.StateChanged += () => seen.Add(state.Status);

            await state.EnsureRestoredAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.Ready);
            state.Dependencies.ShouldBe("set-1");
            state.Problems.ShouldBeEmpty();
            state.AllowsACheck.ShouldBeTrue();
            seen.ShouldBe([PackagesStatus.Restoring, PackagesStatus.Ready]);
            await _manager.Received(1).RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>());
            await _toolchain.Received(1).PrepareAsync(new DependencyRequest(Manifest, Lockfile), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_pair_that_was_restored_is_not_restored_again_and_a_changed_one_is()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Greeter, Words);
            Builds();

            await state.EnsureRestoredAsync(projects.Current);
            await state.EnsureRestoredAsync(projects.Current);
            await projects.WriteFileAsync("p1", PackagesState.LockfilePath, "another lockfile");
            await state.EnsureRestoredAsync(projects.Current);

            await _manager.Received(1).RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>());
            await _manager.Received(1).RestoreAsync(Manifest, "another lockfile", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task The_archives_the_toolchain_lacks_are_handed_over_and_it_is_asked_again()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Greeter, Words);
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>())
                .Returns(new DependencyAnswer.Needed([Words]), new DependencyAnswer.Ready("set-1", ["hello_greeter", "hello_words"]));
            var archive = new MemoryStream([1, 2, 3]);
            _manager.OpenArchive(Words).Returns(archive);
            _toolchain.AddArchiveAsync(Words, archive, Arg.Any<CancellationToken>()).Returns(ArchiveOutcome.Stored);

            await state.EnsureRestoredAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.Ready);
            await _toolchain.Received(1).AddArchiveAsync(Words, archive, Arg.Any<CancellationToken>());
            await _toolchain.Received(2).PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>());
            _manager.DidNotReceive().OpenArchive(Greeter);
        }

        [Fact]
        public async Task An_archive_that_was_not_fetched_is_a_problem_naming_the_package_and_is_tried_again_next_time()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>()).Returns(new DependencyAnswer.Needed([Words]));
            _manager.OpenArchive(Words).Returns((Stream?)null);

            await state.EnsureRestoredAsync(projects.Current);
            await state.EnsureRestoredAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.Failed);
            state.Problems.ShouldHaveSingleItem().Message.ShouldContain("@thatplatypus/hello_words");
            state.AllowsACheck.ShouldBeFalse();
            await _manager.Received(2).RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task An_archive_the_toolchain_will_not_build_is_said_once_and_not_sent_again_for_the_same_files()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>()).Returns(new DependencyAnswer.Needed([Words]));
            _manager.OpenArchive(Words).Returns(_ => new MemoryStream([1]));
            _toolchain.AddArchiveAsync(Words, Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(ArchiveOutcome.NotAllowed);

            await state.EnsureRestoredAsync(projects.Current);
            await state.EnsureRestoredAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.Failed);
            state.Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.Scope);
            await _toolchain.Received(1).AddArchiveAsync(Words, Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_toolchain_that_keeps_asking_for_the_same_archives_is_given_up_on()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>()).Returns(new DependencyAnswer.Needed([Words]));
            _manager.OpenArchive(Words).Returns(_ => new MemoryStream([1]));
            _toolchain.AddArchiveAsync(Words, Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(ArchiveOutcome.Stored);

            await state.EnsureRestoredAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.Failed);
            state.Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.Unreadable);
            await _toolchain.Received(3).PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_toolchain_that_says_to_ask_again_is_asked_again_after_a_pause()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>())
                .Returns(new DependencyAnswer.Busy(), new DependencyAnswer.Busy(), new DependencyAnswer.Ready("set-1", ["hello_words"]));

            await EventuallyAsync(state.EnsureRestoredAsync(projects.Current));

            state.Status.ShouldBe(PackagesStatus.Ready);
            await _toolchain.Received(3).PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_set_that_is_never_ready_stops_being_waited_for_and_is_tried_again_next_time()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>()).Returns(new DependencyAnswer.Busy());

            await EventuallyAsync(state.EnsureRestoredAsync(projects.Current), step: TimeSpan.FromMinutes(1));

            state.Status.ShouldBe(PackagesStatus.Failed);
            state.Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.TimedOut);
            Builds();
            await state.EnsureRestoredAsync(projects.Current);
            state.Status.ShouldBe(PackagesStatus.Ready);
        }

        [Fact]
        public async Task A_lockfile_that_is_out_of_date_is_one_problem_saying_to_run_and_nothing_is_resolved_or_built()
        {
            var projects = await ProjectsAsync(Stored(lockfile: null));
            var state = State(projects);
            _manager.RestoreAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());

            await state.EnsureRestoredAsync(projects.Current);
            await state.EnsureRestoredAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.OutOfDate);
            var problem = state.Problems.ShouldHaveSingleItem();
            problem.Code.ShouldBe(PmjProblems.LockOutOfDate);
            problem.Fix.ShouldContain("run the project");
            state.AllowsACheck.ShouldBeFalse();
            state.ProblemsAreFromARun.ShouldBeFalse();
            await _manager.Received(1).RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
            await _manager.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
            _toolchain.ReceivedCalls().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_copy_with_no_pmj_says_one_thing_and_lets_the_check_and_the_run_go_on_as_before()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            _manager.RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new PackageRestore.Failed([PmjProblems.NoPackmoji("This copy of Blazemoji")]));

            await state.EnsureRestoredAsync(projects.Current);
            var mayRun = await state.PrepareForRunAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.Unavailable);
            state.Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.NoPackmoji);
            state.AllowsACheck.ShouldBeTrue();
            mayRun.ShouldBeTrue();
            state.Dependencies.ShouldBeNull();
        }

        [Fact]
        public async Task A_lockfile_that_locks_nothing_needs_no_set()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores();

            await state.EnsureRestoredAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.Ready);
            state.Dependencies.ShouldBeNull();
            _toolchain.ReceivedCalls().ShouldBeEmpty();
        }

        [Fact]
        public async Task What_is_wrong_with_the_two_files_is_shown_and_not_asked_about_again_until_they_change()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            var problem = Problem("json.syntax") with { File = "packmoji.json", Line = 3, Column = 5 };
            _manager.RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new PackageRestore.Failed([problem]));

            await state.EnsureRestoredAsync(projects.Current);
            await state.EnsureRestoredAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.Failed);
            state.Problems.ShouldBe([problem]);
            await _manager.Received(1).RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(PackageProblemCodes.Busy)]
        [InlineData(PackageProblemCodes.TimedOut)]
        [InlineData(PackageProblemCodes.Unreadable)]
        [InlineData("github.unreachable")]
        public async Task A_failure_that_is_about_the_moment_and_not_the_files_is_asked_about_again(string code)
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            _manager.RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new PackageRestore.Failed([Problem(code)]));

            await state.EnsureRestoredAsync(projects.Current);
            await state.EnsureRestoredAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.Failed);
            await _manager.Received(2).RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task What_the_toolchain_finds_wrong_is_shown_and_a_toolchain_that_cannot_be_reached_is_tried_again()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            var problem = Problem("build.compile-failed");
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>())
                .Returns(new DependencyAnswer.Unavailable(), new DependencyAnswer.Failed([problem]));

            await state.EnsureRestoredAsync(projects.Current);
            var unreachable = state.Problems.ShouldHaveSingleItem().Code;
            await state.EnsureRestoredAsync(projects.Current);

            unreachable.ShouldBe(PackageProblemCodes.Toolchain);
            state.Status.ShouldBe(PackagesStatus.Failed);
            state.Problems.ShouldBe([problem]);
        }

        [Fact]
        public async Task The_same_files_asked_about_while_they_are_being_restored_share_the_one_restore()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            var restoring = new TaskCompletionSource<PackageRestore>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(restoring.Task);
            Builds();

            var first = state.EnsureRestoredAsync(projects.Current);
            var second = state.EnsureRestoredAsync(projects.Current);
            state.Status.ShouldBe(PackagesStatus.Restoring);
            state.AllowsACheck.ShouldBeFalse();
            restoring.SetResult(new PackageRestore.Restored([Words]));
            await first.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            await second.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            state.Status.ShouldBe(PackagesStatus.Ready);
            await _manager.Received(1).RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task An_older_restore_that_ends_later_does_not_replace_a_newer_ones_answer()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            var older = new TaskCompletionSource<PackageRestore>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(older.Task);
            _manager.RestoreAsync("a newer manifest", Lockfile, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());

            var first = state.EnsureRestoredAsync(projects.Current);
            await projects.WriteFileAsync("p1", PackagesState.ManifestPath, "a newer manifest");
            await state.EnsureRestoredAsync(projects.Current);
            older.SetResult(new PackageRestore.Failed([Problem("json.syntax")]));
            await first.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            state.Status.ShouldBe(PackagesStatus.OutOfDate);
            state.Problems.ShouldHaveSingleItem().Code.ShouldBe(PmjProblems.LockOutOfDate);
            await state.EnsureRestoredAsync(projects.Current);
            await _manager.Received(1).RestoreAsync("a newer manifest", Lockfile, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_run_of_a_project_whose_packages_are_ready_goes_ahead_and_resolves_nothing()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            Builds();

            (await state.PrepareForRunAsync(projects.Current)).ShouldBeTrue();

            state.Dependencies.ShouldBe("set-1");
            await _manager.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_run_resolves_an_out_of_date_lockfile_writes_it_into_the_project_and_restores()
        {
            var projects = await ProjectsAsync(Stored(lockfile: null));
            var state = State(projects);
            _manager.RestoreAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            _manager.ResolveAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(new PackageResolve.Resolved("the lockfile pmj wrote", [Words]));
            _manager.RestoreAsync(Manifest, "the lockfile pmj wrote", Arg.Any<CancellationToken>()).Returns(new PackageRestore.Restored([Words]));
            Builds("set-9");

            var mayRun = await state.PrepareForRunAsync(projects.Current);

            mayRun.ShouldBeTrue();
            state.Status.ShouldBe(PackagesStatus.Ready);
            state.Dependencies.ShouldBe("set-9");
            projects.Current.Find(PackagesState.LockfilePath).ShouldNotBeNull().Content.ShouldBe("the lockfile pmj wrote");
            await _store.Received().SaveAsync(Arg.Is<Project>(saved => saved.Find(PackagesState.LockfilePath) != null));
            await _toolchain.Received(1).PrepareAsync(new DependencyRequest(Manifest, "the lockfile pmj wrote"), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_run_whose_packages_cannot_be_resolved_does_not_go_ahead_and_its_problems_are_a_runs()
        {
            var projects = await ProjectsAsync(Stored(lockfile: null));
            var state = State(projects);
            _manager.RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            var refusal = Problem("scope.not-allowed");
            _manager.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new PackageResolve.Failed([refusal]));

            var mayRun = await state.PrepareForRunAsync(projects.Current);

            mayRun.ShouldBeFalse();
            state.Status.ShouldBe(PackagesStatus.Failed);
            state.Problems.ShouldBe([refusal]);
            state.ProblemsAreFromARun.ShouldBeTrue();
            projects.Current.Find(PackagesState.LockfilePath).ShouldBeNull();
        }

        [Fact]
        public async Task A_lockfile_resolved_for_a_project_that_is_no_longer_open_is_not_written_anywhere()
        {
            var first = Stored("p1", lockfile: null);
            var second = Stored("p2", manifest: null, lockfile: null);
            var projects = await ProjectsAsync(first, second);
            var state = State(projects);
            _manager.RestoreAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            var resolving = new TaskCompletionSource<PackageResolve>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.ResolveAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(resolving.Task);

            var preparing = state.PrepareForRunAsync(projects.Current);
            await projects.OpenAsync("p2");
            resolving.SetResult(new PackageResolve.Resolved("for the first project", [Words]));
            var mayRun = await preparing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            mayRun.ShouldBeFalse();
            projects.Current.Id.ShouldBe("p2");
            projects.Current.Find(PackagesState.LockfilePath).ShouldBeNull();
            await _store.DidNotReceive().SaveAsync(Arg.Is<Project>(saved => saved.Find(PackagesState.LockfilePath) != null));
            state.Status.ShouldBe(PackagesStatus.None);
            state.ProblemsAreFromARun.ShouldBeFalse();
        }

        [Fact]
        public async Task A_run_tries_again_what_failed_before_even_for_the_same_files()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            _manager.RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new PackageRestore.Failed([Problem("release.invalid")]), new PackageRestore.Restored([Words]));
            Builds();

            await state.EnsureRestoredAsync(projects.Current);
            await state.EnsureRestoredAsync(projects.Current);
            state.Status.ShouldBe(PackagesStatus.Failed);
            var mayRun = await state.PrepareForRunAsync(projects.Current);

            mayRun.ShouldBeTrue();
            state.Status.ShouldBe(PackagesStatus.Ready);
        }

        [Fact]
        public async Task A_set_the_toolchain_has_lost_is_restored_again_for_the_same_files_when_a_run_asks()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>())
                .Returns(new DependencyAnswer.Ready("set-1", ["hello_words"]), new DependencyAnswer.Ready("set-2", ["hello_words"]));
            await state.EnsureRestoredAsync(projects.Current);

            var again = await state.RestoreAgainAsync(Cancellation);

            again.ShouldBe("set-2");
            state.Dependencies.ShouldBe("set-2");
            await _manager.Received(2).RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Restoring_again_gives_nothing_when_the_packages_cannot_be_made_ready()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>()).Returns(new DependencyAnswer.Unavailable());

            (await state.RestoreAgainAsync(Cancellation)).ShouldBeNull();
        }

        [Fact]
        public async Task Something_going_wrong_that_nobody_foresaw_is_one_fixed_problem_and_is_not_thrown()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            _manager.RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("a secret of the server's"));

            await state.EnsureRestoredAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.Failed);
            var problem = state.Problems.ShouldHaveSingleItem();
            problem.Code.ShouldBe(PackageProblemCodes.Unreadable);
            (problem.Message + problem.Reason + problem.Fix).ShouldNotContain("secret");
        }

        [Fact]
        public async Task A_state_that_has_gone_away_ends_what_it_was_doing_and_says_nothing_more()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            _manager.RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(call => WhenCancelled(call.Arg<CancellationToken>()));
            var restoring = state.EnsureRestoredAsync(projects.Current);
            var changes = 0;
            state.StateChanged += () => changes++;

            await state.DisposeAsync();
            await restoring.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            await state.EnsureRestoredAsync(projects.Current);

            changes.ShouldBe(0);
        }

        private static async Task<PackageRestore> WhenCancelled(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new PackageRestore.OutOfDate();
        }

        [Fact]
        public void No_more_than_two_archives_are_on_their_way_from_one_host_at_once()
        {
            var uploads = new PackageUploads();

            uploads.TryBegin().ShouldBeTrue();
            uploads.TryBegin().ShouldBeTrue();
            uploads.TryBegin().ShouldBeFalse();
            uploads.End();
            uploads.TryBegin().ShouldBeTrue();
        }
    }
}
