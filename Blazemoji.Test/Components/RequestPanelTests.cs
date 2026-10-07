using System.Text;
using Blazemoji.Components;
using Blazemoji.Shared.State;
using Blazemoji.Test.State;
using Blazemoji.Toolchain;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;
using NSubstitute;

namespace Blazemoji.Test.Components
{
    public sealed class RequestPanelTests : BunitContext
    {
        private readonly IToolchain _toolchain = Substitute.For<IToolchain>();
        private readonly ScriptedRun _run = new();
        private readonly RunState _runState;

        public RequestPanelTests()
        {
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            JSInterop.Mode = JSRuntimeMode.Loose;

            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>()).Returns(new CompileResult(true, [], "build-1"));
            _toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(_run);
            _runState = new RunState(_toolchain, NullLogger<RunState>.Instance, new FakeTimeProvider());
            Services.AddSingleton(_runState);
            Services.AddSingleton(new RequestState(_runState));
        }

        private Task StartServer() =>
            _runState.RunAsync(new RunTarget(new Dictionary<string, string> { ["main.🍇"] = "x" }, "main.🍇", Server: true));

        private static ProgramResponse Answer(int status, string body, string contentType = "application/json") =>
            new(ProgramResponseOutcome.Answered, status, null, [new("Content-Type", contentType), new("X-Powered-By", "Grapevine")], Encoding.UTF8.GetBytes(body), TimeSpan.FromMilliseconds(12));

        [Fact]
        public void With_no_server_running_it_says_so_and_cannot_send()
        {
            var cut = Render<RequestPanel>();

            cut.Find("[data-testid=no-server]").TextContent.ShouldContain("Run a project that is set to run as a web server");
            cut.Find("[data-testid=request-send]").HasAttribute("disabled").ShouldBeTrue();
            cut.FindAll("[data-testid=response]").ShouldBeEmpty();
        }

        [Fact]
        public async Task Once_a_server_is_running_the_notice_goes_and_send_is_enabled()
        {
            var cut = Render<RequestPanel>();

            var running = StartServer();
            cut.WaitForAssertion(() => cut.FindAll("[data-testid=no-server]").ShouldBeEmpty());

            cut.Find("[data-testid=request-send]").HasAttribute("disabled").ShouldBeFalse();
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task Sending_shows_the_status_the_facts_the_headers_and_the_body()
        {
            _run.Respond = _ => Answer(201, "{\"id\":1,\"title\":\"🍇\"}");
            var cut = Render<RequestPanel>();
            var running = StartServer();
            cut.WaitForAssertion(() => cut.Find("[data-testid=request-send]").HasAttribute("disabled").ShouldBeFalse());

            await cut.Find("[data-testid=request-path]").InputAsync("/todos");
            await cut.Find("[data-testid=request-send]").ClickAsync();

            cut.WaitForAssertion(() => cut.Find("[data-testid=response-status]").TextContent.ShouldBe("201 Created"));
            cut.Find("[data-testid=response-request]").TextContent.ShouldBe("GET /todos");
            cut.Find("[data-testid=response-facts]").TextContent.ShouldBe("12 ms, 23 bytes");
            cut.Find("[data-testid=response-headers]").TextContent.ShouldContain("X-Powered-By: Grapevine");
            cut.Find("[data-testid=response-body]").TextContent.ReplaceLineEndings("\n").ShouldBe("{\n  \"id\": 1,\n  \"title\": \"🍇\"\n}");
            _run.Requests.ShouldHaveSingleItem().Path.ShouldBe("/todos");
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task A_request_the_program_did_not_answer_is_explained_in_a_sentence()
        {
            _run.Respond = _ => ProgramResponse.Without(ProgramResponseOutcome.NotListening);
            var cut = Render<RequestPanel>();
            var running = StartServer();
            cut.WaitForAssertion(() => cut.Find("[data-testid=request-send]").HasAttribute("disabled").ShouldBeFalse());

            await cut.Find("[data-testid=request-send]").ClickAsync();

            cut.WaitForAssertion(() => cut.Find("[data-testid=response-problem]").TextContent.ShouldContain("not accepting connections yet"));
            cut.FindAll("[data-testid=response-status]").ShouldBeEmpty();
            cut.FindAll("[data-testid=response-body]").ShouldBeEmpty();
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task A_request_that_cannot_be_made_shows_why_beside_the_button()
        {
            var cut = Render<RequestPanel>();
            var running = StartServer();
            cut.WaitForAssertion(() => cut.Find("[data-testid=request-send]").HasAttribute("disabled").ShouldBeFalse());

            await cut.Find("[data-testid=request-headers]").InputAsync("this is not a header");
            await cut.Find("[data-testid=request-send]").ClickAsync();

            cut.WaitForAssertion(() => cut.Find("[data-testid=request-problem]").TextContent.ShouldBe("Write each header on its own line as Name: value."));
            _run.Requests.ShouldBeEmpty();
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task Earlier_requests_are_listed_once_there_is_more_than_one()
        {
            var cut = Render<RequestPanel>();
            var running = StartServer();
            cut.WaitForAssertion(() => cut.Find("[data-testid=request-send]").HasAttribute("disabled").ShouldBeFalse());

            await cut.Find("[data-testid=request-path]").InputAsync("/one");
            await cut.Find("[data-testid=request-send]").ClickAsync();
            cut.WaitForAssertion(() => cut.Find("[data-testid=response-request]").TextContent.ShouldBe("GET /one"));
            cut.FindAll("[data-testid=request-history]").ShouldBeEmpty();

            await cut.Find("[data-testid=request-path]").InputAsync("/two");
            await cut.Find("[data-testid=request-send]").ClickAsync();

            cut.WaitForAssertion(() => cut.FindAll("[data-testid=request-history]").Count.ShouldBe(2));
            cut.FindAll("[data-testid=request-history]")[0].TextContent.ShouldContain("GET /two");
            _run.Exit();
            await running;
        }
    }
}
