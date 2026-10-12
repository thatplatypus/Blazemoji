using Blazemoji.Toolchain.Local;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// The packages every program is linked against are the compiler's own. A package of a
    /// project comes locked, in a set, and never by being put in this folder. One left there,
    /// as a script used to leave Grapevine, is linked into every program on that machine, and
    /// lets a project with no manifest import it there and nowhere else.
    /// </summary>
    public class StockPackagesTests
    {
        [Fact]
        public void The_stock_packages_are_the_ones_the_compiler_came_with_and_no_others()
        {
            // A folder with nothing in it is what a build leaves of one that has been removed
            // from the source, and nothing of it can be linked.
            var folders = Directory.GetDirectories(new ToolchainOptions().PackagesPath)
                .Where(folder => Directory.EnumerateFileSystemEntries(folder).Any())
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal);

            folders.ShouldBe(["files", "json", "runtime", "s", "sockets", "testtube"]);
        }
    }
}
