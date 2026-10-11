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

        private const int DefaultMaxOutput = 4 * 1024 * 1024;

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
            var most = _options.MaxOutput > 0 ? _options.MaxOutput : DefaultMaxOutput;
            var stdout = ReadAsync(process.StandardOutput, most, reading.Token);
            var stderr = ReadAsync(process.StandardError, most, reading.Token);

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

            var outcome = timedOut ? PmjOutcome.TimedOut : PmjOutcome.Ran;
            if (printed.Value.Stdout.Cut)
            {
                // Half of a JSON object is not one. Nothing is given back, so that whoever
                // reads it finds no answer and not a wrong one.
                logger.LogWarning("pmj printed more than {Most} characters, and what it printed is not used", most);
                return new PmjResult(outcome, process.ExitCode, string.Empty, printed.Value.Stderr.Text);
            }

            return new PmjResult(outcome, process.ExitCode, printed.Value.Stdout.Text, printed.Value.Stderr.Text);
        }

        private readonly record struct Printed(string Text, bool Cut);

        /// <summary>
        /// Reads a stream to its end and keeps the first of it. It goes on reading past the
        /// limit and keeps none of that: a pipe nobody reads fills, and pmj would wait on it
        /// until its time ran out.
        /// </summary>
        private static async Task<Printed> ReadAsync(StreamReader reader, int most, CancellationToken cancellationToken)
        {
            var kept = new StringBuilder();
            var cut = false;
            var buffer = new char[8192];
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                var room = most - kept.Length;
                if (room >= read)
                {
                    kept.Append(buffer, 0, read);
                    continue;
                }

                if (room > 0)
                    kept.Append(buffer, 0, room);
                cut = true;
            }

            return new Printed(kept.ToString(), cut);
        }

        /// <summary>Awaits both reads to their end, whether that is the end of the output or a cancelled read. Null when cancelled.</summary>
        private static async Task<(Printed Stdout, Printed Stderr)?> Finish(Task<Printed> stdout, Task<Printed> stderr)
        {
            Printed? output = null;
            Printed? err = null;
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

            return output is null || err is null ? null : (output.Value, err.Value);
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
