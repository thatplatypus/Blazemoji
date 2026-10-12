using System.Text.RegularExpressions;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// packmoji.pin says which pmj this repository fetches and which bytes it must be. It is
    /// a copy of the file of digests that Packmoji's release carries.
    /// </summary>
    public partial class PackmojiPinTests
    {
        internal static string[] Lines() =>
            File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "packmoji.pin")).Where(line => line.Length > 0).ToArray();

        /// <summary>The version every line of the pin names.</summary>
        internal static string Version() =>
            Lines().Select(line => PinLine().Match(line).Groups["version"].Value).Distinct().ShouldHaveSingleItem();

        [Fact]
        public void The_pin_names_one_version_of_pmj_for_the_three_machines_Packmoji_releases_for()
        {
            var lines = Lines();

            lines.ShouldAllBe(line => PinLine().IsMatch(line));
            lines.Select(line => PinLine().Match(line).Groups["machine"].Value).Order().ShouldBe(["linux-x64", "osx-arm64", "win-x64.exe"]);
            Version().ShouldMatch(@"^\d+\.\d+\.\d+$");
        }

        [GeneratedRegex(@"^[0-9a-f]{64}  pmj-(?<version>[0-9A-Za-z.+-]+?)-(?<machine>linux-x64|osx-arm64|win-x64\.exe)$")]
        private static partial Regex PinLine();
    }
}
