using Blazemoji.Shared.Models.Library;
using Microsoft.Extensions.Options;

namespace Blazemoji.Services.Library
{
    public sealed class SampleOptions
    {
        public const string SectionName = "Samples";

        /// <summary>A folder of sample programs, one file each.</summary>
        public string Path { get; set; } = System.IO.Path.Combine(AppContext.BaseDirectory, "Emojicode", "Samples");
    }

    /// <summary>The sample programs offered in the Library tab.</summary>
    public interface ISamples
    {
        /// <summary>Every sample, in the order of their names.</summary>
        Task<IReadOnlyList<EmojicFile>> AllAsync();
    }

    /// <summary>
    /// Sample programs read from a folder on disk. A sample is data, so adding one needs no code.
    /// </summary>
    public sealed class FileSamples(IOptions<SampleOptions> options, ILogger<FileSamples> logger) : ISamples
    {
        public async Task<IReadOnlyList<EmojicFile>> AllAsync()
        {
            var root = options.Value.Path;
            if (!Directory.Exists(root))
            {
                logger.LogWarning("No sample programs were found in {Path}", root);
                return [];
            }

            var samples = new List<EmojicFile>();
            foreach (var path in Directory.EnumerateFiles(root).Order(StringComparer.Ordinal))
            {
                var code = await File.ReadAllTextAsync(path);
                samples.Add(new EmojicFile { Name = Path.GetFileName(path), Code = code.TrimStart('﻿') });
            }

            return samples;
        }
    }
}
