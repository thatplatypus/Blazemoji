namespace Blazemoji.Toolchain.Local
{
    /// <summary>
    /// Lends built sets to compiles.
    /// </summary>
    public interface IDependencySetStore
    {
        /// <returns>The set, held until the lease is disposed, or null when it is not held here.</returns>
        DependencySetLease? Lease(string id);
    }

    /// <summary>
    /// A built set for the length of one compile. While it is held the set is not dropped for
    /// room, so the compiler and the linker never look for a file that has just gone.
    /// </summary>
    /// <param name="packagesDirectory">The folder the compiler is told to search. Null for a set of no packages.</param>
    /// <param name="archives">Every package's archive, for the linker.</param>
    /// <param name="linkLibraries">The libraries the packages say to link with, each once.</param>
    public sealed class DependencySetLease(
        string? packagesDirectory,
        IReadOnlyList<string> archives,
        IReadOnlyList<string> linkLibraries,
        Action? release = null) : IDisposable
    {
        private Action? _release = release;

        public string? PackagesDirectory { get; } = packagesDirectory;

        public IReadOnlyList<string> Archives { get; } = archives;

        public IReadOnlyList<string> LinkLibraries { get; } = linkLibraries;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
