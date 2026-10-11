namespace Blazemoji.Toolchain.Packmoji
{
    /// <summary>
    /// The shape of what a lockfile names: a package, a version, a digest, a library to link.
    /// Each ends up in a path or on a command line, and each comes from a file a visitor can
    /// write, so none is used that has not passed here. pmj has stricter rules of its own and
    /// is the judge of a package; these only keep a path inside the folder it belongs in.
    /// </summary>
    public static class PackageNames
    {
        private const int MaxScopeLength = 39;
        private const int MaxNameLength = 64;
        private const int MaxVersionLength = 64;
        private const int MaxLibraryLength = 64;
        private const int DigestLength = 64;

        /// <summary>Splits <c>@scope/name</c>. False, with both parts empty, when it is not written that way.</summary>
        public static bool TryParse(string? fullName, out string scope, out string name)
        {
            scope = string.Empty;
            name = string.Empty;

            if (fullName is null || fullName.Length < 4 || fullName[0] != '@')
                return false;

            var slash = fullName.IndexOf('/');
            if (slash < 0 || fullName.IndexOf('/', slash + 1) >= 0)
                return false;

            var scopePart = fullName[1..slash];
            var namePart = fullName[(slash + 1)..];
            if (!IsScope(scopePart) || !IsBareName(namePart))
                return false;

            scope = scopePart;
            name = namePart;
            return true;
        }

        public static bool IsScope(string? scope) =>
            scope is { Length: > 0 and <= MaxScopeLength }
            && scope[0] != '-'
            && scope.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

        public static bool IsBareName(string? name) =>
            name is { Length: > 0 and <= MaxNameLength }
            && name[0] is >= 'a' and <= 'z'
            && name.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

        public static bool IsVersion(string? version) =>
            version is { Length: > 0 and <= MaxVersionLength }
            && version[0] is >= '0' and <= '9'
            && version.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-');

        public static bool IsDigest(string? sha256) =>
            sha256 is { Length: DigestLength }
            && sha256.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

        /// <summary>A library the linker is told of with <c>-l</c>, as a manifest's <c>native.link</c> may name one.</summary>
        public static bool IsLinkLibrary(string? library) =>
            library is { Length: > 0 and <= MaxLibraryLength }
            && (char.IsAsciiLetterOrDigit(library[0]) || library[0] == '_')
            && library.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '+' or '.' or '-');
    }
}
