namespace Blazemoji.Shared.State
{
    /// <summary>
    /// What a compile asks when the toolchain says it no longer holds the project's set of
    /// packages, as after it was restarted.
    /// </summary>
    public interface IPackageSets
    {
        /// <summary>
        /// Has the open project's set built again, though its two files have not changed.
        /// </summary>
        /// <returns>The set's id, or null when the packages could not be made ready. What went wrong is then the packages' own problem to show.</returns>
        Task<string?> RestoreAgainAsync(CancellationToken cancellationToken = default);
    }
}
