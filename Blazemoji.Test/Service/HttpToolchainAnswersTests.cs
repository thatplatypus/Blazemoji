using System.Net;
using System.Text;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blazemoji.Test.Service
{
    /// <summary>
    /// What the client makes of answers a toolchain service should never give, and one it
    /// gives for one reason only. None of them may be an exception.
    /// </summary>
    public class HttpToolchainAnswersTests
    {
        private static readonly Dictionary<string, string> Files = new() { ["main.🍇"] = "🏁 🍇 🍉" };

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static HttpToolchain Toolchain(HttpStatusCode status, string body = "", string mediaType = "application/json") =>
            new(
                new HttpClient(new Answering(status, body, mediaType)) { BaseAddress = new Uri("http://toolchain.test/") },
                NullLogger<HttpToolchain>.Instance);

        [Fact]
        public async Task A_409_for_a_compile_that_named_a_set_says_the_set_is_not_held()
        {
            var result = await Toolchain(HttpStatusCode.Conflict).CompileAsync(new CompileRequest(Files, "main.🍇", Dependencies: "set-1"), Cancellation);

            result.DependenciesMissing.ShouldBeTrue();
            result.ReachedCompiler.ShouldBeFalse();
            result.Ok.ShouldBeFalse();
        }

        [Fact]
        public async Task A_409_for_a_compile_that_named_no_set_cannot_mean_that_and_is_a_plain_failure()
        {
            var result = await Toolchain(HttpStatusCode.Conflict).CompileAsync(new CompileRequest(Files, "main.🍇"), Cancellation);

            result.DependenciesMissing.ShouldBeFalse();
            result.ReachedCompiler.ShouldBeFalse();
            result.Ok.ShouldBeFalse();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("The toolchain service could not compile the program.");
        }

        [Theory]
        [InlineData("<html>Bad gateway</html>", "text/html")]
        [InlineData("{ not json", "application/json")]
        [InlineData("null", "application/json")]
        [InlineData("""{"ok":true}""", "application/json")]
        public async Task A_200_that_is_not_the_services_answer_is_a_failure_in_fixed_words_and_nothing_is_thrown(string body, string mediaType)
        {
            var result = await Toolchain(HttpStatusCode.OK, body, mediaType).CompileAsync(new CompileRequest(Files, "main.🍇"), Cancellation);

            result.Ok.ShouldBeFalse();
            result.ReachedCompiler.ShouldBeFalse();
            result.BuildId.ShouldBeNull();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldNotContain("html");
        }

        private sealed class Answering(HttpStatusCode status, string body, string mediaType) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
        }
    }
}
