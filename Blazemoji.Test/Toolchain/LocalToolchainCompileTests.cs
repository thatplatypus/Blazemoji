using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Local;

namespace Blazemoji.Test.Toolchain
{
    public class LocalToolchainCompileTests
    {
        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task Compile_of_a_valid_program_is_ok_and_returns_a_build_id()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Hello), TestContext.Current.CancellationToken);

            result.Ok.ShouldBeTrue(Describe(result));
            result.Diagnostics.ShouldBeEmpty();
            result.BuildId.ShouldNotBeNullOrEmpty();
            File.Exists(Path.Combine(toolchain.WorkRoot, "builds", result.BuildId, "program")).ShouldBeTrue();
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task Compile_with_a_compiler_warning_is_still_ok()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.RttiWarning), TestContext.Current.CancellationToken);

            result.Ok.ShouldBeTrue(Describe(result));
            result.BuildId.ShouldNotBeNullOrEmpty();
            var warning = result.Diagnostics.ShouldHaveSingleItem();
            warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
            warning.Message.ShouldStartWith("Run-time type information");
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task Compile_error_returns_the_diagnostic_and_no_build()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.UndefinedVariable), TestContext.Current.CancellationToken);

            result.Ok.ShouldBeFalse();
            result.BuildId.ShouldBeNull();
            result.Diagnostics.ShouldHaveSingleItem().ShouldBe(
                new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Variable \"nope\" not defined."));
            BuildDirectories(toolchain).ShouldBeEmpty();
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task An_error_on_the_first_line_is_reported_one_based_like_any_other_line()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            // The compiler itself counts characters on line 1 from zero and on later lines from one.
            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile("🏁 🍇 😀 nope❗️ 🍉\n"), TestContext.Current.CancellationToken);

            result.Diagnostics.ShouldHaveSingleItem().ShouldBe(
                new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 1, 7, "Variable \"nope\" not defined."));
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task Concurrent_compiles_get_separate_builds()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.PrintsMarker(i.ToString())), TestContext.Current.CancellationToken)));

            results.ShouldAllBe(r => r.Ok);
            results.Select(r => r.BuildId).Distinct().Count().ShouldBe(8);
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task ReleaseBuild_deletes_the_build_directory()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();
            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Hello), TestContext.Current.CancellationToken);

            await toolchain.ReleaseBuildAsync(result.BuildId!);

            BuildDirectories(toolchain).ShouldBeEmpty();
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task Stale_builds_are_swept_on_the_next_compile()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create(options => options.BuildLifetime = TimeSpan.Zero);
            var first = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Hello), TestContext.Current.CancellationToken);

            var second = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Hello), TestContext.Current.CancellationToken);

            BuildDirectories(toolchain).ShouldBe([second.BuildId!]);
            first.BuildId.ShouldNotBe(second.BuildId);
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task Compile_that_outlives_its_timeout_fails_without_leaving_a_build()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create(options => options.CompileTimeout = TimeSpan.FromMilliseconds(1));

            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Hello), TestContext.Current.CancellationToken);

            result.Ok.ShouldBeFalse();
            result.BuildId.ShouldBeNull();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("The compiler took too long and was stopped.");
            BuildDirectories(toolchain).ShouldBeEmpty();
        }

        [Theory]
        [InlineData("../evil.🍇")]
        [InlineData("/tmp/evil.🍇")]
        [InlineData("a/../../evil.🍇")]
        public async Task Compile_rejects_unsafe_file_names(string name)
        {
            await using var toolchain = ToolchainFixture.Create();
            var request = new CompileRequest(new Dictionary<string, string> { [name] = Programs.Hello }, name);

            var result = await toolchain.CompileAsync(request, TestContext.Current.CancellationToken);

            result.Ok.ShouldBeFalse();
            result.BuildId.ShouldBeNull();
            result.Diagnostics.ShouldHaveSingleItem().Severity.ShouldBe(DiagnosticSeverity.Error);
            BuildDirectories(toolchain).ShouldBeEmpty();
        }

        [Fact]
        public async Task Two_toolchains_given_the_same_work_root_do_not_delete_each_others_builds()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var sharedRoot = Path.Combine(Path.GetTempPath(), "blazemoji-tests", Guid.NewGuid().ToString("N"));
            var first = ToolchainFixture.Create(options => options.WorkRoot = sharedRoot);
            await using var second = ToolchainFixture.Create(options => options.WorkRoot = sharedRoot);
            var buildId = ToolchainFixture.ScriptBuild(second, "echo still here");

            await first.DisposeAsync();

            second.HasBuild(buildId).ShouldBeTrue();
        }

        [Fact]
        public async Task Compile_rejects_an_entry_that_is_not_among_the_files()
        {
            await using var toolchain = ToolchainFixture.Create();
            var request = new CompileRequest(new Dictionary<string, string> { ["main.🍇"] = Programs.Hello }, "other.🍇");

            var result = await toolchain.CompileAsync(request, TestContext.Current.CancellationToken);

            result.Ok.ShouldBeFalse();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("The entry file is not among the files.");
        }

        [Fact]
        public async Task A_compiler_that_exits_cleanly_without_producing_an_object_is_a_failed_build()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var fakeCompiler = ToolchainFixture.Script("fake-compiler", "echo '[]'");
            var linkerRan = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "linker-ran-" + Guid.NewGuid().ToString("N"));
            var fakeLinker = ToolchainFixture.Script("fake-linker", $": > '{linkerRan}'");
            await using var toolchain = ToolchainFixture.Create(options =>
            {
                options.CompilerPath = fakeCompiler;
                options.LinkerPath = fakeLinker;
            });

            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Hello), TestContext.Current.CancellationToken);

            result.Ok.ShouldBeFalse();
            result.BuildId.ShouldBeNull();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("The compiler failed without reporting an error.");
            File.Exists(linkerRan).ShouldBeFalse();
            BuildDirectories(toolchain).ShouldBeEmpty();
        }

        [Fact]
        public async Task A_check_only_compile_reports_success_without_linking_or_keeping_a_build()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var linkerRan = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "linker-ran-" + Guid.NewGuid().ToString("N"));
            await using var toolchain = ToolchainFixture.Create(options =>
            {
                options.CompilerPath = ToolchainFixture.CompilerThatSucceeds();
                options.LinkerPath = ToolchainFixture.Script("fake-linker", $": > '{linkerRan}'\n: > program");
            });

            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Hello) with { CheckOnly = true }, TestContext.Current.CancellationToken);

            result.Ok.ShouldBeTrue(Describe(result));
            result.BuildId.ShouldBeNull();
            File.Exists(linkerRan).ShouldBeFalse();
            BuildDirectories(toolchain).ShouldBeEmpty();
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task A_check_only_compile_reports_the_same_diagnostics_as_a_build()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var broken = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.UndefinedVariable) with { CheckOnly = true }, TestContext.Current.CancellationToken);
            var fine = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.RttiWarning) with { CheckOnly = true }, TestContext.Current.CancellationToken);

            broken.Ok.ShouldBeFalse();
            broken.Diagnostics.ShouldHaveSingleItem().ShouldBe(
                new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Variable \"nope\" not defined."));
            fine.Ok.ShouldBeTrue(Describe(fine));
            fine.BuildId.ShouldBeNull();
            fine.Diagnostics.ShouldHaveSingleItem().Severity.ShouldBe(DiagnosticSeverity.Warning);
            BuildDirectories(toolchain).ShouldBeEmpty();
        }

        [Fact]
        public async Task A_linker_that_fails_is_a_failed_build_with_a_fixed_message()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var fakeLinker = ToolchainFixture.Script("fake-linker", "echo 'undefined reference to secretSymbol' >&2\nexit 1");
            await using var toolchain = ToolchainFixture.Create(options =>
            {
                options.CompilerPath = ToolchainFixture.CompilerThatSucceeds();
                options.LinkerPath = fakeLinker;
            });

            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Hello), TestContext.Current.CancellationToken);

            result.Ok.ShouldBeFalse();
            result.BuildId.ShouldBeNull();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("The program could not be linked.");
            BuildDirectories(toolchain).ShouldBeEmpty();
        }

        [Fact]
        public async Task A_linker_that_exits_cleanly_without_producing_a_program_is_a_failed_build()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create(options =>
            {
                options.CompilerPath = ToolchainFixture.CompilerThatSucceeds();
                options.LinkerPath = ToolchainFixture.Script("fake-linker", "exit 0");
            });

            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Hello), TestContext.Current.CancellationToken);

            result.Ok.ShouldBeFalse();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("The program could not be linked.");
            BuildDirectories(toolchain).ShouldBeEmpty();
        }

        [Fact]
        public async Task The_linker_gets_the_object_then_every_package_archive_in_one_group_then_the_system_libraries()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var scratch = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "link-" + Guid.NewGuid().ToString("N"));
            var packages = Path.Combine(scratch, "packages");
            foreach (var package in new[] { "json", "grapevine", "s" })
            {
                Directory.CreateDirectory(Path.Combine(packages, package));
                File.WriteAllText(Path.Combine(packages, package, $"lib{package}.a"), string.Empty);
                File.WriteAllText(Path.Combine(packages, package, "🏛"), string.Empty);
            }

            var recorded = Path.Combine(scratch, "arguments");
            var fakeLinker = ToolchainFixture.Script("fake-linker", $"printf '%s\\n' \"$@\" > '{recorded}'\n: > program");
            await using var toolchain = ToolchainFixture.Create(options =>
            {
                options.CompilerPath = ToolchainFixture.CompilerThatSucceeds();
                options.LinkerPath = fakeLinker;
                options.PackagesPath = packages;
            });

            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Hello), TestContext.Current.CancellationToken);

            result.Ok.ShouldBeTrue(Describe(result));
            var build = Path.Combine(toolchain.WorkRoot, "builds", result.BuildId!);
            File.ReadAllLines(recorded).ShouldBe(
            [
                Path.Combine(build, "program.o"),
                "-Wl,--start-group",
                Path.Combine(packages, "grapevine", "libgrapevine.a"),
                Path.Combine(packages, "json", "libjson.a"),
                Path.Combine(packages, "s", "libs.a"),
                "-Wl,--end-group",
                "-lm",
                "-lpthread",
                "-o",
                Path.Combine(build, "program"),
            ]);
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task An_error_in_an_included_file_names_that_file_by_its_path_in_the_project()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();
            var files = new Dictionary<string, string>
            {
                ["main.🍇"] = "📜 🔤lib/greeting.🍇🔤\n\n🏁 🍇\n  😀 🔤hi🔤❗️\n🍉\n",
                ["lib/greeting.🍇"] = "🐇 🙋 🍇\n  ❗️ 😀 🍇\n    😀 nope❗️\n  🍉\n🍉\n",
            };

            var result = await toolchain.CompileAsync(new CompileRequest(files, "main.🍇"), TestContext.Current.CancellationToken);

            result.Ok.ShouldBeFalse();
            var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
            diagnostic.File.ShouldBe("lib/greeting.🍇");
            diagnostic.Line.ShouldBe(3);
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task An_error_in_a_file_included_through_a_parent_folder_names_its_plain_path()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();
            var files = new Dictionary<string, string>
            {
                ["app/main.🍇"] = "📜 🔤../shared/util.🍇🔤\n\n🏁 🍇\n  😀 🔤hi🔤❗️\n🍉\n",
                ["shared/util.🍇"] = "🐇 🙋 🍇\n  ❗️ 😀 🍇\n    😀 nope❗️\n  🍉\n🍉\n",
            };

            var result = await toolchain.CompileAsync(new CompileRequest(files, "app/main.🍇"), TestContext.Current.CancellationToken);

            result.Ok.ShouldBeFalse();
            result.Diagnostics.ShouldHaveSingleItem().File.ShouldBe("shared/util.🍇");
        }

        [Fact]
        public async Task A_missing_compiler_is_reported_without_internal_detail()
        {
            await using var toolchain = ToolchainFixture.Create(options => options.CompilerPath = "/nonexistent/emojicodec");

            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Hello), TestContext.Current.CancellationToken);

            result.Ok.ShouldBeFalse();
            result.BuildId.ShouldBeNull();
            var message = result.Diagnostics.ShouldHaveSingleItem().Message;
            message.ShouldBe("The compiler could not be started.");
            message.ShouldNotContain("nonexistent");
            BuildDirectories(toolchain).ShouldBeEmpty();
        }

        [Theory]
        [InlineData("../../etc")]
        [InlineData("not-a-build-id")]
        [InlineData("")]
        public async Task ReleaseBuild_ignores_ids_it_did_not_issue(string buildId)
        {
            await using var toolchain = ToolchainFixture.Create();

            await Should.NotThrowAsync(() => toolchain.ReleaseBuildAsync(buildId));
        }

        private static string[] BuildDirectories(LocalToolchain toolchain)
        {
            var builds = Path.Combine(toolchain.WorkRoot, "builds");
            return Directory.Exists(builds)
                ? Directory.GetDirectories(builds).Select(path => Path.GetFileName(path)!).ToArray()
                : [];
        }

        private static string Describe(CompileResult result) =>
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Severity} {d.Line}:{d.Character} {d.Message}"));
    }
}
