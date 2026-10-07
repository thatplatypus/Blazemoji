using System.Text;
using Blazemoji.Toolchain;

namespace Blazemoji.Test.Toolchain
{
    internal sealed record FinishedRun(string Stdout, string Stderr, ExitEvent Exit, int EventsAfterExit);

    internal static class RunExtensions
    {
        public static async Task<FinishedRun> RunToEndAsync(this IToolchainRun run, CancellationToken cancellationToken)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            ExitEvent? exit = null;
            var eventsAfterExit = 0;

            await foreach (var runEvent in run.ReadEventsAsync(cancellationToken))
            {
                if (exit is not null)
                    eventsAfterExit++;

                switch (runEvent)
                {
                    case StdoutEvent output:
                        stdout.Append(output.Text);
                        break;
                    case StderrEvent error:
                        stderr.Append(error.Text);
                        break;
                    case ExitEvent exited:
                        exit = exited;
                        break;
                }
            }

            exit.ShouldNotBeNull("the event stream ended without an exit event");
            return new FinishedRun(stdout.ToString(), stderr.ToString(), exit, eventsAfterExit);
        }

        public static async Task<FinishedRun> CompileAndRunAsync(this IToolchain toolchain, string code, CancellationToken cancellationToken)
        {
            var build = await toolchain.CompileAsync(ToolchainFixture.SingleFile(code), cancellationToken);
            build.Ok.ShouldBeTrue(string.Join("; ", build.Diagnostics.Select(d => $"{d.Line}:{d.Character} {d.Message}")));

            await using var run = await toolchain.StartRunAsync(new RunRequest(build.BuildId!), cancellationToken);
            return await run.RunToEndAsync(cancellationToken);
        }
    }
}
