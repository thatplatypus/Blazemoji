using Blazemoji.Services.Packages;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Packmoji;

namespace Blazemoji.Shared.State
{
    public enum PackagesStatus
    {
        /// <summary>The project has no <c>packmoji.json</c>, and nothing is asked of anyone.</summary>
        None,

        /// <summary>This copy of Blazemoji has no pmj. The project is compiled as if it had no packages.</summary>
        Unavailable,

        /// <summary>The packages are being fetched, chosen or built.</summary>
        Restoring,

        /// <summary>What the lockfile locks is built, or it locks nothing.</summary>
        Ready,

        /// <summary>The lockfile is missing or no longer answers the manifest. Running the project locks it.</summary>
        OutOfDate,

        Failed,
    }

    /// <summary>
    /// Owns the state of the open project's packages. A project is a Packmoji project when it
    /// has a <c>packmoji.json</c>. What its <c>packmoji.lock</c> locks is fetched here and
    /// built by the toolchain when the project is opened and whenever either file changes,
    /// with no click and with nothing written to the project. Versions are chosen, and the
    /// lockfile written, only when someone runs the project: choosing asks GitHub and
    /// rewrites a file, and a manifest being typed is half written most of the time.
    /// </summary>
    public sealed class PackagesState(
        IPackageManager manager,
        IToolchainPackages toolchain,
        ProjectState projects,
        PackageUploads uploads,
        TimeProvider clock,
        ILogger<PackagesState> logger) : IPackageSets, IAsyncDisposable
    {
        public const string ManifestPath = "packmoji.json";
        public const string LockfilePath = "packmoji.lock";

        /// <summary>How long to leave a toolchain that said to ask again before asking.</summary>
        public static readonly TimeSpan AskAgainAfter = TimeSpan.FromSeconds(2);

        /// <summary>
        /// How long one set is waited for in all. A build has five minutes at the toolchain
        /// and may have to wait its turn behind another, so this is twice that.
        /// </summary>
        public static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(10);

        // The toolchain says which archives it lacks, is handed them, and is asked again. A
        // second round is for a set dropped in between. A third is a toolchain that is not
        // keeping what it is given, and asking for ever would not mend it.
        private const int MostRoundsOfArchives = 2;

        // A run waits for the project as it is open. Someone may change one of the two files
        // while it waits, which starts a newer restore, and that is waited for in turn: a few
        // times, and not for as long as someone goes on typing.
        private const int MostTurnsOfWaiting = 3;

        private const string Who = "Blazemoji";

        private readonly CancellationTokenSource _disposal = new();

        // The pair of texts the present status is about, and whether that status is the
        // answer for the pair until one of the two changes. Both are set with the status, in
        // the one place it is set, so that neither outlives it: a status about other text, or
        // one that is only how things stand for the moment, is never taken for the answer to
        // what is open. A pair that was answered is not asked about again, so a check of the
        // typing costs no call to pmj.
        private Pair? _about;
        private bool _remembered;

        // The work going on for a pair, if any is: a restore, or a run's resolve with the
        // restore that follows it. It carries the number of its ask, and only the latest
        // ask's work may be joined: an older one's answer will be thrown away. A restore is
        // about two texts, and any project that has them may join it. A resolve writes into
        // one project, which it names here, and only that project's asks join it.
        private (string? ProjectId, Pair Pair, int Ask, Task Work)? _running;

        // Each ask takes the next number, and only the latest may say how things stand: an
        // older one that ends later is about text that is no longer the project's.
        private int _asks;

        // What stops the latest ask's work. An ask that a newer one has taken over from has
        // nobody left to answer to, and is stopped: its pmj command holds one of the few
        // places the host has for everyone, and its wait on a toolchain that is building asks
        // again every two seconds for ten minutes. Typing in a manifest starts an ask at
        // every pause, so without this nothing bounds how much one session has going.
        private CancellationTokenSource? _stopsTheLatest;

        public PackagesStatus Status { get; private set; } = PackagesStatus.None;

        public IReadOnlyList<PackageProblem> Problems { get; private set; } = [];

        /// <summary>
        /// What a compile names the project's set of packages by. Null when there is no set:
        /// no manifest, nothing locked, or nothing ready.
        /// </summary>
        public string? Dependencies { get; private set; }

        /// <summary>
        /// True when <see cref="Problems"/> stopped a run, false when they were found while
        /// someone was typing. A run that could not start is worth interrupting someone for.
        /// </summary>
        public bool ProblemsAreFromARun { get; private set; }

        /// <summary>
        /// Whether a check of the typing is worth making. While packages are missing it would
        /// only mark every import as not found. With no pmj it is made as it always was.
        /// </summary>
        public bool AllowsACheck => Status is PackagesStatus.None or PackagesStatus.Ready or PackagesStatus.Unavailable;

        public event Action? StateChanged;

        /// <summary>
        /// Has what the project's lockfile locks fetched and built, unless that was done for
        /// these same two texts already. It never chooses a version and never writes to the
        /// project. It ends when the packages are ready or are known not to be.
        /// </summary>
        public Task EnsureRestoredAsync(Project project)
        {
            if (_disposal.IsCancellationRequested)
                return Task.CompletedTask;

            if (PairOf(project) is not { } pair)
            {
                // Whatever is still being worked out is for a project that is not this one.
                _asks++;
                _running = null;
                Apply(PackagesStatus.None, [], null, fromARun: false, about: null, remembered: true);
                var left = _stopsTheLatest;
                _stopsTheLatest = null;
                Stop(left);
                return Task.CompletedTask;
            }

            if (_remembered && pair == _about)
                return Task.CompletedTask;

            if (_running is { } running
                && running.Ask == _asks
                && running.Pair == pair
                && (running.ProjectId is null || running.ProjectId == project.Id))
            {
                return running.Work;
            }

            var (ask, stop, overtaken) = NextAsk();
            var work = RestoreAsync(pair, ask, stop);

            // A restore that ended before it had to wait for anything is not going on.
            _running = work.IsCompleted ? null : (null, pair, ask, work);
            Stop(overtaken);
            return work;
        }

        /// <summary>
        /// Makes the project's packages ready for a run. This is the one place versions are
        /// chosen: when the lockfile is missing or out of date it is written, into the
        /// project, and then restored.
        /// </summary>
        /// <returns>
        /// Whether the run may go ahead. True only when the status is the answer for the
        /// project as it is open. False when the packages are not ready, whose problems are
        /// then <see cref="Problems"/>, and when another project was opened or the two files
        /// went on changing while this waited.
        /// </returns>
        public async Task<bool> PrepareForRunAsync(Project project)
        {
            // Run is the one thing someone can press. What failed before may have been the
            // network, so it is tried again for the same two texts.
            if (Status == PackagesStatus.Failed)
                _remembered = false;

            // What is waited for is the project as it is open now, not as it was when Run was
            // pressed. A restore that a newer one overtook ends with nothing to say, and the
            // newer one is then the one to wait for.
            var settled = false;
            for (var turn = 0; turn < MostTurnsOfWaiting && !settled; turn++)
            {
                if (projects.Current.Id != project.Id)
                    return false;

                await EnsureRestoredAsync(projects.Current);
                settled = SaysHowThingsStandFor(project.Id);
            }

            if (settled && Status == PackagesStatus.OutOfDate && _about is { } pair)
            {
                // The resolve is the work going on for this pair, as a restore is. A typing
                // pause that asks about the same two texts meanwhile joins it. It must not
                // start a restore of its own: that would be a newer ask, the resolve's answer
                // would be thrown away, and the run would be left with nothing.
                var (ask, stop, overtaken) = NextAsk();
                var resolving = ResolveAsync(project.Id, pair, ask, stop);

                // Unless it is already over, or has already moved on to the restore that
                // follows it, which is a newer ask and has said so for itself.
                if (!resolving.IsCompleted && _asks == ask)
                    _running = (project.Id, pair, ask, resolving);

                Stop(overtaken);
                await resolving;
                settled = SaysHowThingsStandFor(project.Id);
            }

            // Another project was opened, or the two files went on changing: there is nothing
            // to say about a run of what was asked for.
            if (!settled)
                return false;

            if (Status is PackagesStatus.None or PackagesStatus.Ready or PackagesStatus.Unavailable)
                return true;

            Apply(Status, Problems, Dependencies, fromARun: true, _about, _remembered);
            return false;
        }

        public async Task<string?> RestoreAgainAsync(string lost, CancellationToken cancellationToken = default)
        {
            // The compile that asks was sent a while ago. Only when what is open is still
            // what was built into the set it named is there anything to give it: another
            // project's set would have its files compiled against packages that are not theirs.
            var pair = PairOf(projects.Current);
            if (pair is null || pair != _about || Dependencies != lost)
                return null;

            _remembered = false;
            await EnsureRestoredAsync(projects.Current).WaitAsync(cancellationToken);
            return Status == PackagesStatus.Ready && pair == _about && PairOf(projects.Current) == pair ? Dependencies : null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposal.IsCancellationRequested)
                return;

            await _disposal.CancelAsync();
        }

        private static Pair? PairOf(Project project) =>
            project.Find(ManifestPath) is { } manifest ? new Pair(manifest.Content, project.Find(LockfilePath)?.Content) : null;

        /// <summary>
        /// Whether the present status is the answer for this project as it is open now: it is
        /// the project that is open, its two texts are the ones the status is about, and
        /// nothing is still being worked out.
        /// </summary>
        private bool SaysHowThingsStandFor(string projectId) =>
            projects.Current.Id == projectId && PairOf(projects.Current) == _about && Status != PackagesStatus.Restoring;

        /// <summary>
        /// Takes the next number for an ask, and gives it a token that is cancelled when a
        /// later ask takes its place or the session goes away.
        /// </summary>
        /// <returns>
        /// The number, the token, and what stops the ask before this one, which the caller
        /// hands to <see cref="Stop"/> as the last thing it does.
        /// </returns>
        private (int Ask, CancellationToken Stop, CancellationTokenSource? Overtaken) NextAsk()
        {
            var overtaken = _stopsTheLatest;
            _stopsTheLatest = CancellationTokenSource.CreateLinkedTokenSource(_disposal.Token);
            return (++_asks, _stopsTheLatest.Token, overtaken);
        }

        /// <summary>
        /// Stops the work of an ask that a newer one has taken over from. Whatever was
        /// waiting on that work goes on there and then, inside this call, and may ask again
        /// itself: so this is called last, when the newer ask is in place for it to find.
        /// </summary>
        private static void Stop(CancellationTokenSource? overtaken)
        {
            overtaken?.Cancel();
            overtaken?.Dispose();
        }

        private async Task RestoreAsync(Pair pair, int ask, CancellationToken stop)
        {
            Apply(PackagesStatus.Restoring, [], null, fromARun: false, pair, remembered: false);
            try
            {
                var outcome = await WorkOutAsync(pair, stop);
                if (ask != _asks)
                    return;

                Apply(outcome.Status, outcome.Problems, outcome.Dependencies, fromARun: false, pair, outcome.Remember);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // A newer ask has taken over, or the session is going away. Either way there
                // is nothing to say.
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The project's packages could not be restored");
                if (ask == _asks)
                    Apply(PackagesStatus.Failed, [PmjProblems.Unreadable(Who)], null, fromARun: false, pair, remembered: false);
            }
            finally
            {
                if (_running is { } running && running.Ask == ask)
                    _running = null;
            }
        }

        private async Task<Outcome> WorkOutAsync(Pair pair, CancellationToken cancellationToken)
        {
            switch (await manager.RestoreAsync(pair.Manifest, pair.Lockfile, cancellationToken))
            {
                case PackageRestore.OutOfDate:
                    return new Outcome(PackagesStatus.OutOfDate, [NotLocked()], null, Remember: true);

                case PackageRestore.Failed failed:
                    return Failed(failed.Problems);

                // A lockfile that locks nothing needs no set. The toolchain is not asked, and
                // the project compiles as one without packages does.
                case PackageRestore.Restored { Packages.Count: 0 }:
                    return new Outcome(PackagesStatus.Ready, [], null, Remember: true);

                default:
                    return await BuildAsync(pair, cancellationToken);
            }
        }

        /// <summary>Asks the toolchain for the set, hands it what it lacks, and waits while it builds.</summary>
        private async Task<Outcome> BuildAsync(Pair pair, CancellationToken cancellationToken)
        {
            var request = new DependencyRequest(pair.Manifest, pair.Lockfile ?? string.Empty);
            var started = clock.GetTimestamp();
            var rounds = 0;
            while (true)
            {
                switch (await toolchain.PrepareAsync(request, cancellationToken))
                {
                    case DependencyAnswer.Ready ready:
                        return new Outcome(PackagesStatus.Ready, [], ready.Id, Remember: true);

                    case DependencyAnswer.Failed failed:
                        return Failed(failed.Problems);

                    case DependencyAnswer.Unavailable:
                        return new Outcome(PackagesStatus.Failed, [NoToolchain()], null, Remember: false);

                    case DependencyAnswer.Needed needed:
                        if (++rounds > MostRoundsOfArchives)
                        {
                            logger.LogError("The toolchain asked a third time for archives it had been handed");
                            return new Outcome(PackagesStatus.Failed, [PmjProblems.Unreadable(Who)], null, Remember: false);
                        }

                        if (await HandOverAsync(needed.Archives, started, cancellationToken) is { } refused)
                            return refused;

                        break;

                    // Busy: too much is being built, or this set's build is not done yet. It
                    // goes on without anyone waiting on the line, and the next ask joins it.
                    default:
                        if (clock.GetElapsedTime(started) > LongestWait)
                            return new Outcome(PackagesStatus.Failed, [PmjProblems.TimedOut()], null, Remember: false);

                        await Task.Delay(AskAgainAfter, clock, cancellationToken);
                        break;
                }
            }
        }

        /// <returns>Null when every archive was taken. Otherwise how things stand instead.</returns>
        private async Task<Outcome?> HandOverAsync(IReadOnlyList<LockedPackage> archives, long started, CancellationToken cancellationToken)
        {
            foreach (var package in archives)
            {
                while (!uploads.TryBegin())
                {
                    if (clock.GetElapsedTime(started) > LongestWait)
                        return new Outcome(PackagesStatus.Failed, [PmjProblems.TimedOut()], null, Remember: false);

                    await Task.Delay(AskAgainAfter, clock, cancellationToken);
                }

                ArchiveOutcome taken;
                try
                {
                    await using var archive = manager.OpenArchive(package);
                    if (archive is null)
                    {
                        logger.LogError("The archive of {Package} {Version} was restored and is not in the cache", package.Name, package.Version);
                        return new Outcome(PackagesStatus.Failed, [NotHandedOver(package)], null, Remember: false);
                    }

                    taken = await toolchain.AddArchiveAsync(package, archive, cancellationToken);
                }
                finally
                {
                    uploads.End();
                }

                if (taken != ArchiveOutcome.Stored)
                {
                    logger.LogWarning("The toolchain did not take the archive of {Package} {Version}: {Outcome}", package.Name, package.Version, taken);

                    // A package it will not build, or one too large for it, is the same answer
                    // however often it is sent. Anything else may go better next time.
                    return new Outcome(PackagesStatus.Failed, [NotTaken(package, taken)], null, Remember: taken is ArchiveOutcome.NotAllowed or ArchiveOutcome.TooLarge);
                }
            }

            return null;
        }

        /// <summary>
        /// Chooses versions, writes the lockfile into the project it was chosen for, and
        /// restores what is then open.
        /// </summary>
        /// <param name="ask">This resolve's number among the asks, taken by whoever started it.</param>
        /// <param name="stop">Cancelled when a newer ask takes over from this one.</param>
        private async Task ResolveAsync(string projectId, Pair pair, int ask, CancellationToken stop)
        {
            Apply(PackagesStatus.Restoring, [], null, fromARun: false, pair, remembered: false);
            try
            {
                if (!await ChooseAsync(projectId, pair, ask, stop))
                    return;
            }
            finally
            {
                // The choosing is over, however it went. What follows is a restore, which is
                // the work going on in its own right and must not find this one in its way:
                // it would join it, and wait on the very thing that is waiting for it.
                if (_running is { } running && running.Ask == ask)
                    _running = null;
            }

            // Whatever is open now is restored, whether or not anything was written. The
            // status has been "restoring" since this began, and must not be left so.
            await EnsureRestoredAsync(projects.Current);
        }

        /// <returns>
        /// True when versions were chosen and what is open is now to be restored. False when
        /// there is nothing to restore: choosing failed, and the status says so, or a newer
        /// ask has taken over.
        /// </returns>
        private async Task<bool> ChooseAsync(string projectId, Pair pair, int ask, CancellationToken stop)
        {
            PackageResolve? resolved;
            try
            {
                resolved = await manager.ResolveAsync(pair.Manifest, pair.Lockfile, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The project's packages could not be resolved");
                if (ask == _asks)
                    Apply(PackagesStatus.Failed, [PmjProblems.Unreadable(Who)], null, fromARun: true, pair, remembered: false);

                return false;
            }

            // A newer ask has taken over since, and how things stand is its to say.
            if (ask != _asks)
                return false;

            if (resolved is not PackageResolve.Resolved chosen)
            {
                var outcome = Failed(resolved is PackageResolve.Failed failed ? failed.Problems : [PmjProblems.Unreadable(Who)]);
                Apply(outcome.Status, outcome.Problems, null, fromARun: true, pair, outcome.Remember);
                return false;
            }

            // The lockfile was chosen for this project as these two texts. It is written only
            // where that is still what is open: someone may have opened another project, or
            // changed the manifest, while pmj was choosing.
            var open = projects.Current;
            if (open.Id == projectId && PairOf(open) == pair)
                await projects.WriteFileAsync(projectId, LockfilePath, chosen.Lockfile);

            return true;
        }

        private static Outcome Failed(IReadOnlyList<PackageProblem> problems)
        {
            if (problems.Any(problem => problem.Code == PackageProblemCodes.NoPackmoji))
                return new Outcome(PackagesStatus.Unavailable, problems, null, Remember: true);

            // A problem with the two files is the same until they change. One about this
            // moment (too much going on, out of time, GitHub out of reach, an answer nobody
            // could read) is worth asking about again for the same files.
            var aboutTheMoment = problems.Any(problem =>
                problem.Code is PackageProblemCodes.Busy or PackageProblemCodes.TimedOut or PackageProblemCodes.Unreadable or PackageProblemCodes.Toolchain
                || problem.Code.StartsWith("github.", StringComparison.Ordinal));
            return new Outcome(PackagesStatus.Failed, problems, null, Remember: !aboutTheMoment);
        }

        /// <param name="about">The pair of texts this status is about. Null for a project with no manifest.</param>
        /// <param name="remembered">Whether this is the answer for that pair until one of the two texts changes.</param>
        private void Apply(PackagesStatus status, IReadOnlyList<PackageProblem> problems, string? dependencies, bool fromARun, Pair? about, bool remembered)
        {
            if (_disposal.IsCancellationRequested)
                return;

            _about = about;
            _remembered = remembered;

            if (Status == status && Dependencies == dependencies && ProblemsAreFromARun == fromARun && Problems.SequenceEqual(problems))
                return;

            Status = status;
            Problems = problems;
            Dependencies = dependencies;
            ProblemsAreFromARun = fromARun;
            StateChanged?.Invoke();
        }

        private static PackageProblem NotLocked() => new(
            PackageProblem.Error,
            PmjProblems.LockOutOfDate,
            "The project's packages are not locked.",
            "packmoji.lock is missing, or no longer answers what packmoji.json asks for",
            "run the project: its packages are chosen then, and packmoji.lock is written");

        private static PackageProblem NoToolchain() => new(
            PackageProblem.Error,
            PackageProblemCodes.Toolchain,
            "The toolchain service could not be reached.",
            "the project's packages are built there",
            "check that it is running, and try again");

        private static PackageProblem NotHandedOver(LockedPackage package) => new(
            PackageProblem.Error,
            PackageProblemCodes.Unreadable,
            $"The package \"{package.Name}\" could not be handed to the toolchain.",
            "it is not where Packmoji keeps what it has fetched",
            "try again; if it happens again, the reason is in Blazemoji's log");

        private static PackageProblem NotTaken(LockedPackage package, ArchiveOutcome outcome) => outcome switch
        {
            ArchiveOutcome.NotAllowed => new(
                PackageProblem.Error,
                PackageProblemCodes.Scope,
                $"The package \"{package.Name}\" is not one the toolchain builds.",
                "it is outside the scopes the toolchain is limited to",
                "depend only on packages the toolchain builds, or use a toolchain of your own"),
            ArchiveOutcome.TooLarge => new(
                PackageProblem.Error,
                PackageProblemCodes.TooLarge,
                $"The package \"{package.Name}\" is too large for the toolchain.",
                "an archive may be 16 MiB",
                "depend on a smaller package"),
            ArchiveOutcome.Unavailable => NoToolchain(),
            _ => new(
                PackageProblem.Error,
                PackageProblemCodes.Unreadable,
                $"The toolchain did not take the package \"{package.Name}\".",
                "what it was handed was not what it expected",
                "try again; if it happens again, the reason is in Blazemoji's log"),
        };

        private sealed record Pair(string Manifest, string? Lockfile);

        /// <param name="Remember">Whether this is the answer for these two texts until one of them changes.</param>
        private sealed record Outcome(PackagesStatus Status, IReadOnlyList<PackageProblem> Problems, string? Dependencies, bool Remember);
    }
}
