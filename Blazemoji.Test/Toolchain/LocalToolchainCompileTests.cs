using Blazemoji.Toolchain;

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
        public async Task A_compiler_that_exits_cleanly_without_producing_a_program_is_a_failed_build()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var fakeCompiler = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "fake-compiler-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.GetDirectoryName(fakeCompiler)!);
            File.WriteAllText(fakeCompiler, "#!/bin/sh\necho '[]'\necho 'ld: cannot find -lruntime' >&2\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(fakeCompiler, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await using var toolchain = ToolchainFixture.Create(options => options.CompilerPath = fakeCompiler);

            var result = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Hello), TestContext.Current.CancellationToken);

            result.Ok.ShouldBeFalse();
            result.BuildId.ShouldBeNull();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("The program could not be linked.");
            BuildDirectories(toolchain).ShouldBeEmpty();
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
