using System.Text;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.Extensions.Options;

namespace Blazemoji.Services.Packages
{
    /// <summary>
    /// The package manager that runs pmj. Each call gives pmj a folder of its own, holding
    /// the project's two files and nothing else, and removes it afterwards. pmj is never run
    /// in a project's real folder: the editor would not see what pmj wrote there until the
    /// project was opened again, and its next save would take those files for someone
    /// else's changes.
    /// </summary>
    public sealed class PmjPackageManager(IPmj pmj, IOptions<PackmojiOptions> options, ILogger<PmjPackageManager> logger) : IPackageManager
    {
        public const string ManifestFile = "packmoji.json";
        public const string LockfileFile = "packmoji.lock";

        // Packmoji's own limits on its two files. pmj refuses a larger one anyway; refusing
        // it here saves writing it to disk first.
        private const int MaxManifestBytes = 1024 * 1024;
        private const int MaxLockfileBytes = 4 * 1024 * 1024;

        private const string Who = "Blazemoji";

        private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly PackageScopes _scopes = PackageScopes.Parse(options.Value.Scopes);
        private readonly int _mostAtOnce = Math.Max(1, options.Value.MaxAtOnce);
        private readonly string _home = HomeOf(options.Value);
        private readonly string _work = Path.Combine(Path.GetTempPath(), "blazemoji-packages");
        private int _running;

        public async Task<PackageRestore> RestoreAsync(string manifest, string? lockfile, CancellationToken cancellationToken = default)
        {
            var (installed, problems) = await InstallAsync(manifest, lockfile, locked: true, cancellationToken);
            if (installed is not null)
                return new PackageRestore.Restored(installed.Packages);

            return problems.Any(problem => problem.Code == PmjProblems.LockOutOfDate)
                ? new PackageRestore.OutOfDate()
                : new PackageRestore.Failed(problems);
        }

        public async Task<PackageResolve> ResolveAsync(string manifest, string? lockfile, CancellationToken cancellationToken = default)
        {
            var (installed, problems) = await InstallAsync(manifest, lockfile, locked: false, cancellationToken);
            return installed is { Lockfile: { } written }
                ? new PackageResolve.Resolved(written, installed.Packages)
                : new PackageResolve.Failed(problems);
        }

        public Stream? OpenArchive(LockedPackage package)
        {
            if (!_scopes.Allows(package.Name) || PackmojiCache.ArchivePath(_home, package) is not { } path)
                return null;

            try
            {
                return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "The archive of {Package} {Version} could not be opened", package.Name, package.Version);
                return null;
            }
        }

        /// <param name="locked">Install exactly what the lockfile holds, and choose nothing.</param>
        /// <returns>What was installed, or null and what to say instead.</returns>
        private async Task<(Installed? Installed, IReadOnlyList<PackageProblem> Problems)> InstallAsync(
            string manifest, string? lockfile, bool locked, CancellationToken cancellationToken)
        {
            if (_scopes.AllowsNothing)
                return (null, [NoScopes()]);

            if (Encoding.UTF8.GetByteCount(manifest) > MaxManifestBytes || (lockfile is not null && Encoding.UTF8.GetByteCount(lockfile) > MaxLockfileBytes))
                return (null, [TooLarge()]);

            // Refused, not queued: someone waiting behind a slow download would be waiting
            // with nothing to look at, and is better told to try again.
            if (Interlocked.Increment(ref _running) > _mostAtOnce)
            {
                Interlocked.Decrement(ref _running);
                return (null, [Busy()]);
            }

            var folder = Path.Combine(_work, Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(Path.Combine(folder, ManifestFile), manifest, Utf8WithoutBom, cancellationToken);
                if (lockfile is not null)
                    await File.WriteAllTextAsync(Path.Combine(folder, LockfileFile), lockfile, Utf8WithoutBom, cancellationToken);

                string[] arguments = locked ? ["install", "--locked", "--json"] : ["install", "--json"];
                var result = await pmj.RunAsync(new PmjCommand(folder, arguments, Environment: Settings()), cancellationToken);
                if (result.Outcome == PmjOutcome.NotFound)
                    return (null, [PmjProblems.NoPackmoji("This copy of Blazemoji")]);
                if (result.Outcome == PmjOutcome.TimedOut)
                    return (null, [PmjProblems.TimedOut()]);

                if (PmjJson.ReadInstall(result.Stdout) is not { } install)
                {
                    logger.LogError("pmj install printed what could not be read. Status {ExitCode}. stderr: {Stderr}", result.ExitCode, result.Stderr);
                    return (null, [PmjProblems.Unreadable(Who)]);
                }

                if (!install.Report.Ok)
                    return (null, ForAVisitor(install.Report.Problems, folder));

                if (install.Packages.Any(package => !package.IsWellFormed))
                {
                    logger.LogError("pmj install listed a package whose name, version or digest is not well formed");
                    return (null, [PmjProblems.Unreadable(Who)]);
                }

                // pmj was told the scopes and has refused what is outside them. This is the
                // same question asked again here, of pmj's own answer, so that this host's
                // list does not rest on one program's reading of one variable.
                var outside = install.Packages.Where(package => !_scopes.Allows(package.Name)).ToList();
                if (outside.Count > 0)
                    return (null, outside.Select(OutsideTheScopes).ToList());

                if (locked)
                    return (new Installed(install.Packages, null), []);

                if (await ReadLockfileAsync(folder, cancellationToken) is not { } written)
                {
                    logger.LogError("pmj install said it resolved, and left no lockfile that can be used");
                    return (null, [PmjProblems.Unreadable(Who)]);
                }

                return (new Installed(install.Packages, written), []);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogError(exception, "The folder pmj is run in could not be used under {Work}", _work);
                return (null, [PmjProblems.Unreadable(Who)]);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
                Delete(folder);
            }
        }

        private Dictionary<string, string> Settings()
        {
            // Always told its folder, whatever this process's own environment says, so that
            // an archive pmj fetched is where OpenArchive looks for it.
            var settings = new Dictionary<string, string> { [PmjVariables.Home] = _home };
            if (_scopes.ForPmj is { } scopes)
                settings[PmjVariables.Scopes] = scopes;

            return settings;
        }

        private IReadOnlyList<PackageProblem> ForAVisitor(IReadOnlyList<PackageProblem> reported, string folder)
        {
            string[] places = [folder, _work, _home];
            var shown = new List<PackageProblem>();
            var withheldError = false;
            foreach (var problem in reported)
            {
                if (PmjProblems.ForAVisitor(problem, places, asksGitHub: true) is not { } passedOn)
                {
                    logger.LogError("pmj reported {Code}: {Message} ({Reason})", problem.Code, problem.Message, problem.Reason);
                    withheldError |= problem.IsError;
                    continue;
                }

                if (passedOn.Reason != problem.Reason)
                    logger.LogWarning("pmj reported {Code}: {Message} ({Reason})", problem.Code, problem.Message, problem.Reason);

                shown.Add(passedOn);
            }

            return withheldError || !shown.Any(problem => problem.IsError) ? [PmjProblems.Unreadable(Who)] : shown;
        }

        private static async Task<string?> ReadLockfileAsync(string folder, CancellationToken cancellationToken)
        {
            var file = new FileInfo(Path.Combine(folder, LockfileFile));
            if (!file.Exists || file.Length > MaxLockfileBytes)
                return null;

            return await File.ReadAllTextAsync(file.FullName, Encoding.UTF8, cancellationToken);
        }

        private void Delete(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "The folder pmj was run in could not be removed: {Folder}", folder);
            }
        }

        /// <summary>
        /// pmj's folder: the one this host was told, or the one pmj itself would use. A
        /// desktop then shares its cache with pmj on the command line.
        /// </summary>
        private static string HomeOf(PackmojiOptions options)
        {
            if (options.Home is { Length: > 0 } told)
                return told;

            if (Environment.GetEnvironmentVariable(PmjVariables.Home) is { Length: > 0 } ambient)
                return ambient;

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".packmoji");
        }

        private static PackageProblem NoScopes() => new(
            PackageProblem.Error,
            PackageProblemCodes.Scope,
            "This copy of Blazemoji takes no one's packages.",
            "it has been told no scopes to take packages of",
            "whoever runs it names the scopes it takes in Packmoji__Scopes, or * for anyone's");

        private PackageProblem OutsideTheScopes(LockedPackage package) => new(
            PackageProblem.Error,
            PackageProblemCodes.Scope,
            $"The package \"{package.Name}\" is not one this copy of Blazemoji takes.",
            $"it takes packages of these scopes: {_scopes}",
            "depend only on packages of those scopes");

        private static PackageProblem TooLarge() => new(
            PackageProblem.Error,
            PackageProblemCodes.TooLarge,
            "The project's package files are too large.",
            "a manifest is at most 1 MiB and a lockfile at most 4 MiB",
            "check that packmoji.json and packmoji.lock are the files pmj wrote");

        private static PackageProblem Busy() => new(
            PackageProblem.Error,
            PackageProblemCodes.Busy,
            "Too many projects are fetching packages at once.",
            "only so many are fetched at a time",
            "try again in a moment");

        private sealed record Installed(IReadOnlyList<LockedPackage> Packages, string? Lockfile);
    }
}
