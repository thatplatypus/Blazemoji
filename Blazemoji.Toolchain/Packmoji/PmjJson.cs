using System.Text.Json;

namespace Blazemoji.Toolchain.Packmoji
{
    /// <param name="Ok">What pmj said (<c>ok</c>), which pmj sets false when any problem is an error. It is pmj's word, not worked out here.</param>
    public sealed record PmjReport(bool Ok, IReadOnlyList<PackageProblem> Problems);

    public sealed record PmjTree(PmjReport Report, IReadOnlyList<LockedPackage> Packages);

    /// <param name="BareName">The name the package is imported by, which is the name of its folder under <c>packages</c>.</param>
    /// <param name="Link">The libraries a program that uses the package is linked with.</param>
    public sealed record BuiltPackage(string Name, string Version, string BareName, IReadOnlyList<string> Link);

    public sealed record PmjBuild(PmjReport Report, IReadOnlyList<BuiltPackage> Packages);

    /// <summary>What <c>pmj install --json</c> answers, and <c>add</c>, <c>remove</c> and <c>update</c> with it.</summary>
    /// <param name="Written">
    /// Whether pmj wrote the project's files. False when the lockfile already answered the
    /// manifest, and when pmj could not do what it was asked.
    /// </param>
    /// <param name="Packages">Every package the lockfile holds now. Empty when pmj could not do what it was asked.</param>
    public sealed record PmjInstall(PmjReport Report, bool Written, IReadOnlyList<LockedPackage> Packages);

    /// <summary>
    /// Reads the one JSON object pmj puts on standard output when it is given <c>--json</c>.
    /// The text is another program's. A problem with a part missing or of the wrong kind is
    /// read as empty there, but a package pmj could not have printed makes the text not pmj's,
    /// and text that is not that object at all is read as nothing.
    /// </summary>
    public static class PmjJson
    {
        public static PmjTree? ReadTree(string stdout) =>
            Read(stdout, (root, report, packages) => Locked(packages) is { } locked ? new PmjTree(report, locked) : null);

        public static PmjInstall? ReadInstall(string stdout) =>
            Read(stdout, (root, report, packages) => Locked(packages) is { } locked
                ? new PmjInstall(report, root.TryGetProperty("written", out var written) && written.ValueKind == JsonValueKind.True, locked)
                : null);

        public static PmjBuild? ReadBuild(string stdout) =>
            Read(stdout, (root, report, packages) =>
            {
                var built = new List<BuiltPackage>();
                foreach (var package in packages)
                {
                    var (name, version, bareName, link) = (Required(package, "name"), Required(package, "version"), Required(package, "bareName"), Link(package));
                    if (name is null || version is null || bareName is null || link is null)
                        return null;
                    built.Add(new BuiltPackage(name, version, bareName, link));
                }

                return new PmjBuild(report, built);
            });

        /// <summary>The packages as a lockfile names them, or null when one lacks what names it.</summary>
        private static List<LockedPackage>? Locked(IReadOnlyList<JsonElement> packages)
        {
            var locked = new List<LockedPackage>();
            foreach (var package in packages)
            {
                var (name, version, sha256) = (Required(package, "name"), Required(package, "version"), Required(package, "sha256"));
                if (name is null || version is null || sha256 is null)
                    return null;
                locked.Add(new LockedPackage(name, version, sha256));
            }

            return locked;
        }

        private static T? Read<T>(string stdout, Func<JsonElement, PmjReport, IReadOnlyList<JsonElement>, T?> read)
            where T : class
        {
            try
            {
                using var document = JsonDocument.Parse(stdout);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("ok", out var ok)
                    || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return null;
                }

                var packages = Items(root, "packages").ToList();
                if (packages.Any(package => package.ValueKind != JsonValueKind.Object))
                    return null;

                var problems = Objects(root, "diagnostics").Select(Problem).ToList();
                return read(root, new PmjReport(ok.GetBoolean(), problems), packages);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
            {
                // Not JSON, or JSON whose strings cannot be decoded: an escaped lone surrogate
                // passes the parser and fails when the string is asked for.
                return null;
            }
        }

        private static PackageProblem Problem(JsonElement diagnostic)
        {
            var located = diagnostic.TryGetProperty("location", out var location) && location.ValueKind == JsonValueKind.Object;
            var file = located ? Text(location, "file") : string.Empty;

            return new PackageProblem(
                Text(diagnostic, "severity") == PackageProblem.Warning ? PackageProblem.Warning : PackageProblem.Error,
                Text(diagnostic, "code"),
                Text(diagnostic, "message"),
                Text(diagnostic, "reason"),
                Text(diagnostic, "fix"),
                file.Length > 0 ? file : null,
                located ? Number(location, "line") : 0,
                located ? Number(location, "column") : 0);
        }

        private static string? Required(JsonElement package, string name)
        {
            var text = Text(package, name);
            return text.Length > 0 ? text : null;
        }

        /// <summary>The libraries to link, or null when <c>link</c> is there and is not an array of text.</summary>
        private static List<string>? Link(JsonElement package)
        {
            if (!package.TryGetProperty("link", out var link))
                return [];

            if (link.ValueKind != JsonValueKind.Array || link.EnumerateArray().Any(library => library.ValueKind != JsonValueKind.String))
                return null;

            return link.EnumerateArray().Select(library => library.GetString()!).ToList();
        }

        private static IEnumerable<JsonElement> Objects(JsonElement parent, string name) =>
            Items(parent, name).Where(item => item.ValueKind == JsonValueKind.Object);

        private static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

        private static string Text(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

        private static int Number(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
    }
}
