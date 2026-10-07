using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Blazemoji.Toolchain.Local
{
    internal sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

    /// <summary>
    /// Starting, bounding and ending child processes.
    /// </summary>
    internal static class ProcessRunner
    {
        /// <summary>
        /// The longest time limit a timer accepts comfortably. Anything larger, or not positive, is replaced.
        /// </summary>
        public static readonly TimeSpan LongestTimeout = TimeSpan.FromDays(1);

        private const int SigKill = 9;

        // A byte-order mark written ahead of the first input would reach the program as data.
        private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Runs a short-lived process to completion and collects its output.
        /// </summary>
        public static async Task<ProcessResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var process = new Process { StartInfo = CreateStartInfo(fileName, arguments, workingDirectory) };
            process.Start();
            process.StandardInput.Close();

            // Both pipes are drained while the process runs. Reading one after the other
            // deadlocks as soon as the unread pipe's buffer fills.
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(Usable(timeout, LongestTimeout));

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                Kill(process, wholeGroup: false);
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
                // Redirected so that the child never reads the server's own stdin.
                RedirectStandardInput = true,
                StandardInputEncoding = Utf8WithoutBom,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            return startInfo;
        }

        /// <summary>
        /// A time limit that a timer will accept: the given one when it is positive and not
        /// absurdly long, otherwise the fallback.
        /// </summary>
        public static TimeSpan Usable(TimeSpan timeout, TimeSpan fallback) =>
            timeout > TimeSpan.Zero && timeout <= LongestTimeout ? timeout : fallback;

        /// <param name="wholeGroup">
        /// The process leads its own process group (it was started through setsid), so every
        /// process it started is ended with it, including ones that have outlived their parent.
        /// </param>
        public static void Kill(Process process, bool wholeGroup)
        {
            try
            {
                if (wholeGroup)
                    kill(-process.Id, SigKill);

                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // The process had already gone.
            }
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int kill(int pid, int signal);
    }
}
