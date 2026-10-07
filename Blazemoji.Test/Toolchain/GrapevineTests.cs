using System.Text;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Toolchain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// Grapevine's own Todo sample, built and run the way the toolchain service does it, with
    /// the real compiler and under the real per-program limits. Needs the Grapevine package,
    /// which scripts/build-grapevine.sh puts in place.
    /// </summary>
    [Trait("Requires", "Grapevine")]
    public class GrapevineTests
    {
        private const string SkipReason = "The Grapevine package is not built. Run scripts/build-grapevine.sh, then the Docker tests.";

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static bool Available =>
            ToolchainFixture.Available && File.Exists(Path.Combine(AppContext.BaseDirectory, "packages", "grapevine", "libgrapevine.a"));

        private static ProjectTemplate TodoSample() =>
            new FileProjectTemplates(Options.Create(new ProjectTemplateOptions()), NullLogger<FileProjectTemplates>.Instance)
                .All.Single(template => template.Id == "grapevine-todo");

        private static CompileRequest Request(ProjectTemplate template) =>
            new(template.Files.ToDictionary(file => file.Path, file => file.Content), template.Entry);

        private static ProgramRequest Json(string method, string path, string body = "") =>
            new(method, path, [new("Content-Type", "application/json")], Encoding.UTF8.GetBytes(body));

        [Fact]
        public void The_sample_is_offered_as_a_server_project()
        {
            Assert.SkipUnless(Available, SkipReason);

            var sample = TodoSample();

            sample.Kind.ShouldBe(ProjectKind.Server);
            sample.Entry.ShouldBe("main.🍇");
            sample.Files.Select(file => file.Path).ShouldContain("todos.🍇");
        }

        [Fact]
        public async Task The_sample_compiles_and_links_against_the_bundled_package()
        {
            Assert.SkipUnless(Available, SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var build = await toolchain.CompileAsync(Request(TodoSample()), Cancellation);

            build.Ok.ShouldBeTrue(string.Join("; ", build.Diagnostics.Select(d => $"{d.File} {d.Line}:{d.Character} {d.Message}")));
            build.Diagnostics.ShouldAllBe(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
        }

        [Fact]
        public async Task The_sample_creates_reads_updates_and_deletes_a_todo_under_the_real_limits()
        {
            Assert.SkipUnless(Available, SkipReason);
            await using var toolchain = ToolchainFixture.Create();
            var build = await toolchain.CompileAsync(Request(TodoSample()), Cancellation);
            build.Ok.ShouldBeTrue();

            await using var run = await toolchain.StartRunAsync(new RunRequest(build.BuildId!, Server: true), Cancellation);
            var output = new StringBuilder();
            var events = run.ReadEventsAsync(Cancellation).GetAsyncEnumerator(Cancellation);
            while (!output.ToString().Contains("listening") && await events.MoveNextAsync())
            {
                output.Append((events.Current as StdoutEvent)?.Text);
                output.Append((events.Current as StderrEvent)?.Text);
                events.Current.ShouldNotBeOfType<ExitEvent>(output.ToString());
            }

            var created = await run.SendHttpAsync(Json("POST", "/todos", "{\"title\":\"Buy grapes\"}"), Cancellation);
            created.Outcome.ShouldBe(ProgramResponseOutcome.Answered);
            created.StatusCode.ShouldBe(201);
            created.Headers.ShouldContain(new KeyValuePair<string, string>("Location", "/todos/1"));
            Encoding.UTF8.GetString(created.Body).ShouldBe("{\"id\":1,\"title\":\"Buy grapes\",\"done\":false}");

            var listed = await run.SendHttpAsync(Json("GET", "/todos"), Cancellation);
            listed.StatusCode.ShouldBe(200);
            Encoding.UTF8.GetString(listed.Body).ShouldBe("[{\"id\":1,\"title\":\"Buy grapes\",\"done\":false}]");

            var updated = await run.SendHttpAsync(Json("PUT", "/todos/1", "{\"title\":\"Buy more grapes 🍇\",\"done\":true}"), Cancellation);
            updated.StatusCode.ShouldBe(200);
            Encoding.UTF8.GetString(updated.Body).ShouldBe("{\"id\":1,\"title\":\"Buy more grapes 🍇\",\"done\":true}");

            var filtered = await run.SendHttpAsync(Json("GET", "/todos?done=true"), Cancellation);
            Encoding.UTF8.GetString(filtered.Body).ShouldContain("Buy more grapes 🍇");

            var deleted = await run.SendHttpAsync(Json("DELETE", "/todos/1"), Cancellation);
            deleted.StatusCode.ShouldBe(204);
            deleted.Body.ShouldBeEmpty();

            var gone = await run.SendHttpAsync(Json("GET", "/todos/1"), Cancellation);
            gone.StatusCode.ShouldBe(404);

            await run.StopAsync();
            ExitEvent? exit = null;
            while (exit is null && await events.MoveNextAsync())
                exit = events.Current as ExitEvent;
            await events.DisposeAsync();
            exit.ShouldNotBeNull().Reason.ShouldBe(RunEndReason.Stopped);
        }

        [Fact]
        public async Task The_samples_own_tests_pass_when_its_test_file_is_made_the_entry()
        {
            Assert.SkipUnless(Available, SkipReason);
            await using var toolchain = ToolchainFixture.Create();
            var sample = TodoSample();
            Assert.SkipUnless(sample.Files.Any(file => file.Path == "tests.🍇"), "This Grapevine commit ships no tests.🍇 with its sample.");

            var build = await toolchain.CompileAsync(Request(sample) with { Entry = "tests.🍇" }, Cancellation);
            build.Ok.ShouldBeTrue(string.Join("; ", build.Diagnostics.Select(d => $"{d.File} {d.Line}:{d.Character} {d.Message}")));
            await using var run = await toolchain.StartRunAsync(new RunRequest(build.BuildId!), Cancellation);
            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.ExitCode.ShouldBe(0, finished.Stdout + finished.Stderr);
        }
    }
}
