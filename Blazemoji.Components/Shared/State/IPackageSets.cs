namespace Blazemoji.Shared.State
{
    /// <summary>
    /// What a compile asks when the toolchain says it no longer holds the project's set of
    /// packages, as after it was restarted.
    /// </summary>
    public interface IPackageSets
    {
        /// <summary>
        /// Has the set a compile named built again, though the project's two files have not
        /// changed.
        /// </summary>
        /// <param name="lost">The set the compile named, which the toolchain said it does not hold.</param>
        /// <returns>
        /// The set's id as the toolchain now gives it. Null when the packages could not be
        /// made ready, whose problem is then the packages' own to show, and when the project
        /// that is open is not built into the set the compile named: another project was
        /// opened, or its two files changed, while the compile was with the toolchain.
        /// </returns>
        Task<string?> RestoreAgainAsync(string lost, CancellationToken cancellationToken = default);
    }
}
