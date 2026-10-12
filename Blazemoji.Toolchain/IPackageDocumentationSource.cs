namespace Blazemoji.Toolchain
{
    /// <summary>
    /// Where the compiler's documentation reports for packages come from.
    /// </summary>
    public interface IPackageDocumentationSource
    {
        /// <param name="package">The name the package is imported by.</param>
        /// <param name="dependencies">
        /// The set of packages the project compiles against, as the toolchain named it, or
        /// null for a project with none of its own. With a set, the set's own package of
        /// that name is looked for first and a stock one second.
        /// </param>
        /// <returns>
        /// The report for a package as JSON, exactly as <c>emojicodec -r</c> wrote it, or null
        /// when there is none or it could not be fetched.
        /// </returns>
        Task<string?> GetAsync(string package, string? dependencies = null, CancellationToken cancellationToken = default);
    }
}
