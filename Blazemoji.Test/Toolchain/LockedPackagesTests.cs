using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Local;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// The real pmj and the real compiler: a program that imports locked packages, one of
    /// them with C++ in it, compiled, linked and run.
    /// </summary>
    public sealed class LockedPackagesTests : IAsyncDisposable
    {
        private const string SkipReason = "This needs the Emojicode compiler and pmj together. Run scripts/test-in-docker.sh.";
        private const string WordsName = "@thatplatypus/hello_words";
        private const string GreeterName = "@thatplatypus/hello_greeter";
        private const string Version = "0.1.0";
        private const string WordsDigest = "8502cea823dc837cf74985e796e3d5604035095ec715289ad5c10540c73759f8";
        private const string GreeterDigest = "9da45105dcb3746b7e01d8bd0e702ef3100c90be1be7aa650ea0a9367b8fae3a";

        private const string Program = """
            📦 hello_greeter 🏠

            🏁 🍇
              😀 💬🐇🙋 🔤Blazemoji🔤❗️❗️
              😀 🔡 🎲🐇🙋❗️ 10❗️❗️
            🍉

            """;

        private readonly string _work = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "locked-" + Guid.NewGuid().ToString("N"));
        private LocalDependencySets? _sets;
        private LocalToolchain? _toolchain;

        private static bool Available => ToolchainFixture.Available && PmjFixture.Available;

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static string Sample(params string[] parts) =>
            Path.Combine([AppContext.BaseDirectory, "Toolchain", "Packages", "hello", .. parts]);

        public async ValueTask DisposeAsync()
        {
            if (_toolchain is not null)
                await _toolchain.DisposeAsync();

            if (_sets is not null)
                await _sets.DisposeAsync();

            if (Directory.Exists(_work))
                Directory.Delete(_work, recursive: true);
        }

        private (LocalDependencySets Sets, LocalToolchain Toolchain) Create()
        {
            var options = Options.Create(new ToolchainOptions { WorkRoot = _work });
            _sets = new LocalDependencySets(
                options,
                Options.Create(new PackmojiOptions { Path = PmjFixture.Path! }),
                PmjFixture.Create(),
                NullLogger<LocalDependencySets>.Instance);
            _toolchain = new LocalToolchain(options, NullLogger<LocalToolchain>.Instance, _sets);
            return (_sets, _toolchain);
        }

        private static async Task<DependencyRequest> RequestAsync() => new(
            await File.ReadAllTextAsync(Sample("app", "packmoji.json"), Cancellation),
            await File.ReadAllTextAsync(Sample("app", "packmoji.lock"), Cancellation));

        private static CompileRequest Compile(string? dependencies, bool checkOnly = false) =>
            new(new Dictionary<string, string> { ["main.🍇"] = Program }, "main.🍇", checkOnly, dependencies);

        private static string Describe(CompileResult result) =>
            string.Join("; ", result.Diagnostics.Select(diagnostic => $"{diagnostic.File}:{diagnostic.Line}:{diagnostic.Character} {diagnostic.Message}"));

        [Fact]
        [Trait("Category", "Toolchain")]
        [Trait("Requires", "Packmoji")]
        public async Task The_sample_packages_pack_to_the_bytes_their_lockfile_holds()
        {
            Assert.SkipUnless(Available, SkipReason);

            var words = await PmjFixture.PackAsync(Sample("words"), WordsName, Version, Cancellation);
            var greeter = await PmjFixture.PackAsync(Sample("greeter"), GreeterName, Version, Cancellation);

            words.Package.Sha256.ShouldBe(WordsDigest);
            greeter.Package.Sha256.ShouldBe(GreeterDigest);
            var lockfile = await File.ReadAllTextAsync(Sample("app", "packmoji.lock"), Cancellation);
            lockfile.ShouldContain(WordsDigest);
            lockfile.ShouldContain(GreeterDigest);
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        [Trait("Requires", "Packmoji")]
        public async Task A_program_that_imports_locked_packages_compiles_links_and_runs()
        {
            Assert.SkipUnless(Available, SkipReason);
            var (sets, toolchain) = Create();
            var words = await PmjFixture.PackAsync(Sample("words"), WordsName, Version, Cancellation);
            var greeter = await PmjFixture.PackAsync(Sample("greeter"), GreeterName, Version, Cancellation);
            var request = await RequestAsync();

            var needed = (await sets.PrepareAsync(request, Cancellation)).ShouldBeOfType<DependencyAnswer.Needed>();
            needed.Archives.ShouldBe([greeter.Package, words.Package], ignoreOrder: true);

            (await sets.AddArchiveAsync(words.Package, new MemoryStream(words.Archive), Cancellation)).ShouldBe(ArchiveOutcome.Stored);
            (await sets.PrepareAsync(request, Cancellation)).ShouldBeOfType<DependencyAnswer.Needed>().Archives.ShouldBe([greeter.Package]);
            (await sets.AddArchiveAsync(greeter.Package, new MemoryStream(greeter.Archive), Cancellation)).ShouldBe(ArchiveOutcome.Stored);

            var ready = (await sets.PrepareAsync(request, Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();
            ready.Packages.ShouldBe(["hello_greeter", "hello_words"]);

            var build = await toolchain.CompileAsync(Compile(ready.Id), Cancellation);
            build.Ok.ShouldBeTrue(Describe(build));
            build.Diagnostics.ShouldBeEmpty();

            await using var run = await toolchain.StartRunAsync(new RunRequest(build.BuildId!), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);
            finished.Exit.ExitCode.ShouldBe(0);
            finished.Stdout.ShouldBe("Hello, Blazemoji!\n42\n");
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        [Trait("Requires", "Packmoji")]
        public async Task A_check_of_the_typing_finds_the_packages_too_and_without_them_the_import_is_what_fails()
        {
            Assert.SkipUnless(Available, SkipReason);
            var (sets, toolchain) = Create();
            var ready = await ReadyAsync(sets);

            var with = await toolchain.CompileAsync(Compile(ready.Id, checkOnly: true), Cancellation);
            var without = await toolchain.CompileAsync(Compile(null, checkOnly: true), Cancellation);

            with.Ok.ShouldBeTrue(Describe(with));
            with.BuildId.ShouldBeNull();
            without.Ok.ShouldBeFalse();
            without.DependenciesMissing.ShouldBeFalse();
            var missing = without.Diagnostics.ShouldHaveSingleItem();
            missing.Severity.ShouldBe(DiagnosticSeverity.Error);
            missing.File.ShouldBe("main.🍇");
            missing.Line.ShouldBe(1);
            missing.Message.ShouldBe("Could not find package hello_greeter.");
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        [Trait("Requires", "Packmoji")]
        public async Task The_compiler_s_report_of_a_built_package_is_kept_for_the_editor()
        {
            Assert.SkipUnless(Available, SkipReason);
            var (sets, _) = Create();
            var ready = await ReadyAsync(sets);

            var report = (await sets.GetDocumentationAsync(ready.Id, "hello_greeter", Cancellation)).ShouldNotBeNull();

            report.ShouldContain("🙋");
            report.ShouldContain("Greets by name");
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        [Trait("Requires", "Packmoji")]
        public async Task A_second_toolchain_on_the_same_machine_is_given_what_the_first_built()
        {
            Assert.SkipUnless(Available, SkipReason);
            var (sets, _) = Create();
            var first = await ReadyAsync(sets);

            await using var second = new LocalDependencySets(
                Options.Create(new ToolchainOptions { WorkRoot = _work }),
                Options.Create(new PackmojiOptions { Path = PmjFixture.Path! }),
                PmjFixture.Create(),
                NullLogger<LocalDependencySets>.Instance);
            var again = (await second.PrepareAsync(await RequestAsync(), Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();

            again.Id.ShouldBe(first.Id);
            using var lease = second.Lease(again.Id).ShouldNotBeNull();
            lease.Archives.ShouldAllBe(archive => File.Exists(archive));
            lease.LinkLibraries.ShouldBe(["m"]);
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        [Trait("Requires", "Packmoji")]
        public async Task An_archive_kept_under_another_packages_name_is_refused_and_nothing_is_built()
        {
            Assert.SkipUnless(Available, SkipReason);
            var (sets, _) = Create();
            var greeter = await PmjFixture.PackAsync(Sample("greeter"), GreeterName, Version, Cancellation);
            var request = await RequestAsync();

            // The lockfile says hello_words is the bytes that are really hello_greeter, and the
            // bytes are handed over under that name with their own true digest.
            var lying = request with { Lockfile = request.Lockfile.Replace(WordsDigest, GreeterDigest) };
            var asWords = new LockedPackage(WordsName, Version, GreeterDigest);
            (await sets.AddArchiveAsync(asWords, new MemoryStream(greeter.Archive), Cancellation)).ShouldBe(ArchiveOutcome.Stored);
            (await sets.AddArchiveAsync(greeter.Package, new MemoryStream(greeter.Archive), Cancellation)).ShouldBe(ArchiveOutcome.Stored);

            var failed = (await sets.PrepareAsync(lying, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            failed.Problems.ShouldNotBeEmpty();
            failed.Problems.ShouldAllBe(problem => problem.Message.Length > 0);
            Directory.Exists(Path.Combine(_work, "packmoji", "built")).ShouldBeFalse();
        }

        private static async Task<DependencyAnswer.Ready> ReadyAsync(LocalDependencySets sets)
        {
            var words = await PmjFixture.PackAsync(Sample("words"), WordsName, Version, Cancellation);
            var greeter = await PmjFixture.PackAsync(Sample("greeter"), GreeterName, Version, Cancellation);
            (await sets.AddArchiveAsync(words.Package, new MemoryStream(words.Archive), Cancellation)).ShouldBe(ArchiveOutcome.Stored);
            (await sets.AddArchiveAsync(greeter.Package, new MemoryStream(greeter.Archive), Cancellation)).ShouldBe(ArchiveOutcome.Stored);

            return (await sets.PrepareAsync(await RequestAsync(), Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();
        }
    }
}
