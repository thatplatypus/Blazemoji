using System.Net;
using System.Net.Sockets;
using System.Text;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Local;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// The endpoint is what talks HTTP to a server program. Here the "program" is a small web
    /// server in the test process, so these run on any platform.
    /// </summary>
    public sealed class ProgramEndpointTests : IAsyncLifetime
    {
        private static readonly TimeSpan Generous = TimeSpan.FromSeconds(20);

        private WebApplication _program = default!;
        private int _port;

        public async ValueTask InitializeAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.ResponseHeaderEncodingSelector = _ => Encoding.UTF8);
            _program = builder.Build();
            _program.Urls.Add("http://127.0.0.1:0");

            _program.Map("/echo/{**rest}", async (HttpContext context) =>
            {
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync(context.RequestAborted);
                context.Response.StatusCode = StatusCodes.Status201Created;
                context.Response.Headers["X-From-Program"] = "yes";
                context.Response.Headers.Append("Set-Cookie", "a=1");
                context.Response.Headers.Append("Set-Cookie", "b=2");
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(string.Join('\n',
                    context.Request.Method,
                    context.Request.Path + context.Request.QueryString,
                    "host=" + context.Request.Headers.Host,
                    "x-test=" + context.Request.Headers["X-Test"],
                    "content-type=" + context.Request.ContentType,
                    "proxy-authorization=" + context.Request.Headers.ProxyAuthorization,
                    "body=" + body));
            });
            _program.MapGet("/slow", async (HttpContext context) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted);
                return "late";
            });
            _program.MapGet("/big", () => new string('x', 64 * 1024));
            _program.MapGet("/redirect", () => Results.Redirect("/echo/after-redirect"));
            _program.MapGet("/emoji-header", (HttpContext context) =>
            {
                context.Response.Headers["X-Mood"] = "🍇 fresh";
                return "ok";
            });

            await _program.StartAsync(TestContext.Current.CancellationToken);
            _port = new Uri(_program.Urls.First()).Port;
        }

        public async ValueTask DisposeAsync() => await _program.DisposeAsync();

        [Fact]
        public async Task A_request_reaches_the_program_with_its_method_path_query_headers_and_body()
        {
            using var endpoint = new ProgramEndpoint(_port, Generous, 1024 * 1024);
            var request = new ProgramRequest(
                "PUT",
                "/echo/todos/7?done=true&q=a%20b",
                [new("X-Test", "grapes"), new("Content-Type", "application/json")],
                Encoding.UTF8.GetBytes("{\"title\":\"🍇\"}"));

            var response = await endpoint.SendAsync(request, TestContext.Current.CancellationToken);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Answered);
            response.StatusCode.ShouldBe(201);
            Encoding.UTF8.GetString(response.Body).Split('\n').ShouldBe(
            [
                "PUT",
                "/echo/todos/7?done=true&q=a%20b",
                $"host=127.0.0.1:{_port}",
                "x-test=grapes",
                "content-type=application/json",
                "proxy-authorization=",
                "body={\"title\":\"🍇\"}",
            ]);
        }

        [Fact]
        public async Task The_response_carries_the_programs_headers_with_repeats_kept_apart()
        {
            using var endpoint = new ProgramEndpoint(_port, Generous, 1024 * 1024);

            var response = await endpoint.SendAsync(Get("/echo/x"), TestContext.Current.CancellationToken);

            response.Headers.ShouldContain(new KeyValuePair<string, string>("X-From-Program", "yes"));
            response.Headers.Where(h => h.Key == "Set-Cookie").Select(h => h.Value).ShouldBe(["a=1", "b=2"]);
            response.Headers.ShouldContain(h => h.Key == "Content-Type" && h.Value == "text/plain; charset=utf-8");
            response.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
        }

        [Fact]
        public async Task Hop_by_hop_headers_are_not_passed_in_either_direction()
        {
            using var endpoint = new ProgramEndpoint(_port, Generous, 1024 * 1024);
            var request = new ProgramRequest(
                "GET",
                "/echo/x",
                [new("Host", "elsewhere.example"), new("Proxy-Authorization", "secret"), new("Connection", "close"), new("Content-Length", "999")],
                []);

            var response = await endpoint.SendAsync(request, TestContext.Current.CancellationToken);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Answered);
            var seen = Encoding.UTF8.GetString(response.Body);
            seen.ShouldContain($"host=127.0.0.1:{_port}");
            seen.ShouldContain("proxy-authorization=\n");
            response.Headers.ShouldNotContain(h => h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase));
            response.Headers.ShouldNotContain(h => h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task A_path_without_a_leading_slash_is_given_one()
        {
            using var endpoint = new ProgramEndpoint(_port, Generous, 1024 * 1024);

            var response = await endpoint.SendAsync(Get("echo/plain"), TestContext.Current.CancellationToken);

            Encoding.UTF8.GetString(response.Body).ShouldContain("/echo/plain");
        }

        [Fact]
        public async Task A_request_header_value_outside_ascii_is_sent_as_utf8()
        {
            using var endpoint = new ProgramEndpoint(_port, Generous, 1024 * 1024);

            var response = await endpoint.SendAsync(new ProgramRequest("GET", "/echo/x", [new("X-Test", "José 🍇")], []), TestContext.Current.CancellationToken);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Answered);
            Encoding.UTF8.GetString(response.Body).ShouldContain("x-test=José 🍇");
        }

        [Fact]
        public async Task A_header_the_program_wrote_in_utf8_is_read_as_utf8()
        {
            using var endpoint = new ProgramEndpoint(_port, Generous, 1024 * 1024);

            var response = await endpoint.SendAsync(Get("/emoji-header"), TestContext.Current.CancellationToken);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Answered);
            response.Headers.ShouldContain(new KeyValuePair<string, string>("X-Mood", "🍇 fresh"));
        }

        [Fact]
        public async Task A_redirect_is_returned_as_it_is_and_not_followed()
        {
            using var endpoint = new ProgramEndpoint(_port, Generous, 1024 * 1024);

            var response = await endpoint.SendAsync(Get("/redirect"), TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(302);
            response.Headers.ShouldContain(new KeyValuePair<string, string>("Location", "/echo/after-redirect"));
        }

        [Fact]
        public async Task Nothing_listening_on_the_port_is_reported_as_not_listening()
        {
            using var endpoint = new ProgramEndpoint(FreePort(), Generous, 1024 * 1024);

            var response = await endpoint.SendAsync(Get("/"), TestContext.Current.CancellationToken);

            response.Outcome.ShouldBe(ProgramResponseOutcome.NotListening);
            response.Body.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_program_that_hangs_up_without_answering_is_a_bad_response()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var hangingUp = HangUpOnEveryCallerAsync(listener, stop.Token);
            using var endpoint = new ProgramEndpoint(((IPEndPoint)listener.LocalEndpoint).Port, Generous, 1024 * 1024);

            var response = await endpoint.SendAsync(Get("/"), TestContext.Current.CancellationToken);
            await stop.CancelAsync();
            await hangingUp;

            response.Outcome.ShouldBe(ProgramResponseOutcome.BadResponse);
        }

        [Fact]
        public async Task A_response_over_the_limit_is_a_bad_response()
        {
            using var endpoint = new ProgramEndpoint(_port, Generous, maxResponseBytes: 1024);

            var response = await endpoint.SendAsync(Get("/big"), TestContext.Current.CancellationToken);

            response.Outcome.ShouldBe(ProgramResponseOutcome.BadResponse);
            response.Body.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_program_that_does_not_answer_in_time_is_timed_out()
        {
            using var endpoint = new ProgramEndpoint(_port, TimeSpan.FromMilliseconds(300), 1024 * 1024);

            var response = await endpoint.SendAsync(Get("/slow"), TestContext.Current.CancellationToken);

            response.Outcome.ShouldBe(ProgramResponseOutcome.TimedOut);
            response.Duration.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task Cancelling_the_caller_is_a_cancellation_and_not_a_timeout()
        {
            using var endpoint = new ProgramEndpoint(_port, Generous, 1024 * 1024);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));

            await Should.ThrowAsync<OperationCanceledException>(() => endpoint.SendAsync(Get("/slow"), cancellation.Token));
        }

        [Theory]
        [InlineData("GE T", "/", "X-Test", "fine")]
        [InlineData("GET", "/a\r\nX-Injected: 1", "X-Test", "fine")]
        [InlineData("GET", "/", "X-Test", "fine\r\nX-Injected: 1")]
        [InlineData("GET", "/", "Bad Name", "fine")]
        [InlineData("GET", "/", "", "fine")]
        [InlineData("GET", "/a/../b", "X-Test", "fine")]
        [InlineData("GET", "/%2e%2e/b", "X-Test", "fine")]
        public async Task A_request_that_cannot_be_sent_as_http_is_refused_without_reaching_the_program(string method, string path, string headerName, string headerValue)
        {
            using var endpoint = new ProgramEndpoint(_port, Generous, 1024 * 1024);
            var request = new ProgramRequest(method, path, [new(headerName, headerValue)], []);

            var response = await endpoint.SendAsync(request, TestContext.Current.CancellationToken);

            response.Outcome.ShouldBe(ProgramResponseOutcome.InvalidRequest);
        }

        private static ProgramRequest Get(string path) => new("GET", path, [], []);

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        /// <summary>
        /// The HTTP client tries again on a fresh connection when one is closed on it, so the
        /// stand-in for a broken program has to hang up every time, as a broken program would.
        /// </summary>
        private static async Task HangUpOnEveryCallerAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            var buffer = new byte[1024];
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                    await client.GetStream().ReadAsync(buffer, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
