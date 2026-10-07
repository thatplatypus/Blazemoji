using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blazemoji.Toolchain.Local
{
    /// <summary>
    /// Runs the Emojicode compiler and the programs it produces as local processes.
    /// Every build and every run gets its own directory under <see cref="ToolchainOptions.WorkRoot"/>.
    /// </summary>
    public sealed partial class LocalToolchain(IOptions<ToolchainOptions> options, ILogger<LocalToolchain> logger) : IToolchain, IBuildStore, IAsyncDisposable
    {
        private const string ProgramFileName = "program";
        private const string ObjectFileName = "program.o";
        private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

        private static readonly Lazy<string?> LineBufferingTool = new(() => FindOnPath("stdbuf"));
        private static readonly Lazy<string?> SessionTool = new(() => FindOnPath("setsid"));
        private static readonly Lazy<string?> LimitTool = new(() => FindOnPath("prlimit"));

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

                diagnostics = diagnostics
                    .Select(CompilerPositions.Normalize)
                    .Select(diagnostic => diagnostic with { File = ProjectPath(diagnostic.File, buildDirectory) })
                    .ToList();

                if (compiler.ExitCode != 0)
                {
                    if (!diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        logger.LogError("Compiler exited with code {ExitCode} and no error diagnostic. stderr: {Stderr}", compiler.ExitCode, compiler.Stderr);
                        diagnostics = [.. diagnostics, Error("The compiler failed without reporting an error.")];
                    }

                    return new CompileResult(false, diagnostics, null);
                }

                if (request.CheckOnly)
                    return new CompileResult(true, diagnostics, null);

                if (!File.Exists(Path.Combine(buildDirectory, ObjectFileName)))
                {
                    logger.LogError("The compiler exited with 0 but produced no object file. stderr: {Stderr}", compiler.Stderr);
                    return new CompileResult(false, [.. diagnostics, Error("The compiler failed without reporting an error.")], null);
                }

                if (!await LinkAsync(buildDirectory, cancellationToken))
                    return new CompileResult(false, [.. diagnostics, Error("The program could not be linked.")], null);

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

            var endpoint = request.Server
                ? new ProgramEndpoint(
                    FreeLoopbackPort(),
                    ProcessRunner.Usable(_options.ProgramResponseTimeout, ProcessRunner.LongestTimeout),
                    _options.MaxProgramResponseBytes)
                : null;

            var process = new Process { StartInfo = CreateRunStartInfo(program, runDirectory, request.Environment, endpoint?.Port) };
            try
            {
                process.Start();
            }
            catch (Win32Exception exception)
            {
                logger.LogError(exception, "Could not start build {BuildId}", request.BuildId);
                process.Dispose();
                endpoint?.Dispose();
                return Task.FromResult(Track(LocalRun.FailedToStart(runId, runDirectory, Forget)));
            }

            var leadsProcessGroup = SessionTool.Value is not null;

            return Task.FromResult(Track(LocalRun.Started(runId, process, runDirectory, TimeLimit(request), _options.MaxOutputBytes, leadsProcessGroup, endpoint, Forget)));
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
        /// <param name="serverPort">Set for a server run: the port the program is told to listen on.</param>
        private ProcessStartInfo CreateRunStartInfo(string program, string runDirectory, IReadOnlyDictionary<string, string>? environment, int? serverPort)
        {
            List<string> command = LineBufferingTool.Value is { } stdbuf ? [stdbuf, "-oL", "-eL", program] : [program];

            // A count of processes is not limited: it is counted per user, and every run shares
            // the service's user, so one run's threads would count against all the others.
            if (LimitTool.Value is { } prlimit)
            {
                command.InsertRange(0,
                [
                    prlimit,
                    // Soft limit, then a hard one a second later. At the soft limit the program is
                    // sent a signal that says why it is being ended, which is how a run comes to be
                    // reported as out of time; the hard limit is for a program that ignores it.
                    $"--cpu={CpuSeconds(serverPort)}:{CpuSeconds(serverPort) + 1}",
                    $"--as={_options.MemoryBytes}",
                    $"--fsize={_options.MaxFileBytes}",
                    $"--nofile={_options.MaxOpenFiles}",
                    "--",
                ]);
            }

            // A session of its own makes the program a process group leader, so that ending the
            // run can end everything the program started, including what has outlived it.
            if (SessionTool.Value is { } setsid)
                command.Insert(0, setsid);

            var startInfo = ProcessRunner.CreateStartInfo(command[0], command.Skip(1), runDirectory);

            foreach (var (name, value) in environment ?? new Dictionary<string, string>())
                startInfo.Environment[name] = value;

            if (serverPort is { } port)
                startInfo.Environment["PORT"] = port.ToString(CultureInfo.InvariantCulture);

            return startInfo;
        }

        private int CpuSeconds(int? serverPort) => serverPort is null ? _options.CpuSeconds : _options.ServerCpuSeconds;

        /// <summary>
        /// A script is ended when it has run for too long. A server is meant to keep running,
        /// so its limit is on how long it may go without being asked anything.
        /// </summary>
        private TimeSpan TimeLimit(RunRequest request)
        {
            if (request.Server)
                return ProcessRunner.Usable(_options.ServerIdleTimeout, ProcessRunner.LongestTimeout);

            var fallback = ProcessRunner.Usable(_options.RunTimeout, ProcessRunner.LongestTimeout);
            return ProcessRunner.Usable(request.Timeout ?? fallback, fallback);
        }

        /// <summary>
        /// Asks the system for a port nobody is using. Another process could take it before the
        /// program binds it; then the program fails to listen and requests say so.
        /// </summary>
        private static int FreeLoopbackPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
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
            string[] arguments = [entry, "--json", "-c", "-o", Path.Combine(buildDirectory, ObjectFileName), "-S", _options.PackagesPath];

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

        /// <summary>
        /// The compiler can link, but it lists package archives in import order, which fails as
        /// soon as one package uses another (Grapevine uses json). It also builds that command
        /// as a string for a shell and ignores the result. So the compiler only produces the
        /// object, and the link happens here: every package archive in one group, where the
        /// order does not matter and the linker takes only what the program refers to. The two
        /// system libraries are the link hints of the standard package.
        /// </summary>
        private async Task<bool> LinkAsync(string buildDirectory, CancellationToken cancellationToken)
        {
            var program = Path.Combine(buildDirectory, ProgramFileName);
            string[] arguments =
            [
                Path.Combine(buildDirectory, ObjectFileName),
                "-Wl,--start-group",
                .. PackageArchives(),
                "-Wl,--end-group",
                "-lm",
                "-lpthread",
                "-o",
                program,
            ];

            try
            {
                var linker = await ProcessRunner.RunAsync(_options.LinkerPath, arguments, buildDirectory, _options.CompileTimeout, cancellationToken);
                if (!linker.TimedOut && linker.ExitCode == 0 && File.Exists(program))
                    return true;

                logger.LogError("Linking failed. Exit code {ExitCode}, timed out {TimedOut}. stderr: {Stderr}", linker.ExitCode, linker.TimedOut, linker.Stderr);
                return false;
            }
            catch (Win32Exception exception)
            {
                logger.LogError(exception, "Could not start the linker at {LinkerPath}", _options.LinkerPath);
                return false;
            }
        }

        private IEnumerable<string> PackageArchives()
        {
            if (!Directory.Exists(_options.PackagesPath))
                return [];

            return Directory.GetDirectories(_options.PackagesPath)
                .SelectMany(package => Directory.GetFiles(package, "lib*.a"))
                .Order(StringComparer.Ordinal);
        }

        /// <summary>
        /// The compiler names a file as it resolved it, which for an include can be
        /// <c>app/../shared/util.🍇</c> or an absolute path. Callers know files by the names
        /// they sent, so the path is made relative to the build directory.
        /// </summary>
        private static string ProjectPath(string file, string buildDirectory)
        {
            if (file.Length == 0)
                return file;

            var relative = Path.GetRelativePath(buildDirectory, Path.GetFullPath(file, buildDirectory));
            return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
                ? file
                : relative.Replace(Path.DirectorySeparatorChar, '/');
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
