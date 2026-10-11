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
    }
}
