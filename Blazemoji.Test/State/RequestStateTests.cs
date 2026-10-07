using System.Text;
using Blazemoji.Shared.State;
using Blazemoji.Toolchain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Blazemoji.Test.State
{
    public sealed class RequestStateTests : IAsyncDisposable
    {
        private readonly IToolchain _toolchain = Substitute.For<IToolchain>();
        private readonly ScriptedRun _run = new();
        private readonly RunState _runState;
        private Task _running = Task.CompletedTask;

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        public RequestStateTests()
        {
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>()).Returns(new CompileResult(true, [], "build-1"));
            _toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(_run);
            _runState = new RunState(_toolchain, NullLogger<RunState>.Instance, new FakeTimeProvider());
            _run.Respond = _ => Answer(200, "ok");
        }

        public async ValueTask DisposeAsync()
        {
            await _runState.DisposeAsync();
            await _running;
        }

        private static ProgramResponse Answer(int status, string body) =>
            new(ProgramResponseOutcome.Answered, status, null, [], Encoding.UTF8.GetBytes(body), TimeSpan.FromMilliseconds(4));

        private async Task<RequestState> WithServerRunningAsync()
        {
            _running = _runState.RunAsync(new RunTarget(new Dictionary<string, string> { ["main.🍇"] = "x" }, "main.🍇", Server: true));
            while (!_runState.ServerRunning)
                await Task.Delay(5, Cancellation);

            return new RequestState(_runState);
        }

        [Fact]
        public async Task A_request_goes_to_the_program_with_its_method_path_headers_and_body()
        {
            var state = await WithServerRunningAsync();

            var refusal = await state.SendAsync("post", "/todos", "Content-Type: application/json\nX-Api-Key: vineyard\n", "{\"title\":\"🍇\"}", Cancellation);

            refusal.ShouldBeNull();
            var sent = _run.Requests.ShouldHaveSingleItem();
            sent.Method.ShouldBe("POST");
            sent.Path.ShouldBe("/todos");
            sent.Headers.ShouldBe([new("Content-Type", "application/json"), new("X-Api-Key", "vineyard")]);
            Encoding.UTF8.GetString(sent.Body).ShouldBe("{\"title\":\"🍇\"}");
        }

        [Fact]
        public async Task The_answer_is_kept_with_what_was_sent_and_shown_as_the_current_one()
        {
            var state = await WithServerRunningAsync();
            _run.Respond = _ => Answer(201, "{\"id\":1}");

            await state.SendAsync("POST", "/todos", "", "{}", Cancellation);

            var exchange = state.Exchanges.ShouldHaveSingleItem();
            exchange.Method.ShouldBe("POST");
            exchange.Path.ShouldBe("/todos");
            exchange.Body.ShouldBe("{}");
            exchange.Response.StatusCode.ShouldBe(201);
            state.Selected.ShouldBe(exchange);
        }

        [Fact]
        public async Task The_newest_exchange_is_first_and_only_the_last_ten_are_kept()
        {
            var state = await WithServerRunningAsync();

            for (var i = 1; i <= 12; i++)
                await state.SendAsync("GET", $"/todos/{i}", "", "", Cancellation);

            state.Exchanges.Count.ShouldBe(RequestState.MaxExchanges);
            state.Exchanges[0].Path.ShouldBe("/todos/12");
            state.Exchanges[^1].Path.ShouldBe("/todos/3");
            state.Exchanges.Select(e => e.Number).ShouldBeUnique();
        }

        [Fact]
        public async Task An_earlier_exchange_can_be_shown_again()
        {
            var state = await WithServerRunningAsync();
            await state.SendAsync("GET", "/one", "", "", Cancellation);
            await state.SendAsync("GET", "/two", "", "", Cancellation);
            var first = state.Exchanges[^1];
            var changes = 0;
            state.StateChanged += () => changes++;

            state.Select(first.Number);

            state.Selected.ShouldBe(first);
            changes.ShouldBe(1);
        }

        [Theory]
        [InlineData("todos", "/todos")]
        [InlineData("  /todos?done=true  ", "/todos?done=true")]
        public async Task A_path_is_trimmed_and_given_its_leading_slash(string typed, string sent)
        {
            var state = await WithServerRunningAsync();

            await state.SendAsync("GET", typed, "", "", Cancellation);

            _run.Requests.ShouldHaveSingleItem().Path.ShouldBe(sent);
        }

        [Fact]
        public async Task A_header_value_may_contain_colons_and_blank_lines_are_skipped()
        {
            var state = await WithServerRunningAsync();

            await state.SendAsync("GET", "/", "\nReferer: http://localhost:8080/x\r\n\r\n  Accept :  */*  \n", "", Cancellation);

            _run.Requests.ShouldHaveSingleItem().Headers.ShouldBe([new("Referer", "http://localhost:8080/x"), new("Accept", "*/*")]);
        }

        [Theory]
        [InlineData("GET", "", "", "Give a path, for example /todos.")]
        [InlineData("", "/", "", "Choose a method.")]
        [InlineData("GET", "/", "no colon here", "Write each header on its own line as Name: value.")]
        [InlineData("GET", "/", ": value without a name", "Write each header on its own line as Name: value.")]
        public async Task A_request_that_cannot_be_made_is_refused_with_a_reason_and_not_sent(string method, string path, string headers, string reason)
        {
            var state = await WithServerRunningAsync();

            (await state.SendAsync(method, path, headers, "", Cancellation)).ShouldBe(reason);

            _run.Requests.ShouldBeEmpty();
            state.Exchanges.ShouldBeEmpty();
        }

        [Fact]
        public async Task Subscribers_hear_when_sending_starts_and_when_the_answer_is_in()
        {
            var state = await WithServerRunningAsync();
            var sendingSeen = new List<bool>();
            state.StateChanged += () => sendingSeen.Add(state.Sending);

            await state.SendAsync("GET", "/", "", "", Cancellation);

            sendingSeen.ShouldBe([true, false]);
            state.Sending.ShouldBeFalse();
        }

        [Fact]
        public async Task A_request_with_no_server_running_is_recorded_as_such()
        {
            var state = new RequestState(_runState);

            await state.SendAsync("GET", "/", "", "", Cancellation);

            state.Exchanges.ShouldHaveSingleItem().Response.Outcome.ShouldBe(ProgramResponseOutcome.Ended);
        }

        [Theory]
        [InlineData(ProgramResponseOutcome.NotListening, "The program is not accepting connections yet. Give it a moment and send again.")]
        [InlineData(ProgramResponseOutcome.Ended, "The program is not running. Run it, then send again.")]
        [InlineData(ProgramResponseOutcome.NotAServer, "This program was not started as a web server. Set the project to run as a web server and run it again.")]
        [InlineData(ProgramResponseOutcome.BadResponse, "The program closed the connection or sent something that is not HTTP.")]
        [InlineData(ProgramResponseOutcome.TooLarge, "The request body is too large to send.")]
        [InlineData(ProgramResponseOutcome.TimedOut, "The program did not answer in time.")]
        [InlineData(ProgramResponseOutcome.InvalidRequest, "The method, path or a header cannot be sent as HTTP.")]
        [InlineData(ProgramResponseOutcome.Unavailable, "The toolchain service could not be reached.")]
        public void Every_way_a_request_can_go_unanswered_has_a_sentence(ProgramResponseOutcome outcome, string sentence)
        {
            RequestState.Describe(outcome).ShouldBe(sentence);
        }

        [Fact]
        public void An_answer_needs_no_sentence()
        {
            RequestState.Describe(ProgramResponseOutcome.Answered).ShouldBeNull();
        }

        [Theory]
        [InlineData("{\"id\":1,\"title\":\"🍇\"}", "application/json; charset=utf-8", "{\n  \"id\": 1,\n  \"title\": \"🍇\"\n}")]
        [InlineData("[{\"a\":1}]", "application/json", "[\n  {\n    \"a\": 1\n  }\n]")]
        [InlineData("{ not json", "application/json", "{ not json")]
        [InlineData("{\"id\":1}", "text/plain", "{\"id\":1}")]
        [InlineData("", "application/json", "")]
        [InlineData("{\"empty\":{},\"none\":[ ],\"text\":\"a \\\"quoted\\\" {brace}, [bracket]: and \\\\\"}", "application/json", "{\n  \"empty\": {},\n  \"none\": [],\n  \"text\": \"a \\\"quoted\\\" {brace}, [bracket]: and \\\\\"\n}")]
        [InlineData(" [ 1 , true , null ] ", "application/json", "[\n  1,\n  true,\n  null\n]")]
        public void A_json_body_is_laid_out_for_reading_and_anything_else_is_shown_as_it_is(string body, string contentType, string shown)
        {
            var response = new ProgramResponse(ProgramResponseOutcome.Answered, 200, null, [new("Content-Type", contentType)], Encoding.UTF8.GetBytes(body), TimeSpan.Zero);

            RequestState.BodyText(response).ReplaceLineEndings("\n").ShouldBe(shown);
        }

        [Fact]
        public void A_body_that_is_not_text_is_described_instead_of_shown()
        {
            var response = new ProgramResponse(ProgramResponseOutcome.Answered, 200, null, [new("Content-Type", "image/png")], [0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0xFE], TimeSpan.Zero);

            RequestState.BodyText(response).ShouldBe("7 bytes that are not text.");
        }
    }
}
