using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blazemoji.Toolchain
{
    /// <summary>
    /// Runs the Emojicode compiler and the programs it produces as local processes.
    /// Every build and every run gets its own directory under <see cref="ToolchainOptions.WorkRoot"/>.
    /// </summary>
    public sealed partial class LocalToolchain(IOptions<ToolchainOptions> options, ILogger<LocalToolchain> logger) : IToolchain, IAsyncDisposable
    {
        private const string ProgramFileName = "program";
        private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

        private static readonly Lazy<string?> LineBufferingTool = new(() => FindOnPath("stdbuf"));
        private static readonly Lazy<string?> SessionTool = new(() => FindOnPath("setsid"));

        private readonly ToolchainOptions _options = options.Value;
        private readonly ConcurrentDictionary<string, LocalRun> _runs = new();

        /// <summary>
        /// This instance's own folder under the configured work root. Several instances can be
        /// given the same root (two app processes on one machine, say) without one deleting the
        /// other's builds when it shuts down.
        /// </summary>
        internal string WorkRoot { get; } = Path.Combine(options.Value.WorkRoot, Guid.NewGuid().ToString("N"));

        private string BuildsRoot => Path.Combine(WorkRoot, "builds");

        private string RunsRoot => Path.Combine(WorkRoot, "runs");

        public async Task<CompileResult> CompileAsync(CompileRequest request, CancellationToken cancellationToken = default)
        {
            if (Validate(request) is { } problem)
                return Failed(problem);

            SweepStaleBuilds();

            var buildId = Guid.NewGuid().ToString("N");
            var buildDirectory = Path.Combine(BuildsRoot, buildId);
            var keepBuild = false;

            try
            {
                await WriteSourcesAsync(request, buildDirectory, cancellationToken);

                var compiler = await RunCompilerAsync(request.Entry, buildDirectory, cancellationToken);
                if (compiler is null)
                    return Failed("The compiler could not be started.");

                if (compiler.TimedOut)
                    return Failed("The compiler took too long and was stopped.");

                if (!TryParseDiagnostics(compiler, out var diagnostics))
                    return Failed("The compiler produced output that could not be read.");

                diagnostics = diagnostics.Select(CompilerPositions.Normalize).ToList();

                if (compiler.ExitCode != 0)
                {
                    if (!diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        logger.LogError("Compiler exited with code {ExitCode} and no error diagnostic. stderr: {Stderr}", compiler.ExitCode, compiler.Stderr);
                        diagnostics = [.. diagnostics, Error("The compiler failed without reporting an error.")];
                    }

                    return new CompileResult(false, diagnostics, null);
                }

                // The compiler does not check its linker's result, so a link failure still exits with 0.
                if (!File.Exists(Path.Combine(buildDirectory, ProgramFileName)))
                {
                    logger.LogError("The compiler exited with 0 but produced no program. stderr: {Stderr}", compiler.Stderr);
                    return new CompileResult(false, [.. diagnostics, Error("The program could not be linked.")], null);
                }

                keepBuild = true;
                return new CompileResult(true, diagnostics, buildId);
            }
            finally
            {
                if (!keepBuild)
                    DeleteDirectory(buildDirectory);
            }
        }

        public Task<IToolchainRun> StartRunAsync(RunRequest request, CancellationToken cancellationToken = default)
        {
            var runId = Guid.NewGuid().ToString("N");
            var program = BuildIdPattern().IsMatch(request.BuildId)
                ? Path.Combine(BuildsRoot, request.BuildId, ProgramFileName)
                : null;

            if (program is null || !File.Exists(program))
            {
                logger.LogWarning("A run was asked for a build that does not exist");
                return Task.FromResult(Track(LocalRun.FailedToStart(runId, null, Forget)));
            }

            var runDirectory = Path.Combine(RunsRoot, runId);
            Directory.CreateDirectory(runDirectory);

            var process = new Process { StartInfo = CreateRunStartInfo(program, runDirectory, request.Environment) };
            try
            {
                process.Start();
                process.StandardInput.Close();
            }
            catch (Win32Exception exception)
            {
                logger.LogError(exception, "Could not start build {BuildId}", request.BuildId);
                process.Dispose();
                return Task.FromResult(Track(LocalRun.FailedToStart(runId, runDirectory, Forget)));
            }

            var fallback = ProcessRunner.Usable(_options.RunTimeout, ProcessRunner.LongestTimeout);
            var timeout = ProcessRunner.Usable(request.Timeout ?? fallback, fallback);
            var leadsProcessGroup = SessionTool.Value is not null;

            return Task.FromResult(Track(LocalRun.Started(runId, process, runDirectory, timeout, _options.MaxOutputBytes, leadsProcessGroup, Forget)));
        }

        public bool HasBuild(string buildId) =>
            BuildIdPattern().IsMatch(buildId) && File.Exists(Path.Combine(BuildsRoot, buildId, ProgramFileName));

        public Task ReleaseBuildAsync(string buildId)
        {
            if (BuildIdPattern().IsMatch(buildId))
                DeleteDirectory(Path.Combine(BuildsRoot, buildId));

            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var run in _runs.Values)
                await run.DisposeAsync();

            DeleteDirectory(WorkRoot);
        }

        /// <summary>
        /// Emojicode prints through a C stdio stream that is block-buffered when it is a pipe,
        /// so without help nothing arrives until the program exits. <c>stdbuf</c> makes it
        /// line-buffered. Where the tool is missing the program still runs, with late output.
        /// </summary>
        private static ProcessStartInfo CreateRunStartInfo(string program, string runDirectory, IReadOnlyDictionary<string, string>? environment)
        {
            List<string> command = LineBufferingTool.Value is { } stdbuf ? [stdbuf, "-oL", "-eL", program] : [program];

            // A session of its own makes the program a process group leader, so that ending the
            // run can end everything the program started, including what has outlived it.
            if (SessionTool.Value is { } setsid)
                command.Insert(0, setsid);

            var startInfo = ProcessRunner.CreateStartInfo(command[0], command.Skip(1), runDirectory);

            foreach (var (name, value) in environment ?? new Dictionary<string, string>())
                startInfo.Environment[name] = value;

            return startInfo;
        }

        private IToolchainRun Track(LocalRun run)
        {
            _runs[run.RunId] = run;
            return run;
        }

        private void Forget(LocalRun run)
        {
            _runs.TryRemove(run.RunId, out _);
            if (run.RunDirectory is { } directory)
                DeleteDirectory(directory);
        }

        private static string? FindOnPath(string fileName)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory, fileName))
                .FirstOrDefault(File.Exists);
        }

        private static string? Validate(CompileRequest request)
        {
            foreach (var name in request.Files.Keys)
            {
                if (!SourceFileNames.IsSafe(name))
                    return "A file name is not allowed. Use relative names without '..' segments.";
            }

            return request.Files.ContainsKey(request.Entry) ? null : "The entry file is not among the files.";
        }

        private static async Task WriteSourcesAsync(CompileRequest request, string buildDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(buildDirectory);

            foreach (var (name, content) in request.Files)
            {
                var path = Path.Combine(buildDirectory, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                // A byte-order mark makes the compiler fail with "Unexpected token Variable".
                await File.WriteAllTextAsync(path, content.TrimStart('﻿'), Utf8WithoutBom, cancellationToken);
            }
        }

        private async Task<ProcessResult?> RunCompilerAsync(string entry, string buildDirectory, CancellationToken cancellationToken)
        {
            string[] arguments = [entry, "--json", "-o", Path.Combine(buildDirectory, ProgramFileName), "-S", _options.PackagesPath];

            try
            {
                return await ProcessRunner.RunAsync(_options.CompilerPath, arguments, buildDirectory, _options.CompileTimeout, cancellationToken);
            }
            catch (Win32Exception exception)
            {
                logger.LogError(exception, "Could not start the compiler at {CompilerPath}", _options.CompilerPath);
                return null;
            }
        }

        private bool TryParseDiagnostics(ProcessResult compiler, out IReadOnlyList<Diagnostic> diagnostics)
        {
            try
            {
                diagnostics = DiagnosticsParser.Parse(compiler.Stdout);
                return true;
            }
            catch (FormatException exception)
            {
                logger.LogError(exception, "Unreadable compiler output. Exit code {ExitCode}. stdout: {Stdout} stderr: {Stderr}", compiler.ExitCode, compiler.Stdout, compiler.Stderr);
                diagnostics = [];
                return false;
            }
        }

        private void SweepStaleBuilds()
        {
            if (!Directory.Exists(BuildsRoot))
                return;

            var cutoff = DateTime.UtcNow - _options.BuildLifetime;
            foreach (var directory in Directory.GetDirectories(BuildsRoot))
            {
                if (Directory.GetCreationTimeUtc(directory) <= cutoff)
                    DeleteDirectory(directory);
            }
        }

        private void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not delete {Path}", path);
            }
        }

        private static CompileResult Failed(string message) => new(false, [Error(message)], null);

        private static Diagnostic Error(string message) => new(DiagnosticSeverity.Error, string.Empty, 0, 0, message);

        [GeneratedRegex(@"\A[0-9a-f]{32}\z")]
        private static partial Regex BuildIdPattern();
    }
}
