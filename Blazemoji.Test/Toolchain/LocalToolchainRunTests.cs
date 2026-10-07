using System.Diagnostics;
using Blazemoji.Toolchain;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// Behaviour that depends on real Emojicode binaries.
    /// </summary>
    [Trait("Category", "Toolchain")]
    public class LocalToolchainRunTests
    {
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_compiled_program_prints_and_exits_with_zero()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var finished = await toolchain.CompileAndRunAsync(Programs.Hello, Cancellation);

            finished.Stdout.ShouldBe("Hello World!\n");
            finished.Stderr.ShouldBeEmpty();
            finished.Exit.ExitCode.ShouldBe(0);
            finished.Exit.Reason.ShouldBe(RunEndReason.Exited);
        }

        [Fact]
        public async Task A_program_built_with_a_compiler_warning_runs()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var finished = await toolchain.CompileAndRunAsync(Programs.RttiWarning, Cancellation);

            finished.Stdout.ShouldBe("stored\n");
            finished.Exit.ExitCode.ShouldBe(0);
        }

        [Fact]
        public async Task A_program_can_choose_its_exit_code()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var finished = await toolchain.CompileAndRunAsync(Programs.ExitsWith(3), Cancellation);

            finished.Stdout.ShouldBe("before exit\n");
            finished.Exit.ExitCode.ShouldBe(3);
        }

        [Fact]
        public async Task More_than_a_megabyte_of_output_completes()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var finished = await toolchain.CompileAndRunAsync(Programs.OneMegabyte, Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.Exited);
            finished.Exit.ExitCode.ShouldBe(0);
            finished.Stdout.Length.ShouldBe(20_000 * 65);
        }

        [Fact]
        public async Task Output_of_a_compiled_program_arrives_line_by_line_while_it_runs()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();
            var build = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.SlowTwoLines), Cancellation);
            build.Ok.ShouldBeTrue();

            await using var run = await toolchain.StartRunAsync(new RunRequest(build.BuildId!), Cancellation);
            var clock = Stopwatch.StartNew();
            var arrivals = new List<(string Text, TimeSpan At)>();
            await foreach (var runEvent in run.ReadEventsAsync(Cancellation))
            {
                if (runEvent is StdoutEvent output)
                    arrivals.Add((output.Text, clock.Elapsed));
            }

            arrivals.Select(a => a.Text).ShouldBe(["one\n", "two\n"]);
            (arrivals[1].At - arrivals[0].At).ShouldBeGreaterThan(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task A_panic_delivers_its_message_and_the_abort_exit_code()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var finished = await toolchain.CompileAndRunAsync(Programs.Crashes, Cancellation);

            // Emojicode writes its panic message to stdout, not stderr, then aborts.
            finished.Stdout.ShouldBe(
                "before the crash\n🤯 Program panicked: Unwrapped an optional that contained no value. (main.🍇:3:5)\n");
            finished.Stderr.ShouldBeEmpty();
            finished.Exit.Reason.ShouldBe(RunEndReason.Exited);
            finished.Exit.ExitCode.ShouldBe(134);
        }

        [Fact]
        public async Task A_compiled_program_reads_its_environment()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();
            var build = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.PrintsEnvironment), Cancellation);
            build.Ok.ShouldBeTrue();
            var environment = new Dictionary<string, string> { ["BLAZEMOJI_TEST"] = "from the request" };

            await using var run = await toolchain.StartRunAsync(new RunRequest(build.BuildId!, environment), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Stdout.ShouldBe("from the request\n");
        }

        [Fact]
        public async Task A_compiled_program_that_never_ends_is_ended_at_the_time_limit()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();
            var build = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.Forever), Cancellation);
            build.Ok.ShouldBeTrue();

            await using var run = await toolchain.StartRunAsync(new RunRequest(build.BuildId!, Timeout: TimeSpan.FromSeconds(2)), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.TimedOut);
        }

        [Fact]
        public async Task Concurrent_runs_each_get_their_own_output()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var finished = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                toolchain.CompileAndRunAsync(Programs.PrintsMarker(i.ToString()), Cancellation)));

            finished.Select(f => f.Stdout).ShouldBe(Enumerable.Range(0, 8).Select(i => $"marker-{i}\n"));
        }
    }
}
