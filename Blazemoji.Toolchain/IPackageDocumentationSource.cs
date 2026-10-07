namespace Blazemoji.Toolchain
{
    /// <summary>
    /// Where the compiler's documentation reports for packages come from.
    /// </summary>
    public interface IPackageDocumentationSource
    {
        /// <returns>
        /// The report for a package as JSON, exactly as <c>emojicodec -r</c> wrote it, or null
        /// when there is none or it could not be fetched.
        /// </returns>
        Task<string?> GetAsync(string package, CancellationToken cancellationToken = default);
    }
}
