using System.Collections.Concurrent;
using Blazemoji.Toolchain;

namespace Blazemoji.Emojicode.Intelligence
{
    public interface IPackageLibrary
    {
        /// <summary>
        /// The documentation of each named package that has any, in the order asked for.
        /// </summary>
        /// <param name="dependencies">
        /// The set of packages the project compiles against, or null for a project with none
        /// of its own. A locked package has documentation only under its set.
        /// </param>
        Task<IReadOnlyList<PackageDocumentation>> GetAsync(IEnumerable<string> packages, string? dependencies = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Package documentation, fetched once per package and kept. A package that could not be
    /// fetched is asked for again next time, since the usual reason is that the toolchain
    /// service was not up yet. What is kept is kept by set as well as by name: a lockfile
    /// that changes is another set, and its packages may be other versions.
    /// </summary>
    public sealed class PackageLibrary(IPackageDocumentationSource source) : IPackageLibrary
    {
        private readonly ConcurrentDictionary<(string Dependencies, string Package), PackageDocumentation> _read = new();

        public async Task<IReadOnlyList<PackageDocumentation>> GetAsync(IEnumerable<string> packages, string? dependencies = null, CancellationToken cancellationToken = default)
        {
            var found = new List<PackageDocumentation>();
            foreach (var package in packages.Distinct())
            {
                var key = (dependencies ?? string.Empty, package);
                if (!_read.TryGetValue(key, out var documentation))
                {
                    var json = await source.GetAsync(package, dependencies, cancellationToken);
                    documentation = json is null ? null : PackageDocumentationReader.Read(package, json);
                    if (documentation is not null)
                        _read[key] = documentation;
                }

                if (documentation is not null)
                    found.Add(documentation);
            }

            return found;
        }
    }
}
