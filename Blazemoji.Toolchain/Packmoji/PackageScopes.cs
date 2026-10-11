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

        /// <param name="scopes">Scopes separated by commas, or <see cref="Any"/>.</param>
        /// <remarks>
        /// Nothing said allows nothing, and an entry that is not a scope is dropped. There is no
        /// default to fall back on, because the only safe one is none: a setting that was
        /// forgotten or mistyped on a public server then builds no one's packages, not everyone's.
        /// </remarks>
        public static PackageScopes Parse(string? scopes)
        {
            var entries = (scopes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (entries.Contains(Any))
                return new PackageScopes(null);

            return new PackageScopes(entries.Where(entry => PackageNames.IsScope(entry)).ToHashSet(StringComparer.Ordinal));
        }

        public bool AllowsNothing => _allowed is { Count: 0 };

        /// <summary>
        /// The list as pmj reads it in its own variable, or null when pmj is to be told
        /// nothing. pmj has no word for any scope: it means that by the variable not being
        /// set. And it takes a variable set to nothing for a mistake, so a list of no scopes
        /// is never given to it: whoever holds one refuses before pmj is run.
        /// </summary>
        public string? ForPmj => _allowed is { Count: > 0 } ? string.Join(',', _allowed.Order(StringComparer.Ordinal)) : null;

        public bool Allows(string? fullName) =>
            PackageNames.TryParse(fullName, out var scope, out _) && (_allowed is null || _allowed.Contains(scope));

        public override string ToString() =>
            _allowed is null ? Any : string.Join(", ", _allowed.Order(StringComparer.Ordinal));
    }
}
