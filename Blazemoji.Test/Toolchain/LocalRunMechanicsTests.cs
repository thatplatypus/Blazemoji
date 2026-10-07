using System.Diagnostics;
using Blazemoji.Toolchain;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// How a run behaves, exercised with shell scripts in place of compiled programs so that
    /// these tests run wherever the test suite runs.
    /// </summary>
    public class LocalRunMechanicsTests
    {
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_run_reports_stdout_stderr_and_a_zero_exit_code()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "echo out; echo err >&2");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Stdout.ShouldBe("out\n");
            finished.Stderr.ShouldBe("err\n");
            finished.Exit.ExitCode.ShouldBe(0);
            finished.Exit.Reason.ShouldBe(RunEndReason.Exited);
            finished.EventsAfterExit.ShouldBe(0);
        }

        [Fact]
        public async Task A_run_reports_a_non_zero_exit_code()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "exit 3");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.ExitCode.ShouldBe(3);
            finished.Exit.Reason.ShouldBe(RunEndReason.Exited);
        }

        [Fact]
        public async Task Output_arrives_while_the_program_is_still_running()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "echo one; sleep 2; echo two");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            var clock = Stopwatch.StartNew();
            TimeSpan? firstOutput = null;
            TimeSpan? exit = null;
            await foreach (var runEvent in run.ReadEventsAsync(Cancellation))
            {
                if (runEvent is StdoutEvent)
                    firstOutput ??= clock.Elapsed;
                if (runEvent is ExitEvent)
                    exit = clock.Elapsed;
            }

            firstOutput.ShouldNotBeNull();
            exit.ShouldNotBeNull();
            (exit.Value - firstOutput.Value).ShouldBeGreaterThan(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task Stop_ends_a_running_program()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "echo started; sleep 60");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            var clock = Stopwatch.StartNew();
            ExitEvent? exit = null;
            await foreach (var runEvent in run.ReadEventsAsync(Cancellation))
            {
                if (runEvent is StdoutEvent)
                    await run.StopAsync();
                if (runEvent is ExitEvent exited)
                    exit = exited;
            }

            exit.ShouldNotBeNull().Reason.ShouldBe(RunEndReason.Stopped);
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task A_run_is_ended_at_its_timeout()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "sleep 60");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId, Timeout: TimeSpan.FromMilliseconds(500)), Cancellation);
            var clock = Stopwatch.StartNew();
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.TimedOut);
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task The_default_timeout_comes_from_the_options()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create(options => options.RunTimeout = TimeSpan.FromMilliseconds(500));
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "sleep 60");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.TimedOut);
        }

        [Fact]
        public async Task A_run_is_ended_at_the_output_limit()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            const int limit = 65_536;
            await using var toolchain = ToolchainFixture.Create(options => options.MaxOutputBytes = limit);
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "while true; do echo 0123456789012345678901234567890123456789; done");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.OutputLimit);
            finished.Stdout.Length.ShouldBeGreaterThan(0);
            finished.Stdout.Length.ShouldBeLessThan(limit * 2);
        }

        [Fact]
        public async Task Environment_variables_reach_the_program()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "echo \"$BLAZEMOJI_TEST\"");
            var environment = new Dictionary<string, string> { ["BLAZEMOJI_TEST"] = "from the request" };

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId, environment), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Stdout.ShouldBe("from the request\n");
        }

        [Fact]
        public async Task The_program_runs_in_its_own_directory_which_is_removed_on_dispose()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "pwd; echo data > written.txt");
            string workingDirectory;

            await using (var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation))
            {
                var finished = await run.RunToEndAsync(Cancellation);
                workingDirectory = finished.Stdout.Trim();
                workingDirectory.ShouldEndWith(Path.Combine("runs", run.RunId));
                File.Exists(Path.Combine(workingDirectory, "written.txt")).ShouldBeTrue();
            }

            Directory.Exists(workingDirectory).ShouldBeFalse();
        }

        [Fact]
        public async Task Disposing_a_run_stops_the_program()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "echo $$; sleep 60");
            int processId;

            await using (var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation))
            {
                await using var events = run.ReadEventsAsync(Cancellation).GetAsyncEnumerator(Cancellation);
                (await events.MoveNextAsync()).ShouldBeTrue();
                processId = int.Parse(events.Current.ShouldBeOfType<StdoutEvent>().Text.Trim());
            }

            Should.Throw<ArgumentException>(() => Process.GetProcessById(processId));
        }

        [Fact]
        public async Task Disposing_the_toolchain_stops_runs_that_are_still_going()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "echo $$; sleep 60");
            var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            await using var events = run.ReadEventsAsync(Cancellation).GetAsyncEnumerator(Cancellation);
            (await events.MoveNextAsync()).ShouldBeTrue();
            var processId = int.Parse(events.Current.ShouldBeOfType<StdoutEvent>().Text.Trim());

            await toolchain.DisposeAsync();

            Should.Throw<ArgumentException>(() => Process.GetProcessById(processId));
            Directory.Exists(toolchain.WorkRoot).ShouldBeFalse();
        }

        [Theory]
        [InlineData("0123456789abcdef0123456789abcdef")]
        [InlineData("../../bin")]
        [InlineData("")]
        public async Task A_run_of_an_unknown_build_fails_to_start(string buildId)
        {
            await using var toolchain = ToolchainFixture.Create();

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.FailedToStart);
            finished.Exit.ExitCode.ShouldBeNull();
        }

        [Fact]
        public async Task A_second_reader_is_refused()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "echo once");
            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            await run.RunToEndAsync(Cancellation);

            await Should.ThrowAsync<InvalidOperationException>(() => run.RunToEndAsync(Cancellation));
        }
    }
}
