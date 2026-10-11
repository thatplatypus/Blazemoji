using System.Security.Cryptography;
using System.Text.Json;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Local;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Packmoji
{
    public sealed class LocalDependencySetsTests : IAsyncDisposable
    {
        private static readonly byte[] WordsBytes = "the archive of hello_words"u8.ToArray();
        private static readonly byte[] GreeterBytes = "the archive of hello_greeter"u8.ToArray();
        private static readonly LockedPackage Words = new("@thatplatypus/hello_words", "0.1.0", Digest(WordsBytes));
        private static readonly LockedPackage Greeter = new("@thatplatypus/hello_greeter", "0.1.0", Digest(GreeterBytes));
        private static readonly DependencyRequest Request = new("""{ "the": "manifest" }""", """{ "the": "lockfile" }""");

        private readonly string _work = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "sets-" + Guid.NewGuid().ToString("N"));
        private readonly FakePmj _pmj = new();
        private readonly List<LocalDependencySets> _made = [];

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private string Home => Path.Combine(_work, "packmoji");

        public async ValueTask DisposeAsync()
        {
            foreach (var sets in _made)
                await sets.DisposeAsync();

            if (Directory.Exists(_work))
                Directory.Delete(_work, recursive: true);
        }

        private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        private LocalDependencySets Sets(Action<ToolchainOptions>? configure = null, string? scopes = null, string? home = null)
        {
            var options = new ToolchainOptions { WorkRoot = _work };
            configure?.Invoke(options);

            var sets = new LocalDependencySets(
                Options.Create(options),
                Options.Create(new PackmojiOptions { Scopes = scopes, Home = home }),
                _pmj,
                NullLogger<LocalDependencySets>.Instance);
            _made.Add(sets);
            return sets;
        }

        private void Lock(params LockedPackage[] packages)
        {
            lock (_pmj.Commands)
            {
                _pmj.Locked.Clear();
                _pmj.Locked.AddRange(packages);
            }
        }

        private static async Task StoreAsync(LocalDependencySets sets, LockedPackage package, byte[] bytes) =>
            (await sets.AddArchiveAsync(package, new MemoryStream(bytes), Cancellation)).ShouldBe(ArchiveOutcome.Stored);

        private async Task<DependencyAnswer.Ready> ReadyAsync(LocalDependencySets sets)
        {
            Lock(Greeter, Words);
            await StoreAsync(sets, Words, WordsBytes);
            await StoreAsync(sets, Greeter, GreeterBytes);
            return (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();
        }

        [Fact]
        public async Task The_archives_the_cache_does_not_hold_are_needed_and_nothing_is_built()
        {
            var sets = Sets();
            Lock(Greeter, Words);
            await StoreAsync(sets, Words, WordsBytes);

            var answer = await sets.PrepareAsync(Request, Cancellation);

            answer.ShouldBeOfType<DependencyAnswer.Needed>().Archives.ShouldBe([Greeter]);
            _pmj.Builds.ShouldBe(0);
        }

        [Fact]
        public async Task With_every_archive_there_the_set_is_built_and_ready()
        {
            var ready = await ReadyAsync(Sets());

            ready.Id.ShouldMatch("^[0-9a-f]{64}$");
            ready.Packages.ShouldBe(["hello_greeter", "hello_words"]);
            _pmj.Builds.ShouldBe(1);
        }

        [Fact]
        public async Task pmj_is_given_the_two_files_as_they_came_and_builds_only_what_is_depended_on()
        {
            await ReadyAsync(Sets());

            var build = _pmj.Commands.Single(command => command.Arguments[0] == "build");
            build.Arguments.ShouldBe(["build", "--dependencies-only", "--json"]);
            _pmj.FilesSeen[build].ShouldBe((Request.Manifest, Request.Lockfile));
        }

        [Fact]
        public async Task pmj_is_told_where_the_compiler_is_and_is_given_no_way_to_GitHub()
        {
            var sets = Sets(options =>
            {
                options.CompilerPath = "/the/toolchain/emojicodec/emojicodec";
                options.PackagesPath = "/the/toolchain/packages";
                options.LinkerPath = "/usr/bin/c++";
                options.DependencyBuildTimeout = TimeSpan.FromSeconds(77);
            });

            await ReadyAsync(sets);

            var build = _pmj.Commands.Single(command => command.Arguments[0] == "build");
            build.Timeout.ShouldBe(TimeSpan.FromSeconds(77));
            var told = build.Environment.ShouldNotBeNull();
            told[PmjVariables.Home].ShouldBe(Home);
            told[PmjVariables.Compiler].ShouldBe("/the/toolchain/emojicodec/emojicodec");
            told[PmjVariables.CompilerPackages].ShouldBe("/the/toolchain/packages");
            told[PmjVariables.CompilerInclude].ShouldBe(Path.Combine("/the/toolchain/emojicodec", "include"));
            told[PmjVariables.Cxx].ShouldBe("/usr/bin/c++");
            told[PmjVariables.GitHub].ShouldBe("http://127.0.0.1:9");
            told[PmjVariables.GitHubApi].ShouldBe("http://127.0.0.1:9");

            var tree = _pmj.Commands.First(command => command.Arguments[0] == "tree");
            tree.Environment.ShouldNotBeNull()[PmjVariables.GitHub].ShouldBe("http://127.0.0.1:9");
        }

        [Fact]
        public async Task The_same_packages_asked_for_again_are_ready_without_a_second_build()
        {
            var sets = Sets();
            var first = await ReadyAsync(sets);

            var second = (await sets.PrepareAsync(new DependencyRequest("another manifest", "another lockfile"), Cancellation))
                .ShouldBeOfType<DependencyAnswer.Ready>();

            second.Id.ShouldBe(first.Id);
            _pmj.Builds.ShouldBe(1);
        }

        [Fact]
        public async Task Other_packages_are_another_set()
        {
            var sets = Sets();
            var both = await ReadyAsync(sets);

            Lock(Words);
            var one = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();

            one.Id.ShouldNotBe(both.Id);
            one.Packages.ShouldBe(["hello_words"]);
            _pmj.Builds.ShouldBe(2);
        }

        [Fact]
        public async Task Two_who_ask_at_once_share_one_build()
        {
            var sets = Sets();
            Lock(Words);
            await StoreAsync(sets, Words, WordsBytes);
            _pmj.HoldBuilds = new TaskCompletionSource();

            var first = sets.PrepareAsync(Request, Cancellation);
            var second = sets.PrepareAsync(Request, Cancellation);
            await _pmj.BuildStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
            first.IsCompleted.ShouldBeFalse();
            second.IsCompleted.ShouldBeFalse();
            _pmj.HoldBuilds.SetResult();

            (await first.WaitAsync(TimeSpan.FromSeconds(30), Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();
            (await second.WaitAsync(TimeSpan.FromSeconds(30), Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();
            _pmj.Builds.ShouldBe(1);
        }

        [Fact]
        public async Task A_caller_who_gives_up_does_not_stop_the_build_for_the_next()
        {
            var sets = Sets();
            Lock(Words);
            await StoreAsync(sets, Words, WordsBytes);
            _pmj.HoldBuilds = new TaskCompletionSource();
            using var gaveUp = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);

            var first = sets.PrepareAsync(Request, gaveUp.Token);
            await _pmj.BuildStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
            await gaveUp.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(30), Cancellation));

            var next = sets.PrepareAsync(Request, Cancellation);
            _pmj.HoldBuilds.SetResult();

            (await next.WaitAsync(TimeSpan.FromSeconds(30), Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();
            _pmj.Builds.ShouldBe(1);
        }

        [Fact]
        public async Task More_builds_at_once_than_are_allowed_is_busy()
        {
            var sets = Sets(options => options.MaxConcurrentDependencyBuilds = 1);
            await StoreAsync(sets, Words, WordsBytes);
            await StoreAsync(sets, Greeter, GreeterBytes);
            _pmj.HoldBuilds = new TaskCompletionSource();

            Lock(Words);
            var first = sets.PrepareAsync(Request, Cancellation);
            await _pmj.BuildStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
            Lock(Greeter);
            var second = await sets.PrepareAsync(Request, Cancellation);

            second.ShouldBeOfType<DependencyAnswer.Busy>();
            _pmj.HoldBuilds.SetResult();
            (await first.WaitAsync(TimeSpan.FromSeconds(30), Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();
        }

        [Fact]
        public async Task A_package_outside_the_scopes_is_refused_before_anything_is_built_or_asked_for()
        {
            var sets = Sets(scopes: "emojicode");
            Lock(Words, new LockedPackage("@emojicode/fine", "1.0.0", Digest([1, 2, 3])));

            var failed = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            var problem = failed.Problems.ShouldHaveSingleItem();
            problem.Code.ShouldBe(PackageProblemCodes.Scope);
            problem.Message.ShouldContain("@thatplatypus/hello_words");
            problem.Reason.ShouldContain("emojicode");
            _pmj.Builds.ShouldBe(0);
        }

        [Fact]
        public async Task What_pmj_finds_wrong_with_the_two_files_is_passed_on()
        {
            var sets = Sets();
            var outOfDate = new PackageProblem(PackageProblem.Error, "lock.out-of-date", "There is no packmoji.lock.", "this command reads what is locked", "run pmj install");
            _pmj.TreeProblems.Add(outOfDate);

            var failed = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            failed.Problems.ShouldHaveSingleItem().ShouldBe(outOfDate);
            _pmj.Builds.ShouldBe(0);
        }

        [Fact]
        public async Task What_pmj_finds_wrong_when_it_builds_is_passed_on_and_no_set_is_kept()
        {
            var sets = Sets();
            Lock(Words);
            await StoreAsync(sets, Words, WordsBytes);
            _pmj.BuildProblems.Add(new PackageProblem(PackageProblem.Error, "build.compile-failed", "The compiler refused the code.", "🍉 expected", "tell its author"));

            var failed = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            failed.Problems.ShouldHaveSingleItem().Code.ShouldBe("build.compile-failed");
            Directory.GetDirectories(_work, "sets-*").SelectMany(root => Directory.GetDirectories(root))
                .Where(folder => Path.GetFileName(folder) != "asks").ShouldBeEmpty();

            _pmj.BuildProblems.Clear();
            (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();
            _pmj.Builds.ShouldBe(2);
        }

        [Theory]
        [InlineData(PmjOutcome.NotFound, PackageProblemCodes.NoPackmoji)]
        [InlineData(PmjOutcome.TimedOut, PackageProblemCodes.TimedOut)]
        public async Task A_pmj_that_is_missing_or_ran_out_of_time_is_a_problem_of_its_own(PmjOutcome outcome, string code)
        {
            var sets = Sets();
            _pmj.Instead = _ => new PmjResult(outcome, -1, string.Empty, string.Empty);

            var failed = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            failed.Problems.ShouldHaveSingleItem().Code.ShouldBe(code);
        }

        [Theory]
        [InlineData("")]
        [InlineData("error[lock.out-of-date]: There is no packmoji.lock.")]
        [InlineData("""{"ok":false,"diagnostics":[]}""")]
        public async Task What_pmj_printed_that_cannot_be_read_or_gives_no_reason_is_one_fixed_problem(string stdout)
        {
            var sets = Sets();
            _pmj.Instead = _ => new PmjResult(PmjOutcome.Ran, 70, stdout, "pmj itself failed: /a/secret/path");

            var failed = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            var problem = failed.Problems.ShouldHaveSingleItem();
            problem.Code.ShouldBe(PackageProblemCodes.Unreadable);
            (problem.Message + problem.Reason + problem.Fix).ShouldNotContain("secret");
        }

        [Fact]
        public async Task A_locked_package_whose_name_could_not_be_a_path_is_refused()
        {
            var sets = Sets();
            Lock(new LockedPackage("@thatplatypus/../../etc", "0.1.0", Words.Sha256));

            var failed = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            failed.Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.Unreadable);
        }

        [Fact]
        public async Task A_built_package_whose_archive_is_not_there_is_not_a_set()
        {
            var sets = Sets();
            Lock(Words);
            await StoreAsync(sets, Words, WordsBytes);
            _pmj.LeaveOutArchives = true;

            var failed = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            failed.Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.Unreadable);
        }

        [Fact]
        public async Task A_library_to_link_that_could_be_an_option_is_not_a_set()
        {
            var sets = Sets();
            Lock(Words);
            await StoreAsync(sets, Words, WordsBytes);
            _pmj.Links["hello_words"] = ["-Wl,--wrap=main"];

            var failed = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            failed.Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.Unreadable);
        }

        [Fact]
        public async Task Files_larger_than_Packmoji_allows_are_refused_before_pmj_is_run()
        {
            var sets = Sets();

            var failed = (await sets.PrepareAsync(new DependencyRequest(new string('x', 1024 * 1024 + 1), "{}"), Cancellation))
                .ShouldBeOfType<DependencyAnswer.Failed>();

            failed.Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.TooLarge);
            _pmj.Commands.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_lease_gives_the_folder_to_search_the_archives_and_the_libraries_each_once()
        {
            var sets = Sets();
            _pmj.Links["hello_greeter"] = ["m", "curl"];
            _pmj.Links["hello_words"] = ["m"];
            var ready = await ReadyAsync(sets);

            using var lease = sets.Lease(ready.Id).ShouldNotBeNull();

            var packages = lease.PackagesDirectory.ShouldNotBeNull();
            Path.GetFileName(packages).ShouldBe("packages");
            lease.Archives.ShouldBe(
            [
                Path.Combine(packages, "hello_greeter", "libhello_greeter.a"),
                Path.Combine(packages, "hello_words", "libhello_words.a"),
            ]);
            lease.Archives.ShouldAllBe(archive => File.Exists(archive));
            lease.LinkLibraries.ShouldBe(["m", "curl"]);
        }

        [Theory]
        [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
        [InlineData("../../etc")]
        [InlineData("")]
        public void A_set_that_is_not_held_cannot_be_leased(string id)
        {
            Sets().Lease(id).ShouldBeNull();
        }

        [Fact]
        public async Task The_set_used_longest_ago_is_dropped_for_room()
        {
            var sets = Sets(options => options.MaxDependencySets = 1);
            var both = await ReadyAsync(sets);
            using (var lease = sets.Lease(both.Id).ShouldNotBeNull())
            {
                Directory.Exists(lease.PackagesDirectory).ShouldBeTrue();
            }

            Lock(Words);
            var one = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();

            sets.Lease(both.Id).ShouldBeNull();
            using var kept = sets.Lease(one.Id).ShouldNotBeNull();
            Directory.GetDirectories(Directory.GetDirectories(_work, "sets-*").Single())
                .Select(Path.GetFileName).Where(name => name != "asks")
                .ShouldHaveSingleItem().ShouldStartWith(one.Id + "-");
        }

        [Fact]
        public async Task A_set_built_again_after_being_dropped_has_a_folder_of_its_own()
        {
            var sets = Sets(options => options.MaxDependencySets = 1);
            var first = await ReadyAsync(sets);
            string? firstPackages;
            using (var lease = sets.Lease(first.Id).ShouldNotBeNull())
            {
                firstPackages = lease.PackagesDirectory.ShouldNotBeNull();
            }

            Lock(Words);
            (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();
            sets.Lease(first.Id).ShouldBeNull();

            Lock(Greeter, Words);
            var again = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();

            again.Id.ShouldBe(first.Id);
            using var second = sets.Lease(again.Id).ShouldNotBeNull();
            second.PackagesDirectory.ShouldNotBe(firstPackages);
            second.Archives.ShouldAllBe(archive => File.Exists(archive));
        }

        [Theory]
        [InlineData("cache.unusable")]
        [InlineData("built.unusable")]
        [InlineData("project.unreadable")]
        [InlineData("compiler.incomplete")]
        [InlineData("compiler.not-found")]
        [InlineData("tool.not-found")]
        [InlineData("github.unreachable")]
        [InlineData("some.new-code")]
        public async Task A_problem_about_this_machine_is_not_passed_on_and_names_no_place_on_it(string code)
        {
            var sets = Sets();
            Lock(Words);
            await StoreAsync(sets, Words, WordsBytes);
            _pmj.BuildProblems.Add(new PackageProblem(PackageProblem.Error, code, "\"/srv/secret/place\" could not be written", "Access to the path '/srv/secret/place' is denied.", "fix it"));

            var failed = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            var problem = failed.Problems.ShouldHaveSingleItem();
            problem.Code.ShouldBe(PackageProblemCodes.Unreadable);
            (problem.Message + problem.Reason + problem.Fix).ShouldNotContain("secret");
        }

        [Fact]
        public async Task A_problem_that_names_a_place_under_the_work_root_is_not_passed_on_whatever_its_code()
        {
            var sets = Sets();
            _pmj.TreeProblems.Add(new PackageProblem(PackageProblem.Error, "lock.mismatch", "The lock does not match.", $"it was read at {Path.Combine(_work, "somewhere")}", "run pmj install"));

            var failed = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            failed.Problems.ShouldHaveSingleItem().Code.ShouldBe(PackageProblemCodes.Unreadable);
        }

        [Fact]
        public async Task A_build_that_a_compiler_refused_is_passed_on_without_what_the_compiler_said()
        {
            var sets = Sets();
            Lock(Words);
            await StoreAsync(sets, Words, WordsBytes);
            _pmj.BuildProblems.Add(new PackageProblem(PackageProblem.Error, "build.compile-failed", "The package \"@thatplatypus/hello_words\" could not be compiled.", "lib.🍇:3:5: 🍉 expected", "tell its author"));

            var failed = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            var problem = failed.Problems.ShouldHaveSingleItem();
            problem.Code.ShouldBe("build.compile-failed");
            problem.Message.ShouldBe("The package \"@thatplatypus/hello_words\" could not be compiled.");
            problem.Fix.ShouldBe("tell its author");
            problem.Reason.ShouldBe("what the compiler said is in the toolchain's log");
        }

        [Fact]
        public async Task A_warning_about_this_machine_is_dropped_and_the_rest_is_passed_on()
        {
            var sets = Sets();
            var outOfDate = new PackageProblem(PackageProblem.Error, "lock.out-of-date", "There is no packmoji.lock.", "this command reads what is locked", "run pmj install");
            _pmj.TreeProblems.Add(new PackageProblem(PackageProblem.Warning, "cache.unusable", "The cache is odd.", "somewhere", "nothing"));
            _pmj.TreeProblems.Add(outOfDate);

            var failed = (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            failed.Problems.ShouldHaveSingleItem().ShouldBe(outOfDate);
        }

        [Fact]
        public async Task Disposing_ends_a_build_that_is_running_and_everyone_waiting_is_answered()
        {
            var sets = Sets();
            Lock(Words);
            await StoreAsync(sets, Words, WordsBytes);
            _pmj.HoldBuilds = new TaskCompletionSource();

            var pending = sets.PrepareAsync(Request, Cancellation);
            await _pmj.BuildStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
            await sets.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

            (await pending.WaitAsync(TimeSpan.FromSeconds(30), Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();
            Directory.GetDirectories(_work, "sets-*").ShouldBeEmpty();
        }

        [Fact]
        public async Task After_it_is_disposed_it_refuses_work()
        {
            var sets = Sets();
            await sets.DisposeAsync();

            await Should.ThrowAsync<ObjectDisposedException>(() => sets.PrepareAsync(Request, Cancellation));
            await Should.ThrowAsync<ObjectDisposedException>(() => sets.AddArchiveAsync(Words, new MemoryStream(WordsBytes), Cancellation));
            await Should.ThrowAsync<ObjectDisposedException>(() => sets.GetDocumentationAsync("id", "hello_words", Cancellation));
            Should.Throw<ObjectDisposedException>(() => sets.Lease("id"));
        }

        [Fact]
        public async Task Disposing_twice_is_harmless()
        {
            var sets = Sets();

            await sets.DisposeAsync();
            await sets.DisposeAsync();
        }

        [Fact]
        public async Task More_asks_at_once_than_are_allowed_is_busy()
        {
            var sets = Sets(options => options.MaxConcurrentDependencyRequests = 1);
            _pmj.HoldTrees = new TaskCompletionSource();

            var first = sets.PrepareAsync(Request, Cancellation);
            await _pmj.TreeStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
            var second = await sets.PrepareAsync(Request, Cancellation);

            second.ShouldBeOfType<DependencyAnswer.Busy>();
            _pmj.HoldTrees.SetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
            (await sets.PrepareAsync(Request, Cancellation)).ShouldNotBeOfType<DependencyAnswer.Busy>();
        }

        [Fact]
        public async Task A_set_that_a_compile_holds_is_not_dropped_for_room()
        {
            var sets = Sets(options => options.MaxDependencySets = 1);
            var both = await ReadyAsync(sets);
            using var held = sets.Lease(both.Id).ShouldNotBeNull();

            Lock(Words);
            (await sets.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();

            held.Archives.ShouldAllBe(archive => File.Exists(archive));
            using var again = sets.Lease(both.Id).ShouldNotBeNull();
        }

        [Fact]
        public async Task The_report_of_a_package_in_a_set_can_be_read_and_no_other_file_can()
        {
            var sets = Sets();
            var ready = await ReadyAsync(sets);

            (await sets.GetDocumentationAsync(ready.Id, "hello_words", Cancellation)).ShouldBe("""{"report":"hello_words"}""");
            (await sets.GetDocumentationAsync(ready.Id, "s", Cancellation)).ShouldBeNull();
            (await sets.GetDocumentationAsync(ready.Id, "../hello_words", Cancellation)).ShouldBeNull();
            (await sets.GetDocumentationAsync("not-a-set", "hello_words", Cancellation)).ShouldBeNull();
        }

        [Fact]
        public async Task An_archive_is_kept_where_pmj_looks_for_it()
        {
            var sets = Sets();

            await StoreAsync(sets, Words, WordsBytes);

            var path = PackmojiCache.ArchivePath(Home, Words).ShouldNotBeNull();
            (await File.ReadAllBytesAsync(path, Cancellation)).ShouldBe(WordsBytes);
            Directory.GetFiles(Path.GetDirectoryName(path)!).ShouldBe([path]);
        }

        [Fact]
        public async Task An_archive_that_is_already_there_is_stored_again_without_complaint()
        {
            var sets = Sets();

            await StoreAsync(sets, Words, WordsBytes);
            await StoreAsync(sets, Words, WordsBytes);
        }

        [Fact]
        public async Task Bytes_that_are_not_the_digest_they_came_under_are_refused_and_nothing_is_left()
        {
            var sets = Sets();

            var outcome = await sets.AddArchiveAsync(Words, new MemoryStream(GreeterBytes), Cancellation);

            outcome.ShouldBe(ArchiveOutcome.WrongDigest);
            var folder = Path.GetDirectoryName(PackmojiCache.ArchivePath(Home, Words))!;
            (Directory.Exists(folder) ? Directory.GetFiles(folder) : []).ShouldBeEmpty();
        }

        [Fact]
        public async Task An_archive_over_the_limit_is_refused_and_nothing_is_left()
        {
            var sets = Sets(options => options.MaxArchiveBytes = 8);

            var outcome = await sets.AddArchiveAsync(Words, new MemoryStream(WordsBytes), Cancellation);

            outcome.ShouldBe(ArchiveOutcome.TooLarge);
            var folder = Path.GetDirectoryName(PackmojiCache.ArchivePath(Home, Words))!;
            (Directory.Exists(folder) ? Directory.GetFiles(folder) : []).ShouldBeEmpty();
        }

        [Fact]
        public async Task An_archive_under_a_name_that_is_not_one_or_outside_the_scopes_is_refused()
        {
            var sets = Sets(scopes: "emojicode");

            (await sets.AddArchiveAsync(new LockedPackage("@emojicode/../x", "0.1.0", Words.Sha256), new MemoryStream(WordsBytes), Cancellation))
                .ShouldBe(ArchiveOutcome.Malformed);
            (await sets.AddArchiveAsync(Words, new MemoryStream(WordsBytes), Cancellation))
                .ShouldBe(ArchiveOutcome.NotAllowed);
            Directory.Exists(Path.Combine(Home, "cache", "thatplatypus")).ShouldBeFalse();
        }

        [Fact]
        public async Task An_archive_that_cannot_be_kept_is_failed_and_nothing_is_thrown()
        {
            Directory.CreateDirectory(_work);
            var notAFolder = Path.Combine(_work, "a-file");
            await File.WriteAllTextAsync(notAFolder, "in the way", Cancellation);
            var sets = Sets(home: notAFolder);

            var outcome = await sets.AddArchiveAsync(Words, new MemoryStream(WordsBytes), Cancellation);

            outcome.ShouldBe(ArchiveOutcome.Failed);
        }

        [Fact]
        public async Task The_home_pmj_is_given_is_the_one_configured_when_there_is_one()
        {
            var elsewhere = Path.Combine(_work, "elsewhere");
            var sets = Sets(home: elsewhere);

            await StoreAsync(sets, Words, WordsBytes);

            File.Exists(PackmojiCache.ArchivePath(elsewhere, Words)).ShouldBeTrue();
        }

        /// <summary>
        /// Stands in for pmj: says what is locked, and for a build leaves in the folder it is
        /// run in what the real one leaves there.
        /// </summary>
        private sealed class FakePmj : IPmj
        {
            public List<LockedPackage> Locked { get; } = [];

            public Dictionary<string, string[]> Links { get; } = [];

            public List<PackageProblem> TreeProblems { get; } = [];

            public List<PackageProblem> BuildProblems { get; } = [];

            public List<PmjCommand> Commands { get; } = [];

            public Dictionary<PmjCommand, (string Manifest, string Lockfile)> FilesSeen { get; } = [];

            public Func<PmjCommand, PmjResult>? Instead { get; set; }

            public TaskCompletionSource? HoldBuilds { get; set; }

            public TaskCompletionSource BuildStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource? HoldTrees { get; set; }

            public TaskCompletionSource TreeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool LeaveOutArchives { get; set; }

            public int Builds
            {
                get
                {
                    lock (Commands)
                        return Commands.Count(command => command.Arguments[0] == "build");
                }
            }

            public async Task<PmjResult> RunAsync(PmjCommand command, CancellationToken cancellationToken = default)
            {
                List<LockedPackage> locked;
                lock (Commands)
                {
                    Commands.Add(command);
                    FilesSeen[command] = (
                        File.ReadAllText(Path.Combine(command.WorkingDirectory, "packmoji.json")),
                        File.ReadAllText(Path.Combine(command.WorkingDirectory, "packmoji.lock")));
                    locked = [.. Locked];
                }

                if (Instead is { } instead)
                    return instead(command);

                if (command.Arguments[0] == "tree")
                {
                    TreeStarted.TrySetResult();
                    if (HoldTrees is { } holdTree)
                        await holdTree.Task.WaitAsync(cancellationToken);

                    return Printed(TreeProblems, new { packages = locked.Select(package => new { name = package.Name, version = package.Version, sha256 = package.Sha256 }) });
                }

                BuildStarted.TrySetResult();
                if (HoldBuilds is { } hold)
                    await hold.Task.WaitAsync(cancellationToken);

                if (BuildProblems.Count > 0)
                    return Printed(BuildProblems, new { packages = Array.Empty<object>() });

                var built = new List<object>();
                foreach (var package in locked)
                {
                    PackageNames.TryParse(package.Name, out string _scope, out var bare).ShouldBeTrue();
                    var folder = Path.Combine(command.WorkingDirectory, "packages", bare);
                    Directory.CreateDirectory(folder);
                    if (!LeaveOutArchives)
                        await File.WriteAllTextAsync(Path.Combine(folder, $"lib{bare}.a"), "archive", cancellationToken);

                    await File.WriteAllTextAsync(Path.Combine(folder, "documentation.json"), $$"""{"report":"{{bare}}"}""", cancellationToken);
                    built.Add(new { name = package.Name, version = package.Version, bareName = bare, directory = folder, link = Links.GetValueOrDefault(bare, []), built = true });
                }

                return Printed([], new { packages = built });
            }

            private static PmjResult Printed(IReadOnlyList<PackageProblem> problems, object rest)
            {
                var ok = !problems.Any(problem => problem.IsError);
                var envelope = new Dictionary<string, object?>
                {
                    ["ok"] = ok,
                    ["diagnostics"] = problems.Select(problem => new { severity = problem.Severity, code = problem.Code, message = problem.Message, reason = problem.Reason, fix = problem.Fix }),
                    ["omittedDiagnostics"] = 0,
                };
                foreach (var property in JsonSerializer.SerializeToElement(rest).EnumerateObject())
                    envelope[property.Name] = property.Value;

                return new PmjResult(PmjOutcome.Ran, ok ? 0 : 1, JsonSerializer.Serialize(envelope), string.Empty);
            }
        }
    }
}
