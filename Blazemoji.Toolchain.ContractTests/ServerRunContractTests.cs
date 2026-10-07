using System.Net;

namespace Blazemoji.Toolchain.ContractTests
{
    /// <summary>
    /// Runs started with <c>http: true</c>, and the route that passes requests on to them.
    /// The server here is written with the stock sockets package, so these tests need no
    /// package beyond the ones every toolchain has.
    /// </summary>
    public class ServerRunContractTests
    {
        private const string ProxyHeader = "X-Toolchain-Proxy";

        /// <summary>
        /// Listens on the port named by PORT, prints each request, answers every one with "200 hello".
        /// </summary>
        private const string TinyHttpServer =
            "📦 sockets 🏠\n\n" +
            "🏁 🍇\n" +
            "  8080 ➡️ 🖍🆕port\n" +
            "  ↪️ 🌳🐇💻 🔤PORT🔤❗️ ➡️ text 🍇\n" +
            "    ↪️ 🔢 text 10❗️ ➡️ parsed 🍇\n" +
            "      parsed ➡️ 🖍port\n" +
            "    🍉\n" +
            "  🍉\n" +
            "  🍺 🆕🏄 port❗️ ➡️ server\n" +
            "  😀 🔤listening🔤❗️\n" +
            "  🔁 👍 🍇\n" +
            "    🍺 🙋 server❗️ ➡️ client\n" +
            "    🆗 data 👂 client 4096❗️ 🍇\n" +
            "      😀 🍺 🔡 data❗️❗️\n" +
            "      🆗 💬 client 📇 🔤HTTP/1.1 200 OK❌r❌nContent-Type: text/plain❌r❌nContent-Length: 5❌r❌nConnection: close❌r❌n❌r❌nhello🔤❗️❗️ 🍇🍉\n" +
            "      🙅 sendError 🍇🍉\n" +
            "    🍉\n" +
            "    🙅 readError 🍇🍉\n" +
            "    🚪 client❗️\n" +
            "  🍉\n" +
            "🍉\n";

        private const string PrintsItsPort =
            "🏁 🍇\n  ↪️ 🌳🐇💻 🔤PORT🔤❗️ ➡️ port 🍇\n    😀 port❗️\n  🍉\n🍉\n";

        private const string Forever =
            "🏁 🍇\n  🔁 👍 🍇\n    ⏲🐇🧵 100000❗️\n  🍉\n🍉\n";

        private const string Hello = "🏁 🍇\n  😀 🔤Hello World!🔤❗️\n🍉\n";

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static async Task<string> StartAsync(string code, bool http)
        {
            var buildId = await ToolchainService.BuildAsync(code, Cancellation);
            using var response = await ToolchainService.Http.PostAsync("runs", ToolchainService.Json(new { buildId, http }), Cancellation);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await ToolchainService.ReadJsonAsync(response, Cancellation)).GetProperty("runId").GetString()!;
        }

        private static async Task StopAsync(string runId)
        {
            using var response = await ToolchainService.Http.DeleteAsync($"runs/{runId}", CancellationToken.None);
        }

        private static string? ProxyReason(HttpResponseMessage response) =>
            response.Headers.TryGetValues(ProxyHeader, out var values) ? values.Single() : null;

        /// <summary>
        /// A program takes a moment to start listening. Until it does, the service answers for
        /// it with a marked 502.
        /// </summary>
        private static async Task<HttpResponseMessage> GetOnceListeningAsync(string runId, string path)
        {
            for (var attempt = 0; ; attempt++)
            {
                var response = await ToolchainService.Http.GetAsync($"runs/{runId}/http{path}", Cancellation);
                if (ProxyReason(response) != "not-listening" || attempt >= 100)
                    return response;

                response.Dispose();
                await Task.Delay(200, Cancellation);
            }
        }

        [Fact]
        public async Task A_request_to_a_server_run_reaches_the_program_and_its_response_comes_back()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await StartAsync(TinyHttpServer, http: true);
            try
            {
                using var response = await GetOnceListeningAsync(runId, "/hello?name=grapes");

                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                ProxyReason(response).ShouldBeNull();
                response.Content.Headers.ContentType?.MediaType.ShouldBe("text/plain");
                (await response.Content.ReadAsStringAsync(Cancellation)).ShouldBe("hello");
            }
            finally
            {
                await StopAsync(runId);
            }

            var events = await ToolchainService.ReadEventsAsync(runId, Cancellation);
            ToolchainService.Text(events, "stdout").ShouldContain("GET /hello?name=grapes HTTP/1.1");
            events[^1].Data.GetProperty("reason").GetString().ShouldBe("stopped");
        }

        [Fact]
        public async Task A_server_run_is_told_its_port_through_the_PORT_variable()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await StartAsync(PrintsItsPort, http: true);

            var events = await ToolchainService.ReadEventsAsync(runId, Cancellation);

            ToolchainService.Text(events, "stdout").ShouldMatch(@"\A\d+\n\z");
        }

        [Fact]
        public async Task A_request_before_the_program_listens_is_a_marked_502()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await StartAsync(Forever, http: true);
            try
            {
                using var response = await ToolchainService.Http.GetAsync($"runs/{runId}/http/", Cancellation);

                await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.BadGateway, Cancellation);
                ProxyReason(response).ShouldBe("not-listening");
            }
            finally
            {
                await StopAsync(runId);
            }
        }

        [Fact]
        public async Task A_request_to_a_run_that_is_not_a_server_is_a_marked_409()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await StartAsync(Forever, http: false);
            try
            {
                using var response = await ToolchainService.Http.GetAsync($"runs/{runId}/http/", Cancellation);

                await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.Conflict, Cancellation);
                ProxyReason(response).ShouldBe("not-a-server");
            }
            finally
            {
                await StopAsync(runId);
            }
        }

        [Fact]
        public async Task A_request_to_a_server_run_that_has_ended_is_a_marked_409()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var runId = await StartAsync(Hello, http: true);
            await ToolchainService.ReadEventsAsync(runId, Cancellation);

            using var response = await ToolchainService.Http.PostAsync($"runs/{runId}/http/todos", ToolchainService.Json(new { title = "late" }), Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.Conflict, Cancellation);
            ProxyReason(response).ShouldBe("ended");
        }

        [Fact]
        public async Task A_request_for_an_unknown_run_is_a_marked_404()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.Http.GetAsync("runs/ffffffffffffffffffffffffffffffff/http/todos", Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.NotFound, Cancellation);
            ProxyReason(response).ShouldBe("unknown-run");
        }
    }
}
