using System.Net;
using System.Text;

namespace Blazemoji.Toolchain.ContractTests
{
    public class RunContractTests
    {
        private const string UnknownId = "ffffffffffffffffffffffffffffffff";

        private const string EchoesALine = "🏁 🍇\n  🆕🔡▶️👂🏼❗️ ➡️ line\n  😀 line❗️\n🍉\n";

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_run_streams_numbered_events_ending_with_one_exit_event()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await ToolchainService.BuildAndStartAsync(Programs.Hello, Cancellation);

            var events = await ToolchainService.ReadEventsAsync(runId, Cancellation);

            events.Select(e => e.Id).ShouldBe(Enumerable.Range(1, events.Count).Select(i => i.ToString()));
            events.ShouldAllBe(e => e.Name == "stdout" || e.Name == "stderr" || e.Name == "exit");
            events.Count(e => e.Name == "exit").ShouldBe(1);
            events[^1].Name.ShouldBe("exit");
            ToolchainService.Text(events, "stdout").ShouldBe("Hello World!\n");
            events[^1].Data.GetProperty("exitCode").GetInt32().ShouldBe(0);
            events[^1].Data.GetProperty("reason").GetString().ShouldBe("exited");
            events[^1].Data.GetProperty("durationMs").GetInt64().ShouldBeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public async Task The_programs_exit_code_is_reported()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await ToolchainService.BuildAndStartAsync(Programs.ExitsWith(3), Cancellation);

            var events = await ToolchainService.ReadEventsAsync(runId, Cancellation);

            events[^1].Data.GetProperty("exitCode").GetInt32().ShouldBe(3);
            events[^1].Data.GetProperty("reason").GetString().ShouldBe("exited");
        }

        [Fact]
        public async Task Output_is_delivered_while_the_program_is_still_running()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await ToolchainService.BuildAndStartAsync(Programs.SlowTwoLines, Cancellation);

            var events = await ToolchainService.ReadEventsAsync(runId, Cancellation);

            var stdout = events.Where(e => e.Name == "stdout").ToList();
            ToolchainService.Text(events, "stdout").ShouldBe("one\ntwo\n");
            (events[^1].At - stdout[0].At).ShouldBeGreaterThan(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task A_client_that_connects_after_the_program_has_finished_still_gets_everything()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await ToolchainService.BuildAndStartAsync(Programs.Hello, Cancellation);
            await Task.Delay(TimeSpan.FromSeconds(2), Cancellation);

            var events = await ToolchainService.ReadEventsAsync(runId, Cancellation);

            ToolchainService.Text(events, "stdout").ShouldBe("Hello World!\n");
            events[^1].Name.ShouldBe("exit");
        }

        [Fact]
        public async Task A_client_can_resume_after_the_last_event_it_saw()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await ToolchainService.BuildAndStartAsync(Programs.SlowTwoLines, Cancellation);
            var all = await ToolchainService.ReadEventsAsync(runId, Cancellation);

            var resumed = await ToolchainService.ReadEventsAsync(runId, Cancellation, lastEventId: "1");

            resumed.Select(e => e.Id).ShouldBe(all.Skip(1).Select(e => e.Id));
            resumed[^1].Name.ShouldBe("exit");
        }

        [Fact]
        public async Task A_run_beyond_the_services_limit_is_a_429_problem_and_a_place_comes_back_when_one_ends()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            const int MostToTry = 33;
            var buildId = await ToolchainService.BuildAsync(Programs.Forever, Cancellation);
            var running = new List<string>();
            try
            {
                HttpResponseMessage? refused = null;
                while (refused is null && running.Count < MostToTry)
                {
                    var response = await ToolchainService.PostRunAsync(buildId, Cancellation);
                    if (response.StatusCode == HttpStatusCode.Created)
                    {
                        running.Add((await ToolchainService.ReadJsonAsync(response, Cancellation)).GetProperty("runId").GetString()!);
                        response.Dispose();
                    }
                    else
                    {
                        refused = response;
                    }
                }

                Assert.SkipWhen(refused is null, $"The service accepted {MostToTry} programs at once, so its limit is beyond what this test tries.");
                using (refused)
                    await ToolchainService.ShouldBeProblemAsync(refused!, HttpStatusCode.TooManyRequests, Cancellation);

                using var stopped = await ToolchainService.Http.DeleteAsync($"runs/{running[0]}", Cancellation);
                await ToolchainService.ReadEventsAsync(running[0], Cancellation);
                running.RemoveAt(0);

                // The place is given back when the run ends, which can be a moment after its
                // last event has been read.
                HttpStatusCode status;
                var attempts = 0;
                do
                {
                    await Task.Delay(100, Cancellation);
                    using var again = await ToolchainService.PostRunAsync(buildId, Cancellation);
                    status = again.StatusCode;
                    if (status == HttpStatusCode.Created)
                        running.Add((await ToolchainService.ReadJsonAsync(again, Cancellation)).GetProperty("runId").GetString()!);
                }
                while (status == HttpStatusCode.TooManyRequests && ++attempts < 50);

                status.ShouldBe(HttpStatusCode.Created);
            }
            finally
            {
                foreach (var runId in running)
                {
                    using var response = await ToolchainService.Http.DeleteAsync($"runs/{runId}", CancellationToken.None);
                }
            }
        }

        [Fact]
        public async Task Delete_stops_a_running_program()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await ToolchainService.BuildAndStartAsync(Programs.Forever, Cancellation);
            var reading = ToolchainService.ReadEventsAsync(runId, Cancellation);
            await Task.Delay(TimeSpan.FromMilliseconds(500), Cancellation);

            using var response = await ToolchainService.Http.DeleteAsync($"runs/{runId}", Cancellation);
            var events = await reading;

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            events[^1].Name.ShouldBe("exit");
            events[^1].Data.GetProperty("reason").GetString().ShouldBe("stopped");
        }

        [Fact]
        public async Task Deleting_a_run_that_has_ended_is_still_a_204()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await ToolchainService.BuildAndStartAsync(Programs.Hello, Cancellation);
            await ToolchainService.ReadEventsAsync(runId, Cancellation);

            using var response = await ToolchainService.Http.DeleteAsync($"runs/{runId}", Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task Text_posted_to_stdin_is_read_by_the_program()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await ToolchainService.BuildAndStartAsync(EchoesALine, Cancellation);

            using var response = await ToolchainService.Http.PostAsync($"runs/{runId}/stdin", new StringContent("typed by the test\n", Encoding.UTF8, "text/plain"), Cancellation);
            var events = await ToolchainService.ReadEventsAsync(runId, Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            ToolchainService.Text(events, "stdout").ShouldBe("typed by the test\n");
            events[^1].Data.GetProperty("exitCode").GetInt32().ShouldBe(0);
        }

        [Fact]
        public async Task Ending_stdin_lets_a_program_that_is_waiting_for_input_finish()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await ToolchainService.BuildAndStartAsync(EchoesALine, Cancellation);

            using var response = await ToolchainService.Http.PostAsync($"runs/{runId}/stdin?eof=true", new StringContent(string.Empty), Cancellation);
            var events = await ToolchainService.ReadEventsAsync(runId, Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            events[^1].Name.ShouldBe("exit");
            events[^1].Data.GetProperty("reason").GetString().ShouldBe("exited");
        }

        [Fact]
        public async Task Stdin_for_a_run_that_has_ended_is_a_409_problem()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await ToolchainService.BuildAndStartAsync(Programs.Hello, Cancellation);
            await ToolchainService.ReadEventsAsync(runId, Cancellation);

            using var response = await ToolchainService.Http.PostAsync($"runs/{runId}/stdin", new StringContent("late"), Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.Conflict, Cancellation);
        }

        [Fact]
        public async Task Environment_variables_given_with_the_run_reach_the_program()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var buildId = await ToolchainService.BuildAsync(Programs.PrintsEnvironment, Cancellation);

            var runId = await ToolchainService.StartRunAsync(buildId, Cancellation, new Dictionary<string, string> { ["BLAZEMOJI_TEST"] = "from the request" });
            var events = await ToolchainService.ReadEventsAsync(runId, Cancellation);

            ToolchainService.Text(events, "stdout").ShouldBe("from the request\n");
        }

        [Fact]
        public async Task The_same_build_can_be_run_more_than_once()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var buildId = await ToolchainService.BuildAsync(Programs.Hello, Cancellation);

            var first = await ToolchainService.ReadEventsAsync(await ToolchainService.StartRunAsync(buildId, Cancellation), Cancellation);
            var second = await ToolchainService.ReadEventsAsync(await ToolchainService.StartRunAsync(buildId, Cancellation), Cancellation);

            ToolchainService.Text(first, "stdout").ShouldBe("Hello World!\n");
            ToolchainService.Text(second, "stdout").ShouldBe("Hello World!\n");
        }

        [Fact]
        public async Task Starting_a_run_of_an_unknown_build_is_a_404_problem()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.Http.PostAsync("runs", ToolchainService.Json(new { buildId = UnknownId }), Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.NotFound, Cancellation);
        }

        [Fact]
        public async Task Starting_a_run_without_a_build_id_is_a_400_problem()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.Http.PostAsync("runs", ToolchainService.Json(new { }), Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, Cancellation);
        }

        [Fact]
        public async Task Events_stdin_and_delete_for_an_unknown_run_are_404s()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var events = await ToolchainService.Http.GetAsync($"runs/{UnknownId}/events", Cancellation);
            using var stdin = await ToolchainService.Http.PostAsync($"runs/{UnknownId}/stdin", new StringContent("x"), Cancellation);
            using var delete = await ToolchainService.Http.DeleteAsync($"runs/{UnknownId}", Cancellation);

            events.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            stdin.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }
}
