using System.Collections.Concurrent;
using Blazemoji.Toolchain;

namespace Blazemoji.Emojicode.Intelligence
{
    public interface IPackageLibrary
    {
        /// <summary>
        /// The documentation of each named package that has any, in the order asked for.
        /// </summary>
        Task<IReadOnlyList<PackageDocumentation>> GetAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Package documentation, fetched once per package and kept. A package that could not be
    /// fetched is asked for again next time, since the usual reason is that the toolchain
    /// service was not up yet.
    /// </summary>
    public sealed class PackageLibrary(IPackageDocumentationSource source) : IPackageLibrary
    {
        private readonly ConcurrentDictionary<string, PackageDocumentation> _read = new();

        public async Task<IReadOnlyList<PackageDocumentation>> GetAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default)
        {
            var found = new List<PackageDocumentation>();
            foreach (var package in packages.Distinct())
            {
                if (!_read.TryGetValue(package, out var documentation))
                {
                    var json = await source.GetAsync(package, cancellationToken);
                    documentation = json is null ? null : PackageDocumentationReader.Read(package, json);
                    if (documentation is not null)
                        _read[package] = documentation;
                }

                if (documentation is not null)
                    found.Add(documentation);
            }

            return found;
        }
    }
}
