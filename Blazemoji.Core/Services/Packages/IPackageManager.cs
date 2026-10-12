using Blazemoji.Toolchain.Packmoji;

namespace Blazemoji.Services.Packages
{
    /// <summary>
    /// Fetches the packages a project depends on. A project is a Packmoji project when it has
    /// a manifest, <c>packmoji.json</c>, and its lockfile, <c>packmoji.lock</c>, says exactly
    /// which packages it uses. This works on the two texts and never on a project's own
    /// folder, so it serves a project kept in a browser as it serves one kept on a disk.
    /// </summary>
    /// <remarks>
    /// Every way of not getting an answer is an answer. Only the caller's own cancellation throws.
    /// </remarks>
    public interface IPackageManager
    {
        /// <summary>
        /// Fetches what the lockfile locks, and chooses nothing. With every archive already
        /// fetched this asks nothing of anyone.
        /// </summary>
        Task<PackageRestore> RestoreAsync(string manifest, string? lockfile, CancellationToken cancellationToken = default);

        /// <summary>
        /// Chooses versions for what the manifest asks for, fetches them, and gives the
        /// lockfile that records the choice. This asks GitHub, so it is for when someone
        /// runs the project and not for while they type.
        /// </summary>
        /// <param name="lockfile">The lockfile there is, which keeps what it already locks where it still can. Null when there is none.</param>
        Task<PackageResolve> ResolveAsync(string manifest, string? lockfile, CancellationToken cancellationToken = default);

        /// <summary>
        /// An archive that a restore or a resolve fetched, to hand to the toolchain.
        /// </summary>
        /// <returns>The archive, which the caller disposes. Null when it is not there to be read.</returns>
        Stream? OpenArchive(LockedPackage package);
    }

    public abstract record PackageRestore
    {
        private PackageRestore()
        {
        }

        /// <param name="Packages">Every package the lockfile locks. Empty when it locks none.</param>
        public sealed record Restored(IReadOnlyList<LockedPackage> Packages) : PackageRestore;

        /// <summary>The lockfile is missing, or no longer answers the manifest. Resolving writes one that does.</summary>
        public sealed record OutOfDate : PackageRestore;

        /// <param name="Problems">
        /// At least one. One with the code <see cref="PackageProblemCodes.NoPackmoji"/> says
        /// there is no pmj here at all; <see cref="PackageProblemCodes.Busy"/> and
        /// <see cref="PackageProblemCodes.TimedOut"/> say to ask again.
        /// </param>
        public sealed record Failed(IReadOnlyList<PackageProblem> Problems) : PackageRestore;
    }

    public abstract record PackageResolve
    {
        private PackageResolve()
        {
        }

        /// <param name="Lockfile">The text of the lockfile to keep in the project.</param>
        public sealed record Resolved(string Lockfile, IReadOnlyList<LockedPackage> Packages) : PackageResolve;

        /// <param name="Problems">At least one, as for <see cref="PackageRestore.Failed"/>.</param>
        public sealed record Failed(IReadOnlyList<PackageProblem> Problems) : PackageResolve;
    }
}
