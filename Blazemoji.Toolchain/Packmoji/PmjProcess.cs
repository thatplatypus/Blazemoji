using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blazemoji.Toolchain.Packmoji
{
    /// <summary>
    /// pmj as a child process, started with its arguments and never through a shell.
    /// </summary>
    public sealed class PmjProcess(IOptions<PackmojiOptions> options, ILogger<PmjProcess> logger) : IPmj
    {
        private static readonly TimeSpan LongestTimeout = TimeSpan.FromDays(1);

        private readonly PackmojiOptions _options = options.Value;

        public async Task<PmjResult> RunAsync(PmjCommand command, CancellationToken cancellationToken = default)
        {
            using var process = new Process { StartInfo = StartInfo(command) };
            try
            {
                process.Start();
            }
            catch (Win32Exception exception)
            {
                logger.LogError(exception, "pmj could not be started at {Path}", _options.Path);
                return new PmjResult(PmjOutcome.NotFound, -1, string.Empty, string.Empty);
            }

            process.StandardInput.Close();

            // Both pipes are drained while pmj runs. Reading one after the other deadlocks as
            // soon as the unread pipe's buffer fills, and a build prints a compiler's output.
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(Usable(command.Timeout ?? _options.Timeout));

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                End(process);
                await process.WaitForExitAsync(CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
                timedOut = true;
            }

            return new PmjResult(timedOut ? PmjOutcome.TimedOut : PmjOutcome.Ran, process.ExitCode, await stdout, await stderr);
        }

        private ProcessStartInfo StartInfo(PmjCommand command)
        {
            var startInfo = new ProcessStartInfo(_options.Path)
            {
                WorkingDirectory = command.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Redirected so that pmj never reads this process's own input.
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var argument in command.Arguments)
                startInfo.ArgumentList.Add(argument);

            // A token this process happens to hold is not pmj's to send anywhere. Whoever
            // means pmj to have one gives it with the command.
            foreach (var token in PmjVariables.Tokens)
                startInfo.Environment.Remove(token);

            if (_options.Home is { Length: > 0 } home)
                startInfo.Environment[PmjVariables.Home] = home;

            foreach (var (name, value) in command.Environment ?? new Dictionary<string, string>())
                startInfo.Environment[name] = value;

            return startInfo;
        }

        private static TimeSpan Usable(TimeSpan timeout) =>
            timeout > TimeSpan.Zero && timeout <= LongestTimeout ? timeout : LongestTimeout;

        /// <summary>A build starts a compiler, so everything pmj started is ended with it.</summary>
        private static void End(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // It had already gone.
            }
        }
    }
}
