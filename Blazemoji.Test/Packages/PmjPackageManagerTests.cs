using System.Text.Json;
using Blazemoji.Services.Packages;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Packages
{
    public sealed class PmjPackageManagerTests : IDisposable
    {
        private const string Manifest = """{ "package": { "name": "@you/app" }, "dependencies": { "@thatplatypus/grapevine": "0.1.0" } }""";
        private const string Lockfile = """{ "version": 1, "packages": [] }""";

        private static readonly LockedPackage Grapevine = new("@thatplatypus/grapevine", "0.1.0", new string('a', 64));
        private static readonly LockedPackage Crypto = new("@thatplatypus/crypto", "0.1.0", new string('b', 64));
        private static readonly LockedPackage Elsewhere = new("@someone/thing", "1.0.0", new string('c', 64));

        private readonly string _home = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "home-" + Guid.NewGuid().ToString("N"));
        private readonly FakePmj _pmj = new();

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        public void Dispose()
        {
            if (Directory.Exists(_home))
                Directory.Delete(_home, recursive: true);
        }

        private PmjPackageManager Manager(string? scopes = "thatplatypus,emojicode", int atOnce = 4) =>
            new(_pmj, Options.Create(new PackmojiOptions { Scopes = scopes, Home = _home, MaxAtOnce = atOnce }), NullLogger<PmjPackageManager>.Instance);

        private static PmjResult Answered(bool written, IEnumerable<LockedPackage> packages) =>
            new(PmjOutcome.Ran, 0, Json(true, written, packages, []), string.Empty);

        private static PmjResult Refused(params PackageProblem[] problems) =>
            new(PmjOutcome.Ran, 1, Json(false, false, [], problems), string.Empty);

        private static string Json(bool ok, bool written, IEnumerable<LockedPackage> packages, IEnumerable<PackageProblem> problems) =>
            JsonSerializer.Serialize(new
            {
                ok,
                diagnostics = problems.Select(problem => new
                {
                    severity = problem.Severity,
                    code = problem.Code,
                    message = problem.Message,
                    reason = problem.Reason,
                    fix = problem.Fix,
                    location = problem.File is null ? null : new { file = problem.File, line = problem.Line, column = problem.Column },
                }),
                omittedDiagnostics = 0,
                written,
                packages = packages.Select(package => new { name = package.Name, version = package.Version, sha256 = package.Sha256 }),
            });

        private static PackageProblem Problem(string code, string reason = "the reason", string? file = null) =>
            new(PackageProblem.Error, code, "The message.", reason, "the fix", file, file is null ? 0 : 3, file is null ? 0 : 5);

        [Fact]
        public async Task A_restore_gives_pmj_the_two_files_and_asks_it_to_install_what_is_locked_and_choose_nothing()
        {
            _pmj.Answer = _ => Answered(written: false, [Crypto, Grapevine]);

            var restore = await Manager().RestoreAsync(Manifest, Lockfile, Cancellation);

            restore.ShouldBeOfType<PackageRestore.Restored>().Packages.ShouldBe([Crypto, Grapevine]);
            var command = _pmj.Commands.ShouldHaveSingleItem();
            command.Arguments.ShouldBe(["install", "--locked", "--json"]);
            _pmj.FilesSeen.ShouldHaveSingleItem().ShouldBe((Manifest, Lockfile));
        }

        [Fact]
        public async Task pmj_is_told_its_folder_and_the_scopes_and_nothing_else()
        {
            _pmj.Answer = _ => Answered(written: false, []);

            await Manager().RestoreAsync(Manifest, Lockfile, Cancellation);

            var told = _pmj.Commands.Single().Environment.ShouldNotBeNull();
            told.Keys.Order().ShouldBe([PmjVariables.Home, PmjVariables.Scopes]);
            told[PmjVariables.Home].ShouldBe(_home);
            told[PmjVariables.Scopes].ShouldBe("emojicode,thatplatypus");
        }

        [Fact]
        public async Task With_any_scope_allowed_pmj_is_told_no_scopes()
        {
            _pmj.Answer = _ => Answered(written: false, [Elsewhere]);

            var restore = await Manager(scopes: PackageScopes.Any).RestoreAsync(Manifest, Lockfile, Cancellation);

            restore.ShouldBeOfType<PackageRestore.Restored>();
            _pmj.Commands.Single().Environment.ShouldNotBeNull().ShouldNotContainKey(PmjVariables.Scopes);
        }

        [Fact]
        public async Task A_project_with_no_lockfile_gives_pmj_the_manifest_alone()
        {
            _pmj.Answer = _ => Refused(Problem(PmjProblems.LockOutOfDate));

            await Manager().RestoreAsync(Manifest, null, Cancellation);

            _pmj.FilesSeen.ShouldHaveSingleItem().ShouldBe((Manifest, null));
        }

        [Fact]
        public async Task A_lockfile_that_is_missing_or_no_longer_answers_the_manifest_is_out_of_date_and_not_a_failure()
        {
            _pmj.Answer = _ => Refused(Problem(PmjProblems.LockOutOfDate));

            (await Manager().RestoreAsync(Manifest, Lockfile, Cancellation)).ShouldBeOfType<PackageRestore.OutOfDate>();
        }

        [Fact]
        public async Task The_folder_pmj_ran_in_is_gone_afterwards_whether_pmj_did_what_it_was_asked_or_not()
        {
            _pmj.Answer = _ => Answered(written: false, []);
            await Manager().RestoreAsync(Manifest, Lockfile, Cancellation);
            _pmj.Answer = _ => Refused(Problem("json.syntax", file: "packmoji.json"));
            await Manager().RestoreAsync(Manifest, Lockfile, Cancellation);

            _pmj.Commands.Count.ShouldBe(2);
            _pmj.Commands.ShouldAllBe(command => !Directory.Exists(command.WorkingDirectory));
            _pmj.Commands.Select(command => command.WorkingDirectory).Distinct().Count().ShouldBe(2);
        }

        [Fact]
        public async Task A_resolve_asks_pmj_to_install_and_gives_back_the_lockfile_pmj_wrote()
        {
            _pmj.LockfileToWrite = "the lockfile pmj wrote\n";
            _pmj.Answer = _ => Answered(written: true, [Crypto, Grapevine]);

            var resolve = await Manager().ResolveAsync(Manifest, null, Cancellation);

            var resolved = resolve.ShouldBeOfType<PackageResolve.Resolved>();
            resolved.Lockfile.ShouldBe("the lockfile pmj wrote\n");
            resolved.Packages.ShouldBe([Crypto, Grapevine]);
            _pmj.Commands.ShouldHaveSingleItem().Arguments.ShouldBe(["install", "--json"]);
        }

        [Fact]
        public async Task A_resolve_that_left_no_lockfile_is_not_believed()
        {
            _pmj.Answer = _ => Answered(written: true, [Grapevine]);

            var resolve = await Manager().ResolveAsync(Manifest, null, Cancellation);

            resolve.ShouldBeOfType<PackageResolve.Failed>().Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.Unreadable);
        }

        [Fact]
        public async Task What_pmj_finds_wrong_with_the_two_files_is_passed_on_with_its_place()
        {
            var problem = Problem("json.syntax", file: "packmoji.json");
            _pmj.Answer = _ => Refused(problem);

            var failed = (await Manager().RestoreAsync(Manifest, Lockfile, Cancellation)).ShouldBeOfType<PackageRestore.Failed>();

            failed.Problems.ShouldBe([problem]);
        }

        [Fact]
        public async Task A_package_outside_the_scopes_is_refused_and_nothing_is_written()
        {
            var refusal = Problem("scope.not-allowed");
            _pmj.Answer = _ => Refused(refusal);

            var resolve = await Manager().ResolveAsync(Manifest, null, Cancellation);

            resolve.ShouldBeOfType<PackageResolve.Failed>().Problems.ShouldBe([refusal]);
        }

        [Fact]
        public async Task A_package_outside_the_scopes_that_pmj_let_through_is_refused_here()
        {
            _pmj.LockfileToWrite = "a lockfile";
            _pmj.Answer = _ => Answered(written: true, [Grapevine, Elsewhere]);

            var resolve = await Manager().ResolveAsync(Manifest, null, Cancellation);

            var problem = resolve.ShouldBeOfType<PackageResolve.Failed>().Problems.ShouldHaveSingleItem();
            problem.Code.ShouldBe(PackageProblemCodes.Scope);
            problem.Message.ShouldContain("@someone/thing");
            problem.Reason.ShouldContain("emojicode, thatplatypus");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task A_host_told_no_scopes_runs_no_pmj_and_says_what_to_set(string? scopes)
        {
            var manager = Manager(scopes: scopes);

            var restore = await manager.RestoreAsync(Manifest, Lockfile, Cancellation);
            var resolve = await manager.ResolveAsync(Manifest, null, Cancellation);

            var problem = restore.ShouldBeOfType<PackageRestore.Failed>().Problems.ShouldHaveSingleItem();
            problem.Code.ShouldBe(PackageProblemCodes.Scope);
            problem.Fix.ShouldContain("Packmoji__Scopes");
            resolve.ShouldBeOfType<PackageResolve.Failed>().Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.Scope);
            _pmj.Commands.ShouldBeEmpty();
        }

        [Fact]
        public async Task With_no_pmj_it_says_so_once_and_throws_nothing()
        {
            _pmj.Answer = _ => new PmjResult(PmjOutcome.NotFound, -1, string.Empty, string.Empty);

            var failed = (await Manager().RestoreAsync(Manifest, Lockfile, Cancellation)).ShouldBeOfType<PackageRestore.Failed>();

            var problem = failed.Problems.ShouldHaveSingleItem();
            problem.Code.ShouldBe(PackageProblemCodes.NoPackmoji);
            problem.Message.ShouldBe("This copy of Blazemoji has no Packmoji.");
        }

        [Fact]
        public async Task A_pmj_that_ran_out_of_time_is_a_problem_to_try_again()
        {
            _pmj.Answer = _ => new PmjResult(PmjOutcome.TimedOut, -1, string.Empty, string.Empty);

            var failed = (await Manager().ResolveAsync(Manifest, null, Cancellation)).ShouldBeOfType<PackageResolve.Failed>();

            failed.Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.TimedOut);
        }

        [Theory]
        [InlineData("")]
        [InlineData("error: Unrecognized command or argument '--json'.")]
        [InlineData("""{"ok":true,"packages":[{"name":"not a name","version":"1","sha256":"x"}]}""")]
        public async Task What_pmj_printed_that_cannot_be_used_is_one_fixed_problem(string stdout)
        {
            _pmj.Answer = _ => new PmjResult(PmjOutcome.Ran, 2, stdout, "pmj's own words, with /a/secret/path in them");

            var failed = (await Manager().RestoreAsync(Manifest, Lockfile, Cancellation)).ShouldBeOfType<PackageRestore.Failed>();

            var problem = failed.Problems.ShouldHaveSingleItem();
            problem.Code.ShouldBe(PackageProblemCodes.Unreadable);
            (problem.Message + problem.Reason + problem.Fix).ShouldNotContain("secret");
        }

        [Fact]
        public async Task A_problem_about_this_machine_stays_in_the_log()
        {
            _pmj.Answer = _ => Refused(Problem("cache.unusable", reason: "could not write under " + _home));

            var failed = (await Manager().RestoreAsync(Manifest, Lockfile, Cancellation)).ShouldBeOfType<PackageRestore.Failed>();

            failed.Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.Unreadable);
        }

        [Fact]
        public async Task GitHub_being_out_of_reach_is_said_in_pmjs_message_and_never_in_a_sockets_words()
        {
            _pmj.Answer = _ => Refused(Problem("github.unreachable", reason: "the request failed: Connection refused (10.0.0.7:443)"));

            var failed = (await Manager().RestoreAsync(Manifest, Lockfile, Cancellation)).ShouldBeOfType<PackageRestore.Failed>();

            var problem = failed.Problems.ShouldHaveSingleItem();
            problem.Code.ShouldBe("github.unreachable");
            problem.Reason.ShouldBe(PmjProblems.GitHubSaidReason);
        }

        [Fact]
        public async Task Files_larger_than_Packmoji_allows_are_refused_before_pmj_is_run()
        {
            var failed = (await Manager().RestoreAsync(new string('x', 1024 * 1024 + 1), Lockfile, Cancellation)).ShouldBeOfType<PackageRestore.Failed>();

            failed.Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.TooLarge);
            (await Manager().RestoreAsync(Manifest, new string('x', 4 * 1024 * 1024 + 1), Cancellation)).ShouldBeOfType<PackageRestore.Failed>();
            _pmj.Commands.ShouldBeEmpty();
        }

        [Fact]
        public async Task One_more_than_may_run_at_once_is_told_to_ask_again_and_is_taken_once_there_is_room()
        {
            var manager = Manager(atOnce: 1);
            _pmj.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pmj.Answer = _ => Answered(written: false, []);
            var first = manager.RestoreAsync(Manifest, Lockfile, Cancellation);
            await _pmj.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            var second = await manager.RestoreAsync(Manifest, Lockfile, Cancellation);

            second.ShouldBeOfType<PackageRestore.Failed>().Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.Busy);
            _pmj.Hold.SetResult();
            (await first.WaitAsync(TimeSpan.FromSeconds(10), Cancellation)).ShouldBeOfType<PackageRestore.Restored>();
            _pmj.Hold = null;
            (await manager.RestoreAsync(Manifest, Lockfile, Cancellation)).ShouldBeOfType<PackageRestore.Restored>();
        }

        [Fact]
        public async Task A_caller_who_stops_waiting_is_told_so_and_the_folder_is_still_removed()
        {
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            _pmj.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var manager = Manager(atOnce: 1);
            var restoring = manager.RestoreAsync(Manifest, Lockfile, stopping.Token);
            await _pmj.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

            await stopping.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(() => restoring);
            Directory.Exists(_pmj.Commands.Single().WorkingDirectory).ShouldBeFalse();
            _pmj.Hold = null;
            _pmj.Answer = _ => Answered(written: false, []);
            (await manager.RestoreAsync(Manifest, Lockfile, Cancellation)).ShouldBeOfType<PackageRestore.Restored>();
        }

        [Fact]
        public async Task An_archive_pmj_fetched_is_opened_from_where_pmj_keeps_it()
        {
            var path = PackmojiCache.ArchivePath(_home, Grapevine).ShouldNotBeNull();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, [1, 2, 3], Cancellation);

            await using var archive = Manager().OpenArchive(Grapevine).ShouldNotBeNull();

            var read = new MemoryStream();
            await archive.CopyToAsync(read, Cancellation);
            read.ToArray().ShouldBe([1, 2, 3]);
        }

        [Fact]
        public void An_archive_that_is_not_there_or_is_not_one_this_host_takes_is_nothing()
        {
            var manager = Manager();

            manager.OpenArchive(Crypto).ShouldBeNull();
            manager.OpenArchive(Elsewhere).ShouldBeNull();
            manager.OpenArchive(new LockedPackage("@thatplatypus/../x", "0.1.0", Grapevine.Sha256)).ShouldBeNull();
        }

        private sealed class FakePmj : IPmj
        {
            public List<PmjCommand> Commands { get; } = [];

            public List<(string Manifest, string? Lockfile)> FilesSeen { get; } = [];

            public Func<PmjCommand, PmjResult> Answer { get; set; } = _ => new PmjResult(PmjOutcome.Ran, 0, "{}", string.Empty);

            public string? LockfileToWrite { get; set; }

            public TaskCompletionSource? Hold { get; set; }

            public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<PmjResult> RunAsync(PmjCommand command, CancellationToken cancellationToken = default)
            {
                var lockfile = Path.Combine(command.WorkingDirectory, PmjPackageManager.LockfileFile);
                lock (Commands)
                {
                    Commands.Add(command);
                    FilesSeen.Add((
                        File.ReadAllText(Path.Combine(command.WorkingDirectory, PmjPackageManager.ManifestFile)),
                        File.Exists(lockfile) ? File.ReadAllText(lockfile) : null));
                }

                Started.TrySetResult();
                if (Hold is { } hold)
                    await hold.Task.WaitAsync(cancellationToken);

                if (LockfileToWrite is { } text)
                    await File.WriteAllTextAsync(lockfile, text, cancellationToken);

                return Answer(command);
            }
        }
    }
}
