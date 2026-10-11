namespace Blazemoji.Toolchain.Packmoji
{
    /// <summary>One package a lockfile locks.</summary>
    /// <param name="Name">The full name, <c>@scope/name</c>.</param>
    /// <param name="Sha256">The digest of the package's archive, in lowercase hexadecimal.</param>
    public sealed record LockedPackage(string Name, string Version, string Sha256)
    {
        public bool IsWellFormed =>
            PackageNames.TryParse(Name, out _, out _) && PackageNames.IsVersion(Version) && PackageNames.IsDigest(Sha256);
    }
}
