using Blazemoji.Toolchain.Packmoji;

namespace Blazemoji.Toolchain
{
    /// <summary>
    /// The packages a project locks, as the toolchain sees them. A set is the packages one
    /// lockfile locks: their names, versions and digests. It is built once, from archives the
    /// caller hands over, and every project that locks the same packages uses the same one.
    /// </summary>
    public interface IToolchainPackages
    {
        /// <summary>
        /// Asks for the set a manifest and a lockfile name. The answer is the archives the
        /// toolchain still lacks, or the set built and ready, or what is wrong. It waits for a
        /// build; someone who stops waiting does not stop the build.
        /// </summary>
        Task<DependencyAnswer> PrepareAsync(DependencyRequest request, CancellationToken cancellationToken = default);

        /// <summary>Hands over one archive. It is kept only if its bytes are the digest it came under.</summary>
        Task<ArchiveOutcome> AddArchiveAsync(LockedPackage package, Stream content, CancellationToken cancellationToken = default);

        /// <returns>
        /// The compiler's documentation report for one package of a set, as JSON, or null when
        /// the set is not held or has no such package.
        /// </returns>
        /// <param name="package">The name the package is imported by.</param>
        Task<string?> GetDocumentationAsync(string dependencies, string package, CancellationToken cancellationToken = default);
    }

    /// <param name="Manifest">The text of <c>packmoji.json</c>.</param>
    /// <param name="Lockfile">The text of <c>packmoji.lock</c>.</param>
    public sealed record DependencyRequest(string Manifest, string Lockfile);

    public abstract record DependencyAnswer
    {
        private DependencyAnswer()
        {
        }

        /// <summary>Nothing was built. These archives are to be handed over, and the question asked again.</summary>
        public sealed record Needed(IReadOnlyList<LockedPackage> Archives) : DependencyAnswer;

        /// <param name="Id">What a compile names the set by. It means nothing to a caller.</param>
        /// <param name="Packages">The names the set's packages are imported by, in order.</param>
        public sealed record Ready(string Id, IReadOnlyList<string> Packages) : DependencyAnswer;

        public sealed record Failed(IReadOnlyList<PackageProblem> Problems) : DependencyAnswer;

        /// <summary>Ask again shortly: too much is being built, or a build has not ended in the time one answer may take.</summary>
        public sealed record Busy : DependencyAnswer;

        /// <summary>The toolchain could not be reached, or answered with something that could not be read.</summary>
        public sealed record Unavailable : DependencyAnswer;
    }

    public enum ArchiveOutcome
    {
        Stored,

        /// <summary>The bytes are not the digest they came under.</summary>
        WrongDigest,

        /// <summary>The package's name, version or digest is not well formed.</summary>
        Malformed,

        /// <summary>The package is outside the scopes the toolchain builds.</summary>
        NotAllowed,

        TooLarge,

        /// <summary>The toolchain could not keep it.</summary>
        Failed,

        /// <summary>The toolchain could not be reached.</summary>
        Unavailable,
    }
}
