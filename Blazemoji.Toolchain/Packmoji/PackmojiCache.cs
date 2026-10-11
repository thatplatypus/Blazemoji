namespace Blazemoji.Toolchain.Packmoji
{
    /// <summary>
    /// Where pmj keeps an archive, as its own guide to the command line gives it:
    /// <c>&lt;home&gt;/cache/&lt;scope&gt;/&lt;name&gt;/&lt;version&gt;/&lt;sha256&gt;.pmj.tar.gz</c>.
    /// An archive put there by another program is one pmj uses, and holds to its digest.
    /// </summary>
    public static class PackmojiCache
    {
        /// <returns>Null when any part of the package is not well formed.</returns>
        public static string? ArchivePath(string home, LockedPackage package)
        {
            if (!package.IsWellFormed || !PackageNames.TryParse(package.Name, out var scope, out var name))
                return null;

            return Path.Combine(home, "cache", scope, name, package.Version, package.Sha256 + ".pmj.tar.gz");
        }
    }
}
