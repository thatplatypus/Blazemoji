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

        private static string LockOf(ProjectState projects) => projects.Current.Find(PackagesState.LockfilePath)?.Content ?? "(none)";

        private int RestoreCalls() => _manager.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IPackageManager.RestoreAsync));

        private int ResolveCalls() => _manager.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IPackageManager.ResolveAsync));

        private int ToolchainAsks() => _toolchain.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IToolchainPackages.PrepareAsync));

        private async Task UntilAsync(Func<bool> reached, string what)
        {
            for (var turn = 0; turn < 400 && !reached(); turn++)
                await Task.Delay(5, Cancellation);

            reached().ShouldBeTrue(what);
        }

        private static async Task<string> EndsAsync(Task task)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
                return "ended";
            }
            catch (TimeoutException)
            {
                return "NEVER ENDED";
            }
            catch (Exception exception)
            {
                return "THREW " + exception.GetType().Name;
            }
        }

        private static readonly CompileResult SetNotHeld = new(
            false,
            [new Diagnostic(DiagnosticSeverity.Error, string.Empty, 0, 0, "The project's packages are not built on the toolchain service.")],
            null,
            ReachedCompiler: false,
            DependenciesMissing: true);

        private static async Task<string> GotAsync(Task<string?> task)
        {
            try
            {
                return await task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation) ?? "(nothing)";
            }
            catch (TimeoutException)
            {
                return "NEVER ENDED";
            }
        }

        /// <summary>A pmj that ends inside the call that tells it to stop: a plain source, completed from the token's own callback.</summary>
        private static Task<PackageRestore> EndsInsideCancel(CancellationToken token)
        {
            var source = new TaskCompletionSource<PackageRestore>();
            token.Register(() => source.TrySetCanceled(token));
            return source.Task;
        }

        private static async Task<bool?> AnswerOf(Task<bool> task) => task.IsCompletedSuccessfully ? await task : null;

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

            var again = await state.RestoreAgainAsync("set-1", Cancellation);

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
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>())
                .Returns(new DependencyAnswer.Ready("set-1", ["hello_words"]), new DependencyAnswer.Unavailable());
            await state.EnsureRestoredAsync(projects.Current);

            (await state.RestoreAgainAsync("set-1", Cancellation)).ShouldBeNull();
            await _manager.Received(2).RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Restoring_again_gives_nothing_for_a_set_that_is_not_the_open_project_s()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            Builds("set-1");
            await state.EnsureRestoredAsync(projects.Current);

            (await state.RestoreAgainAsync("another-set", Cancellation)).ShouldBeNull();

            RestoreCalls().ShouldBe(1);
        }

        // A compile of one project is on its way when the toolchain turns out to have lost
        // its set, and the person has opened another project meanwhile. What the run state
        // would compile the first project's files against must not be the other's set.
        [Fact]
        public async Task Restoring_again_for_a_compile_does_not_give_the_set_of_another_project_opened_since()
        {
            var first = Stored("p1");
            var second = Stored("p2", manifest: "manifest of p2", lockfile: "lockfile of p2");
            var projects = await ProjectsAsync(first, second);
            var state = State(projects);
            Restores(Words);
            _toolchain.PrepareAsync(Arg.Is<DependencyRequest>(request => request.Manifest == Manifest), Arg.Any<CancellationToken>())
                .Returns(new DependencyAnswer.Ready("set-of-p1", ["hello_words"]));
            _toolchain.PrepareAsync(Arg.Is<DependencyRequest>(request => request.Manifest == "manifest of p2"), Arg.Any<CancellationToken>())
                .Returns(new DependencyAnswer.Ready("set-of-p2", ["hello_words"]));

            await state.EnsureRestoredAsync(projects.Current);
            state.Dependencies.ShouldBe("set-of-p1");

            await projects.OpenAsync("p2");
            var again = await state.RestoreAgainAsync("set-of-p1", Cancellation);

            again.ShouldBeNull("the compile that asked was of p1, and what came back is the set of p2");
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
        public async Task Going_back_to_text_that_was_ready_after_other_text_failed_for_the_moment_is_ready_again()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            _manager.RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(new PackageRestore.Restored([Words]));
            _manager.RestoreAsync("b", Lockfile, Arg.Any<CancellationToken>()).Returns(new PackageRestore.Failed([Problem(PackageProblemCodes.Busy)]));
            Builds("set-A");

            await state.EnsureRestoredAsync(projects.Current);
            state.Status.ShouldBe(PackagesStatus.Ready);
            await projects.WriteFileAsync("p1", PackagesState.ManifestPath, "b");
            await state.EnsureRestoredAsync(projects.Current);
            state.Status.ShouldBe(PackagesStatus.Failed);
            await projects.WriteFileAsync("p1", PackagesState.ManifestPath, Manifest);
            await state.EnsureRestoredAsync(projects.Current);
            await state.EnsureRestoredAsync(projects.Current);

            state.Status.ShouldBe(PackagesStatus.Ready, "the project's text is the pair that was ready");
            state.Dependencies.ShouldBe("set-A");
        }

        [Fact]
        public async Task Going_back_to_text_that_was_ready_while_other_text_is_still_restoring_ends_ready()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            _manager.RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(new PackageRestore.Restored([Words]));
            var other = new TaskCompletionSource<PackageRestore>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.RestoreAsync("b", Lockfile, Arg.Any<CancellationToken>()).Returns(other.Task);
            Builds("set-A");

            await state.EnsureRestoredAsync(projects.Current);
            await projects.WriteFileAsync("p1", PackagesState.ManifestPath, "b");
            var restoringB = state.EnsureRestoredAsync(projects.Current);
            await projects.WriteFileAsync("p1", PackagesState.ManifestPath, Manifest);
            var backToA = state.EnsureRestoredAsync(projects.Current);
            other.SetResult(new PackageRestore.Failed([Problem("json.syntax")]));
            await restoringB.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            await backToA.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            state.Status.ShouldBe(PackagesStatus.Ready, "the project's text is the pair that was ready");
            state.Problems.ShouldBeEmpty();
            state.Dependencies.ShouldBe("set-A");
        }

        [Fact]
        public async Task A_run_waiting_on_an_older_restore_does_not_resolve_the_old_text_when_newer_text_is_out_of_date()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            var restoringA = new TaskCompletionSource<PackageRestore>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(restoringA.Task);
            _manager.RestoreAsync("b", Lockfile, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            _manager.ResolveAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(new PackageResolve.Resolved(Lockfile, [Words]));
            _manager.ResolveAsync("b", Lockfile, Arg.Any<CancellationToken>()).Returns(new PackageResolve.Resolved("lock for b", [Words]));
            _manager.RestoreAsync("b", "lock for b", Arg.Any<CancellationToken>()).Returns(new PackageRestore.Restored([Words]));
            Builds("set-B");

            var preparing = state.PrepareForRunAsync(projects.Current);
            await projects.WriteFileAsync("p1", PackagesState.ManifestPath, "b");
            await state.EnsureRestoredAsync(projects.Current);
            state.Status.ShouldBe(PackagesStatus.OutOfDate);
            restoringA.SetResult(new PackageRestore.Restored([Words]));
            var firstRun = await preparing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            var statusAfterFirstRun = state.Status;
            var secondRun = await state.PrepareForRunAsync(projects.Current).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            await state.EnsureRestoredAsync(projects.Current);

            var resolvedTheOldText = _manager.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IPackageManager.ResolveAsync) && (string)call.GetArguments()[0]! == Manifest);
            var summary = $"first run {firstRun}, status after it {statusAfterFirstRun}, second run {secondRun}, status now {state.Status}, resolves of the old manifest {resolvedTheOldText}";
            statusAfterFirstRun.ShouldNotBe(PackagesStatus.Restoring, summary);
            secondRun.ShouldBeTrue(summary);
            resolvedTheOldText.ShouldBe(0, summary);
        }

        [Fact]
        public async Task A_run_waiting_on_a_restore_does_not_resolve_a_project_that_was_left_for_another_out_of_date_one()
        {
            var first = Stored("p1");
            var second = Stored("p2", manifest: "manifest of p2", lockfile: null);
            var projects = await ProjectsAsync(first, second);
            var state = State(projects);
            var restoringFirst = new TaskCompletionSource<PackageRestore>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(restoringFirst.Task);
            _manager.RestoreAsync("manifest of p2", null, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            _manager.ResolveAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(new PackageResolve.Resolved("a lock for p1", [Words]));
            _manager.ResolveAsync("manifest of p2", null, Arg.Any<CancellationToken>()).Returns(new PackageResolve.Resolved("a lock for p2", [Words]));
            _manager.RestoreAsync("manifest of p2", "a lock for p2", Arg.Any<CancellationToken>()).Returns(new PackageRestore.Restored([Words]));
            Builds("set-p2");

            var preparing = state.PrepareForRunAsync(projects.Current);
            await projects.OpenAsync("p2");
            await state.EnsureRestoredAsync(projects.Current);
            state.Status.ShouldBe(PackagesStatus.OutOfDate);
            restoringFirst.SetResult(new PackageRestore.Restored([Words]));
            var firstRun = await preparing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            var statusAfterFirstRun = state.Status;
            await state.EnsureRestoredAsync(projects.Current);
            var statusAfterAPause = state.Status;
            var secondRun = await state.PrepareForRunAsync(projects.Current).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            var resolvedTheProjectLeft = _manager.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IPackageManager.ResolveAsync) && (string)call.GetArguments()[0]! == Manifest);
            var summary = $"first run {firstRun}, status after it {statusAfterFirstRun}, after a typing pause {statusAfterAPause}, run of p2 {secondRun}, status now {state.Status}, resolves of p1 after it was left {resolvedTheProjectLeft}";
            firstRun.ShouldBeFalse(summary);
            projects.Current.Find(PackagesState.LockfilePath).ShouldNotBeNull(summary).Content.ShouldBe("a lock for p2", summary);
            statusAfterFirstRun.ShouldBe(PackagesStatus.OutOfDate, summary);
            secondRun.ShouldBeTrue(summary);
            resolvedTheProjectLeft.ShouldBe(0, summary);
        }

        [Fact]
        public async Task A_resolve_that_ends_after_a_project_with_the_same_two_texts_was_opened_does_not_leave_the_status_restoring()
        {
            var first = Stored("p1", lockfile: null);
            var second = Stored("p2", lockfile: null);
            var projects = await ProjectsAsync(first, second);
            var state = State(projects);
            _manager.RestoreAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            var resolving = new TaskCompletionSource<PackageResolve>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.ResolveAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(resolving.Task, Task.FromResult<PackageResolve>(new PackageResolve.Resolved("a lock", [Words])));
            _manager.RestoreAsync(Manifest, "a lock", Arg.Any<CancellationToken>()).Returns(new PackageRestore.Restored([Words]));
            Builds("set-1");

            var preparing = state.PrepareForRunAsync(projects.Current);
            await projects.OpenAsync("p2");
            await state.EnsureRestoredAsync(projects.Current);
            resolving.SetResult(new PackageResolve.Resolved("a lock", [Words]));
            var firstRun = await preparing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            var statusAfterFirstRun = state.Status;
            await state.EnsureRestoredAsync(projects.Current);
            var statusAfterAPause = state.Status;
            var secondRun = await state.PrepareForRunAsync(projects.Current).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            var summary = $"first run {firstRun}, status after it {statusAfterFirstRun}, after a typing pause {statusAfterAPause}, run of p2 {secondRun}, status now {state.Status}";
            firstRun.ShouldBeFalse(summary);
            statusAfterFirstRun.ShouldNotBe(PackagesStatus.Restoring, summary);
            secondRun.ShouldBeTrue(summary);
        }

        [Fact]
        public async Task A_resolve_that_throws_is_not_remembered_and_the_next_typing_pause_restores_again()
        {
            var projects = await ProjectsAsync(Stored(lockfile: null));
            var state = State(projects);
            _manager.RestoreAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            _manager.ResolveAsync(Manifest, null, Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("a secret of the server's"));

            var mayRun = await state.PrepareForRunAsync(projects.Current);
            var statusAfterRun = state.Status;
            await state.EnsureRestoredAsync(projects.Current);

            var restores = _manager.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IPackageManager.RestoreAsync));
            var summary = $"run {mayRun}, status after it {statusAfterRun}, status after a typing pause {state.Status}, restores {restores}, from a run {state.ProblemsAreFromARun}";
            mayRun.ShouldBeFalse(summary);
            statusAfterRun.ShouldBe(PackagesStatus.Failed, summary);
            restores.ShouldBe(2, summary);
        }

        [Fact]
        public async Task A_lockfile_chosen_for_text_that_is_no_longer_the_projects_is_not_written()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            var restoringA = new TaskCompletionSource<PackageRestore>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(restoringA.Task);
            _manager.RestoreAsync("b", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            _manager.ResolveAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(new PackageResolve.Resolved("chosen for the old manifest", [Words]));
            Builds("set-A");

            var preparing = state.PrepareForRunAsync(projects.Current);
            await projects.WriteFileAsync("p1", PackagesState.ManifestPath, "b");
            await state.EnsureRestoredAsync(projects.Current);
            restoringA.SetResult(new PackageRestore.Restored([Words]));
            var mayRun = await preparing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            var summary = $"run {mayRun}, status {state.Status}, manifest now '{projects.Current.Find(PackagesState.ManifestPath)!.Content}', lockfile now '{projects.Current.Find(PackagesState.LockfilePath)!.Content}', from a run {state.ProblemsAreFromARun}";
            projects.Current.Find(PackagesState.LockfilePath)!.Content.ShouldBe(Lockfile, summary);
        }

        [Fact]
        public async Task A_run_whose_restore_was_overtaken_waits_for_the_newer_one()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            var restoringA = new TaskCompletionSource<PackageRestore>(TaskCreationOptions.RunContinuationsAsynchronously);
            var restoringB = new TaskCompletionSource<PackageRestore>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(restoringA.Task);
            _manager.RestoreAsync("b", Lockfile, Arg.Any<CancellationToken>()).Returns(restoringB.Task);
            Builds("set-B");

            var preparing = state.PrepareForRunAsync(projects.Current);
            await projects.WriteFileAsync("p1", PackagesState.ManifestPath, "b");
            var newer = state.EnsureRestoredAsync(projects.Current);
            restoringA.SetResult(new PackageRestore.Restored([Words]));
            restoringB.SetResult(new PackageRestore.Restored([Words]));
            var mayRun = await preparing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            await newer.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            mayRun.ShouldBeTrue();
            state.Status.ShouldBe(PackagesStatus.Ready);
            state.Dependencies.ShouldBe("set-B");
            await _manager.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_run_gives_up_and_says_nothing_when_the_manifest_goes_on_changing_while_it_waits()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            var changes = 0;
            _manager.RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                // Someone changes the manifest while pmj works, every time.
                await projects.WriteFileAsync("p1", PackagesState.ManifestPath, "changed " + ++changes);
                return (PackageRestore)new PackageRestore.Restored([]);
            });

            var mayRun = await state.PrepareForRunAsync(projects.Current).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            mayRun.ShouldBeFalse();
            changes.ShouldBe(3);
            state.ProblemsAreFromARun.ShouldBeFalse();
            await _manager.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
            _toolchain.ReceivedCalls().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_typing_pause_for_the_same_two_texts_while_a_run_is_resolving_joins_it_and_the_run_goes_ahead()
        {
            var projects = await ProjectsAsync(Stored(lockfile: null));
            var state = State(projects);
            _manager.RestoreAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            var resolving = new TaskCompletionSource<PackageResolve>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.ResolveAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(resolving.Task);
            _manager.RestoreAsync(Manifest, "a lock", Arg.Any<CancellationToken>()).Returns(new PackageRestore.Restored([Words]));
            Builds("set-1");

            await state.EnsureRestoredAsync(projects.Current).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            state.Status.ShouldBe(PackagesStatus.OutOfDate);
            var preparing = state.PrepareForRunAsync(projects.Current);
            var statusWhileResolving = state.Status;
            // The pause joins the resolve, so it cannot end before the resolve is let end.
            var pause = state.EnsureRestoredAsync(projects.Current);
            var statusAfterThePause = state.Status;
            resolving.SetResult(new PackageResolve.Resolved("a lock", [Words]));
            var mayRun = await preparing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            await pause.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            var summary = $"while resolving {statusWhileResolving}, after the pause {statusAfterThePause}, run {mayRun}, status {state.Status}, "
                + $"problems [{string.Join(", ", state.Problems.Select(problem => problem.Code))}], from a run {state.ProblemsAreFromARun}, "
                + $"lockfile now '{LockOf(projects)}', restores {RestoreCalls()}, resolves {ResolveCalls()}";
            mayRun.ShouldBeTrue(summary);
            LockOf(projects).ShouldBe("a lock", summary);
            await _manager.Received(1).ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
            await _manager.Received(1).RestoreAsync(Manifest, null, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_project_opened_while_another_with_the_same_two_texts_is_resolving_does_not_wait_for_its_resolve()
        {
            var first = Stored("p1", lockfile: null);
            var second = Stored("p2", lockfile: null);
            var projects = await ProjectsAsync(first, second);
            var state = State(projects);
            _manager.RestoreAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            var resolving = new TaskCompletionSource<PackageResolve>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.ResolveAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(resolving.Task);

            var preparing = state.PrepareForRunAsync(projects.Current);
            var restoresBefore = RestoreCalls();
            await projects.OpenAsync("p2").WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            await state.EnsureRestoredAsync(projects.Current).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            var restoresAfter = RestoreCalls();
            resolving.SetResult(new PackageResolve.Resolved("a lock", [Words]));
            await preparing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            restoresAfter.ShouldBe(restoresBefore + 1, "the second project is restored for itself");
            projects.Current.Id.ShouldBe("p2");
            projects.Current.Find(PackagesState.LockfilePath).ShouldBeNull();
            await projects.OpenAsync("p1").WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            projects.Current.Find(PackagesState.LockfilePath).ShouldBeNull();
        }

        [Fact]
        public async Task A_typing_pause_whose_restore_is_still_going_when_a_resolve_of_the_same_two_texts_ends_does_not_cost_the_run()
        {
            var projects = await ProjectsAsync(Stored(lockfile: null));
            var state = State(projects);
            var slow = new TaskCompletionSource<PackageRestore>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.RestoreAsync(Manifest, null, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<PackageRestore>(new PackageRestore.OutOfDate()), slow.Task);
            var resolving = new TaskCompletionSource<PackageResolve>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.ResolveAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(resolving.Task);
            _manager.RestoreAsync(Manifest, "a lock", Arg.Any<CancellationToken>()).Returns(new PackageRestore.Restored([Words]));
            Builds("set-1");

            await state.EnsureRestoredAsync(projects.Current).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            var preparing = state.PrepareForRunAsync(projects.Current);
            var pause = state.EnsureRestoredAsync(projects.Current);
            resolving.SetResult(new PackageResolve.Resolved("a lock", [Words]));
            var mayRun = await preparing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            var summary = $"run {mayRun}, status as the run returned {state.Status}, problems {state.Problems.Count}, from a run {state.ProblemsAreFromARun}, lockfile '{LockOf(projects)}'";
            slow.SetResult(new PackageRestore.OutOfDate());
            await pause.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            summary += $"; once the pause's restore ended: status {state.Status}, from a run {state.ProblemsAreFromARun}, restores {RestoreCalls()}, resolves {ResolveCalls()}";

            mayRun.ShouldBeTrue(summary);
        }

        [Fact]
        public async Task A_manifest_changed_while_pmj_was_choosing_and_not_yet_asked_about_gets_no_lockfile_written()
        {
            var projects = await ProjectsAsync(Stored(lockfile: null));
            var state = State(projects);
            _manager.RestoreAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            _manager.RestoreAsync("b", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            var resolving = new TaskCompletionSource<PackageResolve>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.ResolveAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(resolving.Task);

            var preparing = state.PrepareForRunAsync(projects.Current);
            await projects.WriteFileAsync("p1", PackagesState.ManifestPath, "b").WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            resolving.SetResult(new PackageResolve.Resolved("chosen for the old manifest", [Words]));
            var mayRun = await preparing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            var summary = $"run {mayRun}, status {state.Status}, from a run {state.ProblemsAreFromARun}, lockfile now '{LockOf(projects)}', restores {RestoreCalls()}, resolves {ResolveCalls()}";
            LockOf(projects).ShouldBe("(none)", summary);
            mayRun.ShouldBeFalse(summary);
            state.Status.ShouldNotBe(PackagesStatus.Restoring, summary);
        }

        // Each pause of typing begins a restore for the text as it then is, and the ones that
        // newer text overtook have nobody to answer to.
        [Fact]
        public async Task A_restore_that_newer_text_overtook_stops_asking_the_toolchain()
        {
            const int Edits = 20;
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>()).Returns(new DependencyAnswer.Busy());

            var restores = new List<Task>();
            for (var edit = 0; edit < Edits; edit++)
            {
                await projects.WriteFileAsync("p1", PackagesState.ManifestPath, "manifest " + edit);
                restores.Add(state.EnsureRestoredAsync(projects.Current));
            }

            await UntilAsync(() => ToolchainAsks() >= Edits, "each restore asks the toolchain once to begin with");
            var askedAtFirst = ToolchainAsks();

            // Only the restore of the text that is open has a reason to ask again.
            _time.Advance(PackagesState.AskAgainAfter);
            await Task.Delay(300, Cancellation);
            var askedAfterOneWait = ToolchainAsks() - askedAtFirst;
            var stillGoing = restores.Count(restore => !restore.IsCompleted);

            await state.DisposeAsync();
            await Task.WhenAll(restores).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            var summary = $"asks at first {askedAtFirst}, asks after one wait {askedAfterOneWait}, restores still going {stillGoing} of {Edits}";
            askedAfterOneWait.ShouldBe(1, summary);
            stillGoing.ShouldBe(1, summary);
        }

        // The host runs only so many pmj commands at once for everyone, so one person typing
        // must not hold every place.
        [Fact]
        public async Task A_restore_that_newer_text_overtook_is_told_to_stop_its_pmj()
        {
            const int Edits = 5;
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            var told = new List<CancellationToken>();
            _manager.RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var token = call.Arg<CancellationToken>();
                lock (told)
                    told.Add(token);

                return WhenCancelled(token);
            });

            var restores = new List<Task>();
            for (var edit = 0; edit < Edits; edit++)
            {
                await projects.WriteFileAsync("p1", PackagesState.ManifestPath, "manifest " + edit);
                restores.Add(state.EnsureRestoredAsync(projects.Current));
            }

            await UntilAsync(() => { lock (told) return told.Count == Edits; }, "pmj is asked once for each text");
            await Task.Delay(100, Cancellation);
            int stopped;
            lock (told)
                stopped = told.Take(Edits - 1).Count(token => token.IsCancellationRequested);

            await state.DisposeAsync();
            await Task.WhenAll(restores).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            stopped.ShouldBe(Edits - 1, $"of the {Edits - 1} pmj commands whose text is no longer the project's, {stopped} were told to stop");
        }

        // pmj chooses at once, and the restore of the new pair is slow. A typing pause while
        // it is going on joins that restore and does not overtake it, so the run still goes ahead.
        [Fact]
        public async Task A_typing_pause_during_the_restore_that_follows_a_resolve_which_answered_at_once_does_not_cost_the_run()
        {
            var projects = await ProjectsAsync(Stored(lockfile: null));
            var state = State(projects);
            _manager.RestoreAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            _manager.ResolveAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(new PackageResolve.Resolved("a lock", [Words]));
            var restoringNew = new TaskCompletionSource<PackageRestore>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.RestoreAsync(Manifest, "a lock", Arg.Any<CancellationToken>()).Returns(restoringNew.Task);
            Builds("set-1");

            var preparing = state.PrepareForRunAsync(projects.Current);
            var pause = state.EnsureRestoredAsync(projects.Current);
            var restoresAfterThePause = RestoreCalls();
            restoringNew.SetResult(new PackageRestore.Restored([Words]));
            var run = await EndsAsync(preparing);
            var paused = await EndsAsync(pause);

            var mayRun = await AnswerOf(preparing);
            var summary = $"run {run} {mayRun}, pause {paused}, status {state.Status}, restores after the pause {restoresAfterThePause}, in all {RestoreCalls()}";
            run.ShouldBe("ended", summary);
            paused.ShouldBe("ended", summary);
            mayRun.ShouldBe(true, summary);
            restoresAfterThePause.ShouldBe(2, summary);
        }

        // pmj takes a while to choose, and what it gives is the lockfile the project already
        // has, so the write changes nothing. The restore that follows must not join the
        // resolve it is part of.
        [Fact]
        public async Task A_resolve_that_took_a_while_and_whose_write_changes_nothing_does_not_have_its_restore_wait_on_the_resolve()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            _manager.RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            var resolving = new TaskCompletionSource<PackageResolve>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.ResolveAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(resolving.Task);

            var preparing = state.PrepareForRunAsync(projects.Current);
            resolving.SetResult(new PackageResolve.Resolved(Lockfile, [Words]));
            var run = await EndsAsync(preparing);

            var mayRun = await AnswerOf(preparing);
            var summary = $"run {run} {mayRun}, status {state.Status}, from a run {state.ProblemsAreFromARun}, restores {RestoreCalls()}, resolves {ResolveCalls()}";
            run.ShouldBe("ended", summary);
            mayRun.ShouldBe(false, summary);
            state.Status.ShouldBe(PackagesStatus.OutOfDate, summary);
        }

        [Fact]
        public async Task Run_pressed_again_after_a_resolve_that_took_a_while_and_failed_for_the_moment_asks_pmj_to_choose_again()
        {
            var projects = await ProjectsAsync(Stored(lockfile: null));
            var state = State(projects);
            _manager.RestoreAsync(Manifest, null, Arg.Any<CancellationToken>()).Returns(new PackageRestore.OutOfDate());
            var resolving = new TaskCompletionSource<PackageResolve>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.ResolveAsync(Manifest, null, Arg.Any<CancellationToken>())
                .Returns(resolving.Task, Task.FromResult<PackageResolve>(new PackageResolve.Resolved("a lock", [Words])));
            _manager.RestoreAsync(Manifest, "a lock", Arg.Any<CancellationToken>()).Returns(new PackageRestore.Restored([Words]));
            Builds("set-1");

            var preparing = state.PrepareForRunAsync(projects.Current);
            resolving.SetResult(new PackageResolve.Failed([Problem("github.unreachable")]));
            var first = await EndsAsync(preparing);
            var again = state.PrepareForRunAsync(projects.Current);
            var second = await EndsAsync(again);

            var firstMayRun = await AnswerOf(preparing);
            var secondMayRun = await AnswerOf(again);
            var summary = $"first run {first} {firstMayRun}, second run {second} {secondMayRun}, status {state.Status}, restores {RestoreCalls()}, resolves {ResolveCalls()}";
            first.ShouldBe("ended", summary);
            second.ShouldBe("ended", summary);
            firstMayRun.ShouldBe(false, summary);
            secondMayRun.ShouldBe(true, summary);
            ResolveCalls().ShouldBe(2, summary);
        }

        // Two compiles named the same set and both come back "not held": the toolchain was
        // restarted, or dropped the set for room. The second arrives while the first's restore
        // is going on, for the same open, unchanged project, and joins it.
        [Fact]
        public async Task A_second_compile_that_lost_the_same_set_while_it_is_being_restored_again_is_given_it_too()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            var rebuilding = new TaskCompletionSource<DependencyAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DependencyAnswer>(new DependencyAnswer.Ready("set-1", ["hello_words"])), rebuilding.Task);
            await state.EnsureRestoredAsync(projects.Current);
            state.Dependencies.ShouldBe("set-1");

            var first = state.RestoreAgainAsync("set-1", Cancellation);
            var statusWhileTheFirstRestores = state.Status;
            var second = state.RestoreAgainAsync("set-1", Cancellation);
            var secondWasAnsweredAtOnce = second.IsCompleted;
            rebuilding.SetResult(new DependencyAnswer.Ready("set-1", ["hello_words"]));
            var firstGot = await GotAsync(first);
            var secondGot = await GotAsync(second);

            var summary = $"status while the first restores {statusWhileTheFirstRestores}, the second was answered at once {secondWasAnsweredAtOnce}, the first got {firstGot}, the second got {secondGot}, restores {RestoreCalls()}";
            firstGot.ShouldBe("set-1", summary);
            secondGot.ShouldBe("set-1", summary);
            RestoreCalls().ShouldBe(2, summary);
        }

        // Typing pauses and a check is sent; Run is pressed while the check is still with the
        // toolchain, so both compiles name the set, and the toolchain has lost it. The run
        // hears while the check's restore is going on, and waits for the set as a run that
        // hears first does.
        [Fact]
        public async Task A_run_sent_while_a_check_was_with_the_toolchain_is_compiled_again_when_both_lost_the_set()
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            Restores(Words);
            var rebuilding = new TaskCompletionSource<DependencyAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
            _toolchain.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DependencyAnswer>(new DependencyAnswer.Ready("set-1", ["hello_words"])), rebuilding.Task);
            await state.EnsureRestoredAsync(projects.Current);

            var compiler = Substitute.For<IToolchain>();
            var checking = new TaskCompletionSource<CompileResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var building = new TaskCompletionSource<CompileResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var theCompilersOwnWord = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 1, 1, "What the compiler says of the program.");
            compiler.CompileAsync(Arg.Is<CompileRequest>(request => request.CheckOnly), Arg.Any<CancellationToken>())
                .Returns(checking.Task, Task.FromResult(new CompileResult(false, [theCompilersOwnWord], null)));
            compiler.CompileAsync(Arg.Is<CompileRequest>(request => !request.CheckOnly), Arg.Any<CancellationToken>())
                .Returns(building.Task, Task.FromResult(new CompileResult(false, [theCompilersOwnWord], null)));
            await using var runs = new RunState(compiler, NullLogger<RunState>.Instance, _time, state);
            var target = new RunTarget(new Dictionary<string, string> { ["main.🍇"] = "🏁 🍇 🍉" }, "main.🍇", Server: false, state.Dependencies);

            var check = runs.CheckAsync(target);
            var run = runs.RunAsync(target);
            checking.SetResult(SetNotHeld);
            await UntilAsync(() => state.Status == PackagesStatus.Restoring, "the check has the set restored again");
            building.SetResult(SetNotHeld);
            var runWasOverBeforeTheSetWasBack = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(3), Cancellation)) == run;
            var shownBeforeTheSetWasBack = string.Join(" | ", runs.Diagnostics.Select(diagnostic => diagnostic.Message));
            rebuilding.SetResult(new DependencyAnswer.Ready("set-1", ["hello_words"]));
            var ran = await EndsAsync(run);
            var checkedIt = await EndsAsync(check);

            var compilesOfTheRun = compiler.ReceivedCalls().Count(call =>
                call.GetMethodInfo().Name == nameof(IToolchain.CompileAsync) && call.GetArguments()[0] is CompileRequest { CheckOnly: false });
            var shown = string.Join(" | ", runs.Diagnostics.Select(diagnostic => diagnostic.Message));
            var summary = $"run {ran}, check {checkedIt}, the run was over before the set was back {runWasOverBeforeTheSetWasBack}, shown then \"{shownBeforeTheSetWasBack}\", "
                + $"compiles of the run {compilesOfTheRun}, shown at the end \"{shown}\", packages {state.Status} {state.Dependencies}";
            ran.ShouldBe("ended", summary);
            compilesOfTheRun.ShouldBe(2, summary);
            shown.ShouldBe(theCompilersOwnWord.Message, summary);
        }

        // A run waits on a restore; newer text overtakes it; the stopped restore ends inside
        // the call that stops it and the run goes round its loop there and then. It finds the
        // newer restore in place and joins it, which holds only while the stop is the last
        // thing the asking does.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Seen_on_one_thread_a_run_waiting_on_a_restore_that_is_stopped_joins_the_newer_one_inside_the_stop(bool postsAtOnce) => OneThread.Run(async () =>
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            var theOlderEnded = Task.CompletedTask;
            _manager.RestoreAsync(Manifest, Lockfile, Arg.Any<CancellationToken>()).Returns(call =>
            {
                var older = EndsInsideCancel(call.Arg<CancellationToken>());
                theOlderEnded = older;
                return older;
            });
            var restoringB = new TaskCompletionSource<PackageRestore>(TaskCreationOptions.RunContinuationsAsynchronously);
            _manager.RestoreAsync("b", Lockfile, Arg.Any<CancellationToken>()).Returns(restoringB.Task);
            Builds("set-B");

            var preparing = state.PrepareForRunAsync(projects.Current);
            await projects.WriteFileAsync("p1", PackagesState.ManifestPath, "b");
            var pause = state.EnsureRestoredAsync(projects.Current);
            var theOlderHadEndedAsThePauseReturned = theOlderEnded.IsCompleted;
            var restoresAsThePauseReturned = RestoreCalls();
            var runWasOverBeforeTheNewerRestore = preparing.IsCompleted;
            await Task.Delay(200, Cancellation);
            var restoresAMomentLater = RestoreCalls();
            restoringB.SetResult(new PackageRestore.Restored([Words]));
            var run = await EndsAsync(preparing);
            var paused = await EndsAsync(pause);
            var mayRun = await AnswerOf(preparing);

            var summary = $"run {run} {mayRun}, pause {paused}, the older had ended as the pause returned {theOlderHadEndedAsThePauseReturned}, restores as the pause returned {restoresAsThePauseReturned}, "
                + $"a moment later {restoresAMomentLater}, in all {RestoreCalls()}, the run was over before the newer restore {runWasOverBeforeTheNewerRestore}, status {state.Status} {state.Dependencies}";
            theOlderHadEndedAsThePauseReturned.ShouldBeTrue(summary);
            run.ShouldBe("ended", summary);
            paused.ShouldBe("ended", summary);
            runWasOverBeforeTheNewerRestore.ShouldBeFalse(summary);
            mayRun.ShouldBe(true, summary);
            state.Dependencies.ShouldBe("set-B", summary);
            restoresAsThePauseReturned.ShouldBe(2, summary);
            restoresAMomentLater.ShouldBe(2, summary);
            RestoreCalls().ShouldBe(2, summary);
        }, postsAtOnce);

        // Each change of the manifest stops the restore a waiting run is on, and that costs the
        // run one of its three turns inside the call that made the change known.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Seen_on_one_thread_a_run_that_is_waiting_gives_up_at_the_third_change_of_the_manifest_and_not_before(bool postsAtOnce) => OneThread.Run(async () =>
        {
            var projects = await ProjectsAsync(Stored());
            var state = State(projects);
            _manager.RestoreAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call => EndsInsideCancel(call.Arg<CancellationToken>()));

            var preparing = state.PrepareForRunAsync(projects.Current);
            var pauses = new List<Task>();
            var overAsEachChangeWasMadeKnown = new List<bool>();
            for (var change = 1; change <= 3; change++)
            {
                await projects.WriteFileAsync("p1", PackagesState.ManifestPath, "changed " + change);
                pauses.Add(state.EnsureRestoredAsync(projects.Current));
                overAsEachChangeWasMadeKnown.Add(preparing.IsCompleted);
            }

            var mayRun = await AnswerOf(preparing);
            var statusAsItGaveUp = state.Status;
            var fromARun = state.ProblemsAreFromARun;
            var restores = RestoreCalls();
            await state.DisposeAsync();
            await Task.WhenAll(pauses).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            var summary = $"over as each change was made known [{string.Join(", ", overAsEachChangeWasMadeKnown)}], run {mayRun}, status as it gave up {statusAsItGaveUp}, from a run {fromARun}, restores {restores}";
            string.Join(", ", overAsEachChangeWasMadeKnown).ShouldBe("False, False, True", summary);
            mayRun.ShouldBe(false, summary);
            statusAsItGaveUp.ShouldBe(PackagesStatus.Restoring, summary);
            restores.ShouldBe(4, summary);
        }, postsAtOnce);

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
