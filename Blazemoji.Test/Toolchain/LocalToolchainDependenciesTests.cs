using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Local;

namespace Blazemoji.Test.Toolchain
{
    public class LocalToolchainDependenciesTests
    {
        private const string SetId = "the-set";

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static CompileRequest Request(string? dependencies, bool checkOnly = false) =>
            new(new Dictionary<string, string> { ["main.🍇"] = "🏁 🍇 🍉" }, "main.🍇", checkOnly, dependencies);

        private static string[] Lines(string file) =>
            File.Exists(file) ? File.ReadAllLines(file) : [];

        [Fact]
        public async Task A_set_s_folder_is_searched_ahead_of_the_stock_packages_and_its_archives_and_libraries_are_linked()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var tools = new Tools();
            var store = new OneSet(SetId, "/the/set/packages", ["/the/set/packages/a/liba.a", "/the/set/packages/b/libb.a"], ["curl", "z"]);
            await using var toolchain = ToolchainFixture.Create(tools.Use, store);

            var result = await toolchain.CompileAsync(Request(SetId), Cancellation);

            result.Ok.ShouldBeTrue(string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
            result.DependenciesMissing.ShouldBeFalse();
            Lines(tools.CompilerArguments)[^4..].ShouldBe(["-S", "/the/set/packages", "-S", tools.Stock]);

            var linked = Lines(tools.LinkerArguments);
            linked[1..^2].ShouldBe(
            [
                "-Wl,--start-group",
                Path.Combine(tools.Stock, "s", "libs.a"),
                "/the/set/packages/a/liba.a",
                "/the/set/packages/b/libb.a",
                "-Wl,--end-group",
                "-lcurl",
                "-lz",
                "-lm",
                "-lpthread",
            ]);
            linked[^2].ShouldBe("-o");
        }

        [Fact]
        public async Task A_set_is_held_for_the_compile_and_let_go_after_it()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var tools = new Tools();
            var store = new OneSet(SetId, "/the/set/packages", [], []);
            await using var toolchain = ToolchainFixture.Create(tools.Use, store);

            await toolchain.CompileAsync(Request(SetId), Cancellation);
            await toolchain.CompileAsync(Request(SetId, checkOnly: true), Cancellation);

            store.Leased.ShouldBe(2);
            store.Released.ShouldBe(2);
        }

        [Fact]
        public async Task A_compile_that_names_a_set_this_toolchain_does_not_hold_says_so_and_compiles_nothing()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var tools = new Tools();
            await using var toolchain = ToolchainFixture.Create(tools.Use, new OneSet(SetId, "/the/set/packages", [], []));

            var result = await toolchain.CompileAsync(Request("another-set"), Cancellation);

            result.Ok.ShouldBeFalse();
            result.BuildId.ShouldBeNull();
            result.DependenciesMissing.ShouldBeTrue();
            result.ReachedCompiler.ShouldBeFalse();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("The project's packages are not built here.");
            File.Exists(tools.CompilerArguments).ShouldBeFalse();
        }

        [Fact]
        public async Task A_toolchain_with_no_sets_at_all_says_the_same()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var tools = new Tools();
            await using var toolchain = ToolchainFixture.Create(tools.Use);

            var result = await toolchain.CompileAsync(Request(SetId), Cancellation);

            result.DependenciesMissing.ShouldBeTrue();
            File.Exists(tools.CompilerArguments).ShouldBeFalse();
        }

        [Fact]
        public async Task A_compile_that_names_no_set_is_as_it_was()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var tools = new Tools();
            var store = new OneSet(SetId, "/the/set/packages", ["/the/set/packages/a/liba.a"], ["curl"]);
            await using var toolchain = ToolchainFixture.Create(tools.Use, store);

            var result = await toolchain.CompileAsync(Request(null), Cancellation);

            result.Ok.ShouldBeTrue();
            store.Leased.ShouldBe(0);
            Lines(tools.CompilerArguments).Count(argument => argument == "-S").ShouldBe(1);
            Lines(tools.LinkerArguments)[1..^2].ShouldBe(
            [
                "-Wl,--start-group",
                Path.Combine(tools.Stock, "s", "libs.a"),
                "-Wl,--end-group",
                "-lm",
                "-lpthread",
            ]);
        }

        [Fact]
        public async Task A_set_of_no_packages_adds_nothing_to_search_or_to_link()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var tools = new Tools();
            await using var toolchain = ToolchainFixture.Create(tools.Use, new OneSet(SetId, null, [], []));

            var result = await toolchain.CompileAsync(Request(SetId), Cancellation);

            result.Ok.ShouldBeTrue();
            Lines(tools.CompilerArguments).Count(argument => argument == "-S").ShouldBe(1);
        }

        /// <summary>A compiler and a linker that write down their arguments, and a stock packages folder with one archive.</summary>
        private sealed class Tools
        {
            private readonly string _folder = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "tools-" + Guid.NewGuid().ToString("N"));

            public Tools()
            {
                Directory.CreateDirectory(Path.Combine(Stock, "s"));
                File.WriteAllText(Path.Combine(Stock, "s", "libs.a"), "archive");
            }

            public string Stock => Path.Combine(_folder, "stock");

            public string CompilerArguments => Path.Combine(_folder, "compiler-arguments");

            public string LinkerArguments => Path.Combine(_folder, "linker-arguments");

            public void Use(ToolchainOptions options)
            {
                options.PackagesPath = Stock;
                options.CompilerPath = ToolchainFixture.Script("fake-compiler", $"printf '%s\\n' \"$@\" > '{CompilerArguments}'\necho '[]'\n: > program.o");

                // The program is the last argument: the loop leaves it in $last.
                options.LinkerPath = ToolchainFixture.Script("fake-linker", $"printf '%s\\n' \"$@\" > '{LinkerArguments}'\nfor last; do :; done\n: > \"$last\"");
            }
        }

        private sealed class OneSet(string id, string? packagesDirectory, IReadOnlyList<string> archives, IReadOnlyList<string> libraries) : IDependencySetStore
        {
            public int Leased { get; private set; }

            public int Released { get; private set; }

            public DependencySetLease? Lease(string asked)
            {
                if (asked != id)
                    return null;

                Leased++;
                return new DependencySetLease(packagesDirectory, archives, libraries, () => Released++);
            }
        }
    }
}
