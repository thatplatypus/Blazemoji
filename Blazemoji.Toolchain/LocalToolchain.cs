using System.ComponentModel;
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

        private readonly ToolchainOptions _options = options.Value;

        internal string WorkRoot => _options.WorkRoot;

        private string BuildsRoot => Path.Combine(_options.WorkRoot, "builds");

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

                if (compiler.ExitCode != 0)
                {
                    if (!diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        logger.LogError("Compiler exited with code {ExitCode} and no error diagnostic. stderr: {Stderr}", compiler.ExitCode, compiler.Stderr);
                        diagnostics = [.. diagnostics, Error("The compiler failed without reporting an error.")];
                    }

                    return new CompileResult(false, diagnostics, null);
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

        public Task<IToolchainRun> StartRunAsync(RunRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task ReleaseBuildAsync(string buildId)
        {
            if (BuildIdPattern().IsMatch(buildId))
                DeleteDirectory(Path.Combine(BuildsRoot, buildId));

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DeleteDirectory(_options.WorkRoot);
            return ValueTask.CompletedTask;
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

        [GeneratedRegex("^[0-9a-f]{32}$")]
        private static partial Regex BuildIdPattern();
    }
}
