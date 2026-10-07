using System.Text;
using Blazemoji.Test.State;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Blazemoji.Test.Service
{
    /// <summary>
    /// The client as the web app registers it, talking to the real service over a real socket.
    /// What the HTTP stack itself does with a response (follow a redirect, keep a cookie,
    /// refuse a header) only shows up here, not against the service hosted in memory.
    /// </summary>
    public sealed class ToolchainClientOverTheWireTests : IDisposable
    {
        private readonly ToolchainServiceFactory _factory = new();
        private readonly ScriptedRun _run = new();
        private readonly ServiceProvider _clientServices;
        private readonly Uri _serviceAddress;

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        public ToolchainClientOverTheWireTests()
        {
            _factory.UseKestrel(0);
            _factory.StartServer();
            _serviceAddress = new Uri(_factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
            _factory.Toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(_run);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddToolchainClient(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ToolchainClient:BaseUrl"] = _serviceAddress.ToString() })
                .Build());
            _clientServices = services.BuildServiceProvider();
        }

        public void Dispose()
        {
            _clientServices.Dispose();
            _factory.Dispose();
        }

        private async Task<IToolchainRun> StartRunAsync() =>
            await _clientServices.GetRequiredService<IToolchain>().StartRunAsync(new RunRequest(ToolchainServiceFactory.KnownBuild, Server: true), Cancellation);

        private static ProgramResponse Answer(int status, string headerName, string headerValue) =>
            new(ProgramResponseOutcome.Answered, status, null, [new(headerName, headerValue)], Encoding.UTF8.GetBytes("from the program"), TimeSpan.Zero);

        private static ProgramRequest Get(string path) => new("GET", path, [], []);

        [Theory]
        [InlineData(301)]
        [InlineData(302)]
        [InlineData(307)]
        public async Task A_redirect_from_the_program_is_the_programs_answer_and_is_not_followed(int status)
        {
            var elsewhere = new Uri(_serviceAddress, "/packages").ToString();
            _run.Respond = _ => Answer(status, "Location", elsewhere);
            await using var run = await StartRunAsync();

            var response = await run.SendHttpAsync(new ProgramRequest("POST", "/old", [], Encoding.UTF8.GetBytes("{}")), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Answered);
            response.StatusCode.ShouldBe(status);
            response.Headers.ShouldContain(new KeyValuePair<string, string>("Location", elsewhere));
            Encoding.UTF8.GetString(response.Body).ShouldBe("from the program");
            _run.Requests.Count.ShouldBe(1);
        }

        [Fact]
        public async Task A_cookie_the_program_sets_is_shown_and_is_not_sent_back_by_itself()
        {
            _run.Respond = _ => Answer(200, "Set-Cookie", "session=abc; Path=/");
            await using var run = await StartRunAsync();

            var first = await run.SendHttpAsync(Get("/login"), Cancellation);
            await run.SendHttpAsync(Get("/todos"), Cancellation);

            first.Headers.ShouldContain(new KeyValuePair<string, string>("Set-Cookie", "session=abc; Path=/"));
            _run.Requests.Count.ShouldBe(2);
            _run.Requests[1].Headers.ShouldNotContain(header => header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData("🍇 fresh")]
        [InlineData("café")]
        public async Task A_header_value_outside_ascii_reaches_the_caller_as_the_program_sent_it(string value)
        {
            _run.Respond = _ => Answer(200, "X-Mood", value);
            await using var run = await StartRunAsync();

            var response = await run.SendHttpAsync(Get("/"), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Answered);
            response.StatusCode.ShouldBe(200);
            response.Headers.ShouldContain(new KeyValuePair<string, string>("X-Mood", value));
        }

        [Fact]
        public async Task A_request_header_value_outside_ascii_reaches_the_program_as_it_was_typed()
        {
            _run.Respond = _ => Answer(200, "X-Mood", "fine");
            await using var run = await StartRunAsync();

            var response = await run.SendHttpAsync(new ProgramRequest("GET", "/", [new("X-Name", "José 🍇")], []), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Answered);
            _run.Requests.ShouldHaveSingleItem().Headers.ShouldContain(new KeyValuePair<string, string>("X-Name", "José 🍇"));
        }

        [Fact]
        public async Task A_header_value_that_cannot_be_passed_on_is_a_bad_response_and_not_the_programs_own_error()
        {
            _run.Respond = _ => Answer(200, "X-Broken", "a\u0001b");
            await using var run = await StartRunAsync();

            var response = await run.SendHttpAsync(Get("/"), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.BadResponse);
        }
    }
}
