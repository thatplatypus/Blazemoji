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
        private static readonly TimeSpan FallbackTimeout = TimeSpan.FromMinutes(2);

        /// <summary>
        /// How long what pmj printed is waited for once pmj has ended. Something pmj started can
        /// outlive it and keep its pipes open, and then the reads would never reach their end.
        /// </summary>
        private static readonly TimeSpan ReadPatience = TimeSpan.FromSeconds(5);

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
            using var reading = new CancellationTokenSource();
            var stdout = process.StandardOutput.ReadToEndAsync(reading.Token);
            var stderr = process.StandardError.ReadToEndAsync(reading.Token);

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
                timedOut = !cancellationToken.IsCancellationRequested;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await reading.CancelAsync();
                await Finish(stdout, stderr);
                cancellationToken.ThrowIfCancellationRequested();
            }

            reading.CancelAfter(ReadPatience);
            var printed = await Finish(stdout, stderr);
            if (printed is null)
            {
                logger.LogWarning("pmj ended but something it started kept its output open past {Patience}", ReadPatience);
                return new PmjResult(PmjOutcome.TimedOut, process.ExitCode, string.Empty, string.Empty);
            }

            return new PmjResult(timedOut ? PmjOutcome.TimedOut : PmjOutcome.Ran, process.ExitCode, printed.Value.Stdout, printed.Value.Stderr);
        }

        /// <summary>Awaits both reads to their end, whether that is the end of the output or a cancelled read. Null when cancelled.</summary>
        private static async Task<(string Stdout, string Stderr)?> Finish(Task<string> stdout, Task<string> stderr)
        {
            string? output = null;
            string? err = null;
            try
            {
                output = await stdout;
            }
            catch (OperationCanceledException)
            {
                // Cancelled, so the other read is awaited below all the same.
            }

            try
            {
                err = await stderr;
            }
            catch (OperationCanceledException)
            {
                // Cancelled.
            }

            return output is null || err is null ? null : (output, err);
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

        /// <summary>A limit that is zero, negative or more than a day is a mistake, and falls back to the default and not to the longest.</summary>
        internal static TimeSpan Usable(TimeSpan timeout) =>
            timeout > TimeSpan.Zero && timeout <= LongestTimeout ? timeout : FallbackTimeout;

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
