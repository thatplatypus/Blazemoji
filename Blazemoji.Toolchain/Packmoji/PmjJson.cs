using System.Text.Json;

namespace Blazemoji.Toolchain.Packmoji
{
    /// <param name="Ok">False when any problem is an error.</param>
    public sealed record PmjReport(bool Ok, IReadOnlyList<PackageProblem> Problems);

    public sealed record PmjTree(PmjReport Report, IReadOnlyList<LockedPackage> Packages);

    /// <param name="BareName">The name the package is imported by, which is the name of its folder under <c>packages</c>.</param>
    /// <param name="Link">The libraries a program that uses the package is linked with.</param>
    public sealed record BuiltPackage(string Name, string Version, string BareName, IReadOnlyList<string> Link);

    public sealed record PmjBuild(PmjReport Report, IReadOnlyList<BuiltPackage> Packages);

    /// <summary>
    /// Reads the one JSON object pmj puts on standard output when it is given <c>--json</c>.
    /// The text is another program's, so a part that is missing or of the wrong kind is read
    /// as empty, and text that is not that object at all is read as nothing.
    /// </summary>
    public static class PmjJson
    {
        public static PmjTree? ReadTree(string stdout) =>
            Read(stdout, (report, root) => new PmjTree(
                report,
                Objects(root, "packages")
                    .Select(package => new LockedPackage(Text(package, "name"), Text(package, "version"), Text(package, "sha256")))
                    .ToList()));

        public static PmjBuild? ReadBuild(string stdout) =>
            Read(stdout, (report, root) => new PmjBuild(
                report,
                Objects(root, "packages")
                    .Select(package => new BuiltPackage(
                        Text(package, "name"),
                        Text(package, "version"),
                        Text(package, "bareName"),
                        Items(package, "link").Where(library => library.ValueKind == JsonValueKind.String).Select(library => library.GetString()!).ToList()))
                    .ToList()));

        private static T? Read<T>(string stdout, Func<PmjReport, JsonElement, T> read)
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

                var problems = Objects(root, "diagnostics").Select(Problem).ToList();
                return read(new PmjReport(ok.GetBoolean(), problems), root);
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
