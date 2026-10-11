using System.Security.Cryptography;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// The real pmj, where there is one. The test image holds it and says where through the
    /// same variable the toolchain service is configured by.
    /// </summary>
    internal static class PmjFixture
    {
        public const string SkipReason = "There is no pmj here. Run scripts/test-in-docker.sh, whose image holds one where Packmoji__Path says.";

        public static string? Path => Environment.GetEnvironmentVariable("Packmoji__Path");

        public static bool Available => Path is { Length: > 0 } path && File.Exists(path);

        public static PmjProcess Create(string? home = null) =>
            new(Options.Create(new PackmojiOptions { Path = Path!, Home = home }), NullLogger<PmjProcess>.Instance);

        /// <summary>
        /// Packs a package with the real pmj and gives its archive and what a lockfile would
        /// say of it. Packing writes into the folder, so it is done in a copy.
        /// </summary>
        public static async Task<(LockedPackage Package, byte[] Archive)> PackAsync(string folder, string name, string version, CancellationToken cancellationToken)
        {
            PackageNames.TryParse(name, out var ignored, out var bare).ShouldBeTrue();

            var copy = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "blazemoji-tests", "pack-" + Guid.NewGuid().ToString("N"));
            CopyFolder(folder, copy);
            try
            {
                var result = await Create().RunAsync(new PmjCommand(copy, ["pack"]), cancellationToken);
                result.Outcome.ShouldBe(PmjOutcome.Ran);
                result.ExitCode.ShouldBe(0, result.Stderr);

                var archive = await File.ReadAllBytesAsync(System.IO.Path.Combine(copy, "target", $"{bare}-{version}.pmj.tar.gz"), cancellationToken);
                return (new LockedPackage(name, version, Convert.ToHexStringLower(SHA256.HashData(archive))), archive);
            }
            finally
            {
                Directory.Delete(copy, recursive: true);
            }
        }

        private static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
                File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)));

            foreach (var folder in Directory.GetDirectories(from))
                CopyFolder(folder, System.IO.Path.Combine(to, System.IO.Path.GetFileName(folder)));
        }
    }
}
