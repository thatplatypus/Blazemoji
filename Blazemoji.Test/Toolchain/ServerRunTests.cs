using System.Text;
using System.Text.RegularExpressions;
using Blazemoji.Toolchain;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// What is different about a run started as a server. Shell scripts stand in for programs
    /// except where a real one has to answer over HTTP.
    /// </summary>
    public class ServerRunTests
    {
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static ProgramRequest Get(string path) => new("GET", path, [], []);

        [Fact]
        public async Task A_server_run_is_told_its_port_through_the_environment()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "echo \"port=$PORT\"");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId, Server: true), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            var port = int.Parse(Regex.Match(finished.Stdout, @"\Aport=(\d+)\n\z").Groups[1].Value);
            port.ShouldBeInRange(1024, 65535);
        }

        [Fact]
        public async Task A_port_named_in_the_request_is_replaced_for_a_server_run()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "echo \"port=$PORT\"");
            var environment = new Dictionary<string, string> { ["PORT"] = "1" };

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId, environment, Server: true), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Stdout.ShouldNotBe("port=1\n");
            finished.Stdout.ShouldMatch(@"\Aport=\d+\n\z");
        }

        [Fact]
        public async Task A_run_that_is_not_a_server_keeps_whatever_port_the_request_named()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "echo \"port=$PORT\"");
            var environment = new Dictionary<string, string> { ["PORT"] = "4321" };

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId, environment), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Stdout.ShouldBe("port=4321\n");
        }

        [Fact]
        public async Task A_server_run_has_no_wall_clock_limit_and_ends_once_it_has_been_idle()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create(options =>
            {
                options.RunTimeout = TimeSpan.FromMilliseconds(300);
                options.ServerIdleTimeout = TimeSpan.FromSeconds(2);
            });
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "sleep 60");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId, Server: true), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.Idle);
            finished.Exit.Duration.ShouldBeGreaterThan(TimeSpan.FromSeconds(1.5));
            finished.Exit.Duration.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        }

        [Fact]
        public async Task Requests_keep_a_server_run_alive()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create(options => options.ServerIdleTimeout = TimeSpan.FromSeconds(2));
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "sleep 60");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId, Server: true), Cancellation);
            var reading = run.RunToEndAsync(Cancellation);
            for (var i = 0; i < 6; i++)
            {
                await run.SendHttpAsync(Get("/"), Cancellation);
                await Task.Delay(TimeSpan.FromMilliseconds(500), Cancellation);
            }

            var finished = await reading;

            // Three seconds of requests and then two of silence. Without the requests the run
            // would have ended after two.
            finished.Exit.Reason.ShouldBe(RunEndReason.Idle);
            finished.Exit.Duration.ShouldBeGreaterThan(TimeSpan.FromSeconds(4));
        }

        [Fact]
        public async Task A_request_before_the_program_listens_is_answered_not_listening()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "sleep 60");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId, Server: true), Cancellation);
            var response = await run.SendHttpAsync(Get("/"), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.NotListening);
        }

        [Fact]
        public async Task A_request_to_a_run_that_is_not_a_server_says_so()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "sleep 60");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);
            var response = await run.SendHttpAsync(Get("/"), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.NotAServer);
        }

        [Fact]
        public async Task A_request_to_a_server_that_has_ended_says_so()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "exit 0");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId, Server: true), Cancellation);
            await run.RunToEndAsync(Cancellation);
            var response = await run.SendHttpAsync(Get("/"), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Ended);
        }

        [Fact]
        public async Task A_request_to_a_server_that_was_stopped_says_it_has_ended()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "sleep 60");

            await using var run = await toolchain.StartRunAsync(new RunRequest(buildId, Server: true), Cancellation);
            await run.StopAsync();
            var response = await run.SendHttpAsync(Get("/"), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Ended);
        }

        [Fact]
        public async Task A_request_to_a_run_that_never_started_says_it_has_ended()
        {
            await using var toolchain = ToolchainFixture.Create();

            await using var run = await toolchain.StartRunAsync(new RunRequest(new string('0', 32), Server: true), Cancellation);
            var response = await run.SendHttpAsync(Get("/"), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Ended);
        }

        [Fact]
        public async Task A_request_after_the_run_is_disposed_says_it_has_ended()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            await using var toolchain = ToolchainFixture.Create();
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "sleep 60");
            var run = await toolchain.StartRunAsync(new RunRequest(buildId, Server: true), Cancellation);

            await run.DisposeAsync();
            var response = await run.SendHttpAsync(Get("/"), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Ended);
        }

        [Fact]
        public async Task A_server_run_is_given_the_larger_cpu_allowance()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Resource limits are applied with prlimit, which is Linux only.");
            await using var toolchain = ToolchainFixture.Create(options =>
            {
                options.CpuSeconds = 7;
                options.ServerCpuSeconds = 77;
            });
            var buildId = ToolchainFixture.ScriptBuild(toolchain, "ulimit -t");

            await using var server = await toolchain.StartRunAsync(new RunRequest(buildId, Server: true), Cancellation);
            await using var script = await toolchain.StartRunAsync(new RunRequest(buildId), Cancellation);

            (await server.RunToEndAsync(Cancellation)).Stdout.ShouldBe("77\n");
            (await script.RunToEndAsync(Cancellation)).Stdout.ShouldBe("7\n");
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task A_compiled_server_answers_a_request_sent_through_its_run()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();
            var build = await toolchain.CompileAsync(ToolchainFixture.SingleFile(Programs.TinyHttpServer), Cancellation);
            build.Ok.ShouldBeTrue(string.Join("; ", build.Diagnostics.Select(d => $"{d.Line}:{d.Character} {d.Message}")));

            await using var run = await toolchain.StartRunAsync(new RunRequest(build.BuildId!, Server: true), Cancellation);
            var output = new StringBuilder();
            var events = run.ReadEventsAsync(Cancellation).GetAsyncEnumerator(Cancellation);
            while (!output.ToString().Contains("listening") && await events.MoveNextAsync())
                output.Append((events.Current as StdoutEvent)?.Text);

            var response = await run.SendHttpAsync(Get("/hello?name=grapes"), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Answered);
            response.StatusCode.ShouldBe(200);
            Encoding.UTF8.GetString(response.Body).ShouldBe("hello");
            response.Headers.ShouldContain(new KeyValuePair<string, string>("Content-Type", "text/plain"));

            while (!output.ToString().Contains("HTTP/1.1") && await events.MoveNextAsync())
                output.Append((events.Current as StdoutEvent)?.Text);
            output.ToString().ShouldContain("GET /hello?name=grapes HTTP/1.1");
            await events.DisposeAsync();
        }
    }
}
