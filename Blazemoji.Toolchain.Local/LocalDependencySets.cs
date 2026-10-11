using System.Security.Cryptography;
using System.Text;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blazemoji.Toolchain.Local
{
    /// <summary>
    /// Builds the packages a lockfile locks, by running pmj where the compiler is, and keeps
    /// what was built for the compiles that follow. Each set has a folder of its own under
    /// this instance's own root, and pmj's cache and its built packages are in a folder that
    /// every instance on the machine shares.
    /// </summary>
    public sealed class LocalDependencySets(
        IOptions<ToolchainOptions> toolchainOptions,
        IOptions<PackmojiOptions> packmojiOptions,
        IPmj pmj,
        ILogger<LocalDependencySets> logger) : IToolchainPackages, IDependencySetStore, IAsyncDisposable
    {
        private const string ManifestFile = "packmoji.json";
        private const string LockfileFile = "packmoji.lock";
        private const string PackagesFolder = "packages";
        private const string DocumentationFile = "documentation.json";

        // Packmoji's own limits on its two files. A larger one is refused by pmj anyway;
        // refusing it here saves writing it to disk first.
        private const int MaxManifestBytes = 1024 * 1024;
        private const int MaxLockfileBytes = 4 * 1024 * 1024;

        // pmj is given this for GitHub's two addresses. Nothing listens there, so whatever a
        // lockfile says, this service asks nothing of GitHub: every archive it builds from was
        // handed to it. Port 9 is the discard port, which nothing here runs.
        private const string NoGitHub = "http://127.0.0.1:9";

        private const string CompilerSaidReason = "what the compiler said is in the toolchain's log";

        private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

        // The codes of the problems pmj finds in a visitor's two files and in the packages they
        // name, which are the visitor's to read and fix. A code that is not here is about this
        // machine or is not known, and is not passed on: it goes to the log.
        private static readonly string[] PassedOnPrefixes =
        [
            "file.", "json.", "key.", "name.", "version.", "requirement.", "kind.", "description.",
            "license.", "repository.", "dependency.", "path.", "glob.", "link.", "list.", "entry.",
            "lock.", "pin.", "sha256.", "verified.", "resolve.", "release.", "archive.",
            "attestation.", "native.",
        ];

        private static readonly string[] PassedOnCodes = ["compiler.invalid", "compiler.too-old", "package.not-found"];

        private static readonly string[] CompilerCodes = ["build.compile-failed", "build.native-failed", "build.archive-failed", "build.link-failed"];

        private readonly ToolchainOptions _options = toolchainOptions.Value;
        private readonly PackageScopes _scopes = PackageScopes.Parse(packmojiOptions.Value.Scopes, PackageScopes.Any);
        private readonly string _home = packmojiOptions.Value.Home is { Length: > 0 } home
            ? home
            : Path.Combine(toolchainOptions.Value.WorkRoot, "packmoji");
        private readonly string _root = Path.Combine(toolchainOptions.Value.WorkRoot, "sets-" + Guid.NewGuid().ToString("N"));

        private readonly object _gate = new();
        private readonly Dictionary<string, BuiltSet> _sets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Task<DependencyAnswer>> _building = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource _shutdown = new();
        private long _uses;
        private int _asking;
        private bool _disposed;

        public async Task<DependencyAnswer> PrepareAsync(DependencyRequest request, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);

            if (Encoding.UTF8.GetByteCount(request.Manifest) > MaxManifestBytes || Encoding.UTF8.GetByteCount(request.Lockfile) > MaxLockfileBytes)
            {
                return Failed(
                    PackageProblemCodes.TooLarge,
                    "The project's package files are too large.",
                    "a manifest is at most 1 MiB and a lockfile at most 4 MiB",
                    "check that packmoji.json and packmoji.lock are the files pmj wrote");
            }

            lock (_gate)
            {
                if (_asking >= Math.Max(1, _options.MaxConcurrentDependencyRequests))
                    return new DependencyAnswer.Busy();

                _asking++;
            }

            IReadOnlyList<LockedPackage> packages;
            try
            {
                var (answer, locked) = await ReadLockedAsync(request, cancellationToken);
                if (answer is not null)
                    return answer;

                packages = locked;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogError(exception, "The folder for a set could not be written under {Root}", _root);
                return Unreadable();
            }
            finally
            {
                lock (_gate)
                    _asking--;
            }

            if (packages.Any(package => !package.IsWellFormed))
            {
                logger.LogError("pmj listed a locked package whose name, version or digest is not well formed");
                return Unreadable();
            }

            var refused = packages.Where(package => !_scopes.Allows(package.Name)).Select(OutsideTheScopes).ToList();
            if (refused.Count > 0)
                return new DependencyAnswer.Failed(refused);

            var needed = packages.Where(package => !File.Exists(PackmojiCache.ArchivePath(_home, package))).ToList();
            if (needed.Count > 0)
                return new DependencyAnswer.Needed(needed);

            var id = SetId(packages);
            Task<DependencyAnswer> build;
            lock (_gate)
            {
                if (_sets.TryGetValue(id, out var held))
                {
                    held.LastUse = ++_uses;
                    return new DependencyAnswer.Ready(id, held.BareNames);
                }

                if (!_building.TryGetValue(id, out build!))
                {
                    if (_building.Count >= Math.Max(1, _options.MaxConcurrentDependencyBuilds))
                        return new DependencyAnswer.Busy();

                    build = BuildOnceAsync(id, request);
                    _building[id] = build;
                }
            }

            // The build is everyone's who asks for this set, so it is not the first asker's to
            // stop. Someone who gives up stops waiting; the build goes on, stays in the table
            // above, and is what the next asker waits for. What ends a build nobody is waiting
            // for is pmj's own time limit, or this class being disposed.
            return await build.WaitAsync(cancellationToken);
        }

        public async Task<ArchiveOutcome> AddArchiveAsync(LockedPackage package, Stream content, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);

            if (PackmojiCache.ArchivePath(_home, package) is not { } path)
                return ArchiveOutcome.Malformed;

            if (!_scopes.Allows(package.Name))
                return ArchiveOutcome.NotAllowed;

            // Written beside its place and moved there whole, so pmj never reads half of one.
            var arriving = $"{path}.{Guid.NewGuid():N}.arriving";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long length = 0;
                await using (var file = new FileStream(arriving, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        length += read;
                        if (length > _options.MaxArchiveBytes)
                            return ArchiveOutcome.TooLarge;

                        hash.AppendData(buffer, 0, read);
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }

                if (Convert.ToHexStringLower(hash.GetHashAndReset()) != package.Sha256)
                    return ArchiveOutcome.WrongDigest;

                // The bytes are the ones their name says, so one already there is the same file.
                File.Move(arriving, path, overwrite: true);
                return ArchiveOutcome.Stored;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogError(exception, "An archive could not be kept under {Home}", _home);
                return ArchiveOutcome.Failed;
            }
            finally
            {
                DeleteFile(arriving);
            }
        }

        public async Task<string?> GetDocumentationAsync(string dependencies, string package, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);

            string? path;
            lock (_gate)
            {
                path = _sets.TryGetValue(dependencies, out var set)
                    && set.PackagesDirectory is { } directory
                    && set.BareNames.Contains(package, StringComparer.Ordinal)
                        ? Path.Combine(directory, package, DocumentationFile)
                        : null;
            }

            try
            {
                return path is not null && File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The set was dropped for room between being found and being read.
                logger.LogWarning(exception, "A documentation report could not be read");
                return null;
            }
        }

        public DependencySetLease? Lease(string id)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);

            lock (_gate)
            {
                if (!_sets.TryGetValue(id, out var set))
                    return null;

                set.Leases++;
                set.LastUse = ++_uses;
                return new DependencySetLease(set.PackagesDirectory, set.Archives, set.LinkLibraries, () => Release(set));
            }
        }

        public async ValueTask DisposeAsync()
        {
            Task<DependencyAnswer>[] running;
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                running = [.. _building.Values];
            }

            await _shutdown.CancelAsync();
            await Task.WhenAll(running);
            DeleteDirectory(_root);
            _shutdown.Dispose();
        }

        private void Release(BuiltSet set)
        {
            lock (_gate)
                set.Leases--;
        }

        /// <summary>
        /// What the lockfile locks, by pmj's own reading of it. pmj reads the two files and
        /// asks nothing of anyone for this.
        /// </summary>
        private async Task<(DependencyAnswer? Answer, IReadOnlyList<LockedPackage> Packages)> ReadLockedAsync(DependencyRequest request, CancellationToken cancellationToken)
        {
            var folder = Path.Combine(_root, "asks", Guid.NewGuid().ToString("N"));
            try
            {
                await WriteProjectAsync(folder, request, cancellationToken);

                var result = await pmj.RunAsync(new PmjCommand(folder, ["tree", "--json"], Environment: Settings()), cancellationToken);
                if (NotRun(result) is { } answer)
                    return (answer, []);

                if (PmjJson.ReadTree(result.Stdout) is not { } tree)
                {
                    logger.LogError("pmj tree printed what could not be read. Status {ExitCode}. stderr: {Stderr}", result.ExitCode, result.Stderr);
                    return (Unreadable(), []);
                }

                return tree.Report.Ok ? (null, tree.Packages) : (FailedWith(tree.Report.Problems), []);
            }
            finally
            {
                DeleteDirectory(folder);
            }
        }

        private async Task<DependencyAnswer> BuildOnceAsync(string id, DependencyRequest request)
        {
            // Started while the table is locked: this hands the lock back before any work.
            await Task.Yield();

            try
            {
                return await BuildAsync(id, request);
            }
            catch (Exception exception)
            {
                // Whatever went wrong, everyone waiting gets an answer and the next asker a fresh build.
                if (Volatile.Read(ref _disposed))
                    logger.LogInformation("Building set {Id} was ended because the toolchain is shutting down", id);
                else
                    logger.LogError(exception, "Building set {Id} failed", id);

                return Unreadable();
            }
            finally
            {
                lock (_gate)
                    _building.Remove(id);
            }
        }

        private async Task<DependencyAnswer> BuildAsync(string id, DependencyRequest request)
        {
            var folder = Path.Combine(_root, $"{id}-{Guid.NewGuid():N}");
            var kept = false;
            try
            {
                var token = _shutdown.Token;
                await WriteProjectAsync(folder, request, token);

                var result = await pmj.RunAsync(
                    new PmjCommand(folder, ["build", "--dependencies-only", "--json"], _options.DependencyBuildTimeout, BuildSettings()),
                    token);
                if (NotRun(result) is { } answer)
                    return answer;

                if (PmjJson.ReadBuild(result.Stdout) is not { } build)
                {
                    logger.LogError("pmj build printed what could not be read. Status {ExitCode}. stderr: {Stderr}", result.ExitCode, result.Stderr);
                    return Unreadable();
                }

                if (!build.Report.Ok)
                {
                    // What the compiler said of a package's code is the log's to hold.
                    logger.LogWarning("The packages of set {Id} could not be built. What the tools printed: {Stderr}", id, result.Stderr);
                    return FailedWith(build.Report.Problems);
                }

                if (Describe(folder, build.Packages) is not { } set)
                {
                    logger.LogError("pmj build said it built set {Id}, and what it left in {Folder} is not that", id, folder);
                    return Unreadable();
                }

                List<string> dropped;
                lock (_gate)
                {
                    set.LastUse = ++_uses;
                    _sets[id] = set;
                    dropped = DropForRoom(keep: id);
                }

                kept = true;
                foreach (var gone in dropped)
                    DeleteDirectory(gone);

                return new DependencyAnswer.Ready(id, set.BareNames);
            }
            finally
            {
                if (!kept)
                    DeleteDirectory(folder);
            }
        }

        /// <summary>
        /// The set as it lies on disk. The names and the libraries are pmj's word; the paths
        /// are worked out here from where pmj is documented to put a package, and a name or a
        /// library that could not be what it claims makes the whole set unusable.
        /// </summary>
        private static BuiltSet? Describe(string folder, IReadOnlyList<BuiltPackage> packages)
        {
            if (packages.Count == 0)
                return new BuiltSet(folder, null, [], [], []);

            var packagesDirectory = Path.Combine(folder, PackagesFolder);
            var bareNames = packages.Select(package => package.BareName).Order(StringComparer.Ordinal).ToList();
            if (bareNames.Any(name => !PackageNames.IsBareName(name)) || bareNames.Distinct(StringComparer.Ordinal).Count() != bareNames.Count)
                return null;

            var archives = bareNames.Select(name => Path.Combine(packagesDirectory, name, $"lib{name}.a")).ToList();
            if (archives.Any(archive => !File.Exists(archive)))
                return null;

            var libraries = packages.SelectMany(package => package.Link).Distinct(StringComparer.Ordinal).ToList();
            if (libraries.Any(library => !PackageNames.IsLinkLibrary(library)))
                return null;

            return new BuiltSet(folder, packagesDirectory, archives, libraries, bareNames);
        }

        /// <summary>Called with the table locked. Gives the folders to delete once it is not.</summary>
        private List<string> DropForRoom(string keep)
        {
            var dropped = new List<string>();
            while (_sets.Count > Math.Max(1, _options.MaxDependencySets))
            {
                var oldest = _sets
                    .Where(entry => entry.Key != keep && entry.Value.Leases == 0)
                    .OrderBy(entry => entry.Value.LastUse)
                    .Select(entry => entry.Key)
                    .FirstOrDefault();
                if (oldest is null)
                    break;

                dropped.Add(_sets[oldest].Folder);
                _sets.Remove(oldest);
            }

            return dropped;
        }

        private static string SetId(IReadOnlyList<LockedPackage> packages)
        {
            var lines = packages
                .OrderBy(package => package.Name, StringComparer.Ordinal)
                .Select(package => $"{package.Name}@{package.Version} {package.Sha256}");

            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
        }

        private static async Task WriteProjectAsync(string folder, DependencyRequest request, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, ManifestFile), request.Manifest, Utf8WithoutBom, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, LockfileFile), request.Lockfile, Utf8WithoutBom, cancellationToken);
        }

        private Dictionary<string, string> Settings() => new()
        {
            [PmjVariables.Home] = _home,
            [PmjVariables.GitHub] = NoGitHub,
            [PmjVariables.GitHubApi] = NoGitHub,
        };

        private Dictionary<string, string> BuildSettings()
        {
            var settings = Settings();
            settings[PmjVariables.Compiler] = _options.CompilerPath;
            settings[PmjVariables.CompilerPackages] = _options.PackagesPath;
            settings[PmjVariables.CompilerInclude] = Path.Combine(Path.GetDirectoryName(_options.CompilerPath) ?? string.Empty, "include");
            settings[PmjVariables.Cxx] = _options.LinkerPath;
            return settings;
        }

        private static DependencyAnswer? NotRun(PmjResult result) => result.Outcome switch
        {
            PmjOutcome.NotFound => Failed(
                PackageProblemCodes.NoPackmoji,
                "This toolchain has no Packmoji.",
                "the pmj program is not where the toolchain looks for it",
                "run a toolchain that came with pmj, or say where it is with Packmoji:Path"),
            PmjOutcome.TimedOut => Failed(
                PackageProblemCodes.TimedOut,
                "The project's packages took too long.",
                "pmj was stopped when it ran out of time",
                "try again: a package that was built is not built a second time"),
            _ => null,
        };

        private DependencyAnswer FailedWith(IReadOnlyList<PackageProblem> problems)
        {
            var passedOn = new List<PackageProblem>();
            var withheldError = false;
            foreach (var reported in problems)
            {
                var problem = reported;
                if (CompilerCodes.Contains(problem.Code))
                {
                    logger.LogWarning("pmj reported {Code}: {Message} ({Reason})", problem.Code, problem.Message, problem.Reason);
                    problem = problem with { Reason = CompilerSaidReason };
                }

                if (!IsPassedOn(problem))
                {
                    logger.LogError("pmj reported {Code}: {Message} ({Reason})", reported.Code, reported.Message, reported.Reason);
                    withheldError |= problem.IsError;
                    continue;
                }

                passedOn.Add(problem);
            }

            return withheldError || !passedOn.Any(problem => problem.IsError) ? Unreadable() : new DependencyAnswer.Failed(passedOn);
        }

        private bool IsPassedOn(PackageProblem problem)
        {
            var known = PassedOnCodes.Contains(problem.Code)
                || CompilerCodes.Contains(problem.Code)
                || PassedOnPrefixes.Any(prefix => problem.Code.StartsWith(prefix, StringComparison.Ordinal));
            if (!known)
                return false;

            var places = new[] { _options.WorkRoot, _root, _home }.Where(place => !string.IsNullOrEmpty(place));
            return !places.Any(place =>
                problem.Message.Contains(place, StringComparison.Ordinal)
                || problem.Reason.Contains(place, StringComparison.Ordinal)
                || problem.Fix.Contains(place, StringComparison.Ordinal));
        }

        private static DependencyAnswer Unreadable() => Failed(
            PackageProblemCodes.Unreadable,
            "The project's packages could not be worked out.",
            "the toolchain could not use what Packmoji told it",
            "try again; if it happens again, the reason is in the toolchain's log");

        private PackageProblem OutsideTheScopes(LockedPackage package) => new(
            PackageProblem.Error,
            PackageProblemCodes.Scope,
            $"The package \"{package.Name}\" is not one this toolchain builds.",
            $"it builds packages of these scopes: {_scopes}",
            "depend on a package of one of those scopes, or use a toolchain of your own");

        private static DependencyAnswer Failed(string code, string message, string reason, string fix) =>
            new DependencyAnswer.Failed([new PackageProblem(PackageProblem.Error, code, message, reason, fix)]);

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

        private void DeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not delete {Path}", path);
            }
        }

        private sealed class BuiltSet(string folder, string? packagesDirectory, IReadOnlyList<string> archives, IReadOnlyList<string> linkLibraries, IReadOnlyList<string> bareNames)
        {
            public string Folder { get; } = folder;

            public string? PackagesDirectory { get; } = packagesDirectory;

            public IReadOnlyList<string> Archives { get; } = archives;

            public IReadOnlyList<string> LinkLibraries { get; } = linkLibraries;

            public IReadOnlyList<string> BareNames { get; } = bareNames;

            public long LastUse { get; set; }

            public int Leases { get; set; }
        }
    }
}
