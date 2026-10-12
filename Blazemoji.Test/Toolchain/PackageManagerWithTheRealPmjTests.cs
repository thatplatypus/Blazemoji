using System.Security.Cryptography;
using Blazemoji.Services.Packages;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// The package manager with the real pmj: what is locked is fetched from a stand-in for
    /// GitHub on this machine, kept in pmj's cache, and opened from there.
    /// </summary>
    public sealed class PackageManagerWithTheRealPmjTests : IAsyncDisposable
    {
        private const string WordsName = "@thatplatypus/hello_words";
        private const string GreeterName = "@thatplatypus/hello_greeter";
        private const string Version = "0.1.0";

        private readonly string _home = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "host-home-" + Guid.NewGuid().ToString("N"));
        private readonly List<string> _asked = [];
        private WebApplication? _gitHub;

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static string Sample(params string[] parts) =>
            Path.Combine([AppContext.BaseDirectory, "Toolchain", "Packages", "hello", .. parts]);

        public async ValueTask DisposeAsync()
        {
            if (_gitHub is not null)
                await _gitHub.DisposeAsync();

            if (Directory.Exists(_home))
            {
                // pmj marks what it caches as not to be written, and a folder holding such a file cannot be removed until it is.
                foreach (var entry in new DirectoryInfo(_home).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                    entry.Attributes &= ~FileAttributes.ReadOnly;

                Directory.Delete(_home, recursive: true);
            }
        }

        /// <summary>A GitHub that has one repository, with the two packages released in it.</summary>
        private async Task<string> StandInForGitHubAsync()
        {
            var words = await PmjFixture.PackAsync(Sample("words"), WordsName, Version, Cancellation);
            var greeter = await PmjFixture.PackAsync(Sample("greeter"), GreeterName, Version, Cancellation);
            var files = new Dictionary<string, byte[]>
            {
                ["hello_words-0.1.0.pmj.tar.gz"] = words.Archive,
                ["hello_greeter-0.1.0.pmj.tar.gz"] = greeter.Archive,
            };

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            _gitHub = builder.Build();
            _gitHub.MapGet("/{**path}", (string path) =>
            {
                lock (_asked)
                    _asked.Add(path);

                return path.StartsWith("thatplatypus/packmoji/", StringComparison.Ordinal) && files.TryGetValue(Path.GetFileName(path), out var bytes)
                    ? Results.Bytes(bytes, "application/gzip")
                    : Results.NotFound();
            });
            await _gitHub.StartAsync(Cancellation);
            return _gitHub.Urls.First();
        }

        private PmjPackageManager Manager(string gitHub, string scopes = "thatplatypus") =>
            new(
                new ToldWhereGitHubIs(PmjFixture.Create(), gitHub),
                Options.Create(new PackmojiOptions { Path = PmjFixture.Path!, Home = _home, Scopes = scopes }),
                NullLogger<PmjPackageManager>.Instance);

        private static async Task<(string Manifest, string Lockfile)> ProjectAsync() => (
            await File.ReadAllTextAsync(Sample("app", "packmoji.json"), Cancellation),
            await File.ReadAllTextAsync(Sample("app", "packmoji.lock"), Cancellation));

        private string Asked()
        {
            lock (_asked)
                return "asked of the stand-in: " + string.Join(", ", _asked);
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        [Trait("Requires", "Packmoji")]
        public async Task What_is_locked_is_fetched_once_and_then_opened_from_pmjs_cache_with_GitHub_gone()
        {
            Assert.SkipUnless(PmjFixture.Available, PmjFixture.SkipReason);
            var manager = Manager(await StandInForGitHubAsync());
            var (manifest, lockfile) = await ProjectAsync();

            var restore = await manager.RestoreAsync(manifest, lockfile, Cancellation);

            var packages = restore.ShouldBeOfType<PackageRestore.Restored>(Asked()).Packages;
            packages.Select(package => package.Name).ShouldBe([GreeterName, WordsName]);
            foreach (var package in packages)
            {
                await using var archive = manager.OpenArchive(package).ShouldNotBeNull();
                Convert.ToHexStringLower(await SHA256.HashDataAsync(archive, Cancellation)).ShouldBe(package.Sha256);
            }

            await _gitHub!.StopAsync(Cancellation);
            (await manager.RestoreAsync(manifest, lockfile, Cancellation)).ShouldBeOfType<PackageRestore.Restored>();
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        [Trait("Requires", "Packmoji")]
        public async Task A_manifest_that_asks_for_more_than_the_lockfile_holds_is_out_of_date()
        {
            Assert.SkipUnless(PmjFixture.Available, PmjFixture.SkipReason);
            var manager = Manager(await StandInForGitHubAsync());
            var (manifest, lockfile) = await ProjectAsync();
            var asksForMore = manifest.Replace("\"dependencies\": {", "\"dependencies\": {\n    \"@thatplatypus/another\": \"1.0.0\",");
            asksForMore.ShouldNotBe(manifest, "the sample manifest no longer has the line this test edits");

            (await manager.RestoreAsync(asksForMore, lockfile, Cancellation)).ShouldBeOfType<PackageRestore.OutOfDate>();
            (await manager.RestoreAsync(manifest, null, Cancellation)).ShouldBeOfType<PackageRestore.OutOfDate>();
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        [Trait("Requires", "Packmoji")]
        public async Task The_real_pmj_refuses_a_package_outside_the_scopes_before_it_asks_for_anything()
        {
            Assert.SkipUnless(PmjFixture.Available, PmjFixture.SkipReason);
            var manager = Manager(await StandInForGitHubAsync(), scopes: "emojicode");
            var (manifest, lockfile) = await ProjectAsync();

            var failed = (await manager.RestoreAsync(manifest, lockfile, Cancellation)).ShouldBeOfType<PackageRestore.Failed>();

            failed.Problems.ShouldNotBeEmpty();
            failed.Problems.ShouldAllBe(problem => problem.Code == "scope.not-allowed");
            lock (_asked)
                _asked.ShouldBeEmpty();
            (Directory.Exists(_home) ? Directory.GetFiles(_home, "*.pmj.tar.gz", SearchOption.AllDirectories) : []).ShouldBeEmpty();
        }

        /// <summary>The real pmj, sent to the stand-in for both of GitHub's addresses.</summary>
        private sealed class ToldWhereGitHubIs(IPmj pmj, string gitHub) : IPmj
        {
            public Task<PmjResult> RunAsync(PmjCommand command, CancellationToken cancellationToken = default)
            {
                var told = new Dictionary<string, string>(command.Environment ?? new Dictionary<string, string>())
                {
                    [PmjVariables.GitHub] = gitHub,
                    [PmjVariables.GitHubApi] = gitHub,
                };
                return pmj.RunAsync(command with { Environment = told }, cancellationToken);
            }
        }
    }
}
