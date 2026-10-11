namespace Blazemoji.Toolchain.Packmoji
{
    /// <summary>
    /// The scopes whose packages are fetched and built. A scope is a GitHub owner, and pmj
    /// takes a package only from a repository its scope owns, so this is a list of whose
    /// releases a server will download and compile for someone it does not know.
    /// </summary>
    public sealed class PackageScopes
    {
        public const string Any = "*";

        private readonly HashSet<string>? _allowed;

        private PackageScopes(HashSet<string>? allowed) => _allowed = allowed;

        /// <param name="scopes">Scopes separated by commas, or <see cref="Any"/>. Null or blank takes <paramref name="fallback"/>.</param>
        /// <remarks>An entry that is not a scope is dropped, so a list of nothing usable allows nothing.</remarks>
        public static PackageScopes Parse(string? scopes, string fallback)
        {
            var text = string.IsNullOrWhiteSpace(scopes) ? fallback : scopes;
            var entries = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (entries.Contains(Any))
                return new PackageScopes(null);

            return new PackageScopes(entries.Where(entry => PackageNames.IsScope(entry)).ToHashSet(StringComparer.Ordinal));
        }

        public bool Allows(string? fullName) =>
            PackageNames.TryParse(fullName, out var scope, out _) && (_allowed is null || _allowed.Contains(scope));

        public override string ToString() =>
            _allowed is null ? Any : string.Join(", ", _allowed.Order(StringComparer.Ordinal));
    }
}
