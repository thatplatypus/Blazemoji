using System.Diagnostics;
using Blazemoji.Toolchain;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// Per-run limits are applied with prlimit, which exists on Linux only.
    /// </summary>
    public class ResourceLimitTests
    {
        private const string LinuxOnly = "Resource limits are applied with prlimit, which needs Linux. Run the Docker tests.";

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_program_that_burns_cpu_is_ended_at_the_cpu_limit()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), LinuxOnly);
            await using var toolchain = ToolchainFixture.Create(options => options.CpuSeconds = 1);
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "while :; do :; done");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            var clock = Stopwatch.StartNew();
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.Exited);
            finished.Exit.ExitCode.ShouldNotBe(0);
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
        }

        [Fact]
        public async Task A_program_that_asks_for_too_much_memory_is_refused_it()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), LinuxOnly);
            await using var toolchain = ToolchainFixture.Create(options => options.MemoryBytes = 256L * 1024 * 1024);
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "data=$(head -c 600000000 /dev/zero | tr '\\0' 'a'); echo \"held ${#data}\"");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Stdout.ShouldNotContain("held 600000000");
            finished.Exit.ExitCode.ShouldNotBe(0);
        }

        [Fact]
        public async Task A_program_cannot_write_a_file_larger_than_the_file_limit()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), LinuxOnly);
            await using var toolchain = ToolchainFixture.Create(options => options.MaxFileBytes = 1024 * 1024);
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "head -c 5000000 /dev/zero > big.bin; wc -c < big.bin");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            long.Parse(finished.Stdout.Trim()).ShouldBeLessThanOrEqualTo(1024 * 1024);
        }

        [Fact]
        public async Task A_program_inside_its_limits_is_unaffected()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), LinuxOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "echo fine > note.txt; cat note.txt");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Stdout.ShouldBe("fine\n");
            finished.Exit.ExitCode.ShouldBe(0);
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task A_compiled_program_that_never_yields_is_ended_at_the_cpu_limit()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create(options => options.CpuSeconds = 1);
            var build = await toolchain.CompileAsync(ToolchainFixture.SingleFile("🏁 🍇\n  🔁 👍 🍇🍉\n🍉\n"), Cancellation);
            build.Ok.ShouldBeTrue();

            await using var run = await toolchain.StartRunAsync(new RunRequest(build.BuildId!), Cancellation);
            var clock = Stopwatch.StartNew();
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.Exited);
            finished.Exit.ExitCode.ShouldNotBe(0);
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
        }
    }
}
