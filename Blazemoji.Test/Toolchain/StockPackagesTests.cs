using Blazemoji.Toolchain.Local;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// The packages every program is linked against are the compiler's own. A package of a
    /// project comes locked, in a set, and never by being put in this folder: one left
    /// there would be linked into every program beside a project's own copy of it.
    /// </summary>
    public class StockPackagesTests
    {
        [Fact]
        public void The_stock_packages_are_the_ones_the_compiler_came_with_and_no_others()
        {
            var folders = Directory.GetDirectories(new ToolchainOptions().PackagesPath).Select(Path.GetFileName).Order(StringComparer.Ordinal);

            folders.ShouldBe(["files", "json", "runtime", "s", "sockets", "testtube"]);
        }
    }
}
