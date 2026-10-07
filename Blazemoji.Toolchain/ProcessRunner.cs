using System.Diagnostics;
using System.Text;

namespace Blazemoji.Toolchain
{
    internal sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

    /// <summary>
    /// Runs a short-lived process to completion and collects its output.
    /// </summary>
    internal static class ProcessRunner
    {
        public static async Task<ProcessResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var process = new Process { StartInfo = CreateStartInfo(fileName, arguments, workingDirectory) };
            process.Start();

            // Both pipes are drained while the process runs. Reading one after the other
            // deadlocks as soon as the unread pipe's buffer fills.
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                KillTree(process);
                await process.WaitForExitAsync(CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
                timedOut = true;
            }

            return new ProcessResult(process.ExitCode, await stdout, await stderr, timedOut);
        }

        public static ProcessStartInfo CreateStartInfo(string fileName, IEnumerable<string> arguments, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            return startInfo;
        }

        public static void KillTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the check and the kill.
            }
        }
    }
}
