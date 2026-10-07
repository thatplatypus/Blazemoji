using Microsoft.Extensions.Options;

namespace Blazemoji.Toolchain.Service
{
    /// <summary>
    /// The packages a build may name, and the compiler's documentation report for each.
    /// </summary>
    public sealed class PackageCatalog(IOptions<ToolchainServiceOptions> options)
    {
        private const string DocumentationFileName = "documentation.json";

        public IReadOnlyList<string> Names()
        {
            var root = options.Value.PackageDocumentationPath;
            if (!Directory.Exists(root))
                return [];

            return Directory.GetDirectories(root)
                .Where(directory => File.Exists(Path.Combine(directory, DocumentationFileName)))
                .Select(directory => Path.GetFileName(directory)!)
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        /// <returns>Null for a package that is not in the catalog. The name is only ever compared, never used to build a path.</returns>
        public string? DocumentationPath(string name) =>
            Names().Contains(name, StringComparer.Ordinal)
                ? Path.Combine(options.Value.PackageDocumentationPath, name, DocumentationFileName)
                : null;
    }
}
