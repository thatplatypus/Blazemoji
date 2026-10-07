using System.Text.Json;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Toolchain;
using Microsoft.Extensions.Options;

namespace Blazemoji.Services.Projects
{
    public sealed class ProjectTemplateOptions
    {
        public const string SectionName = "ProjectTemplates";

        /// <summary>
        /// One folder per template, each holding a <c>template.json</c> and the template's files.
        /// </summary>
        public string Path { get; set; } = System.IO.Path.Combine(AppContext.BaseDirectory, "Emojicode", "Templates");
    }

    /// <summary>
    /// Project templates read from folders on disk, once. A template is data, so adding one
    /// needs no code: the Grapevine sample is put there by a script and is offered when present.
    /// </summary>
    public sealed class FileProjectTemplates(IOptions<ProjectTemplateOptions> options, ILogger<FileProjectTemplates> logger) : IProjectTemplates
    {
        private const string DescriptionFile = "template.json";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private static readonly ProjectTemplate BuiltIn = new(
            "hello-world",
            "Hello World",
            "One file that prints a greeting.",
            ProjectKind.Program,
            "main.🍇",
            [new ProjectFile("main.🍇", "🏁 🍇\n\t😀 🔤Hello World!🔤❗️\n🍉\n")]);

        private readonly Lazy<IReadOnlyList<ProjectTemplate>> _all = new(() => Read(options.Value.Path, logger));

        public IReadOnlyList<ProjectTemplate> All => _all.Value;

        private static IReadOnlyList<ProjectTemplate> Read(string root, ILogger logger)
        {
            var templates = new List<(int Order, ProjectTemplate Template)>();

            if (Directory.Exists(root))
            {
                foreach (var folder in Directory.GetDirectories(root))
                {
                    if (ReadTemplate(folder, logger) is { } template)
                        templates.Add(template);
                }
            }

            if (templates.Count == 0)
            {
                logger.LogWarning("No project templates were found in {Path}", root);
                return [BuiltIn];
            }

            return templates
                .OrderBy(entry => entry.Order)
                .ThenBy(entry => entry.Template.Name, StringComparer.Ordinal)
                .Select(entry => entry.Template)
                .ToList();
        }

        private static (int Order, ProjectTemplate Template)? ReadTemplate(string folder, ILogger logger)
        {
            var descriptionPath = Path.Combine(folder, DescriptionFile);
            if (!File.Exists(descriptionPath))
                return null;

            TemplateFile? description;
            try
            {
                description = JsonSerializer.Deserialize<TemplateFile>(File.ReadAllText(descriptionPath), Json);
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                logger.LogWarning(exception, "The template in {Folder} could not be read", folder);
                return null;
            }

            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(path => (Full: path, Relative: Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/')))
                .Where(file => file.Relative != DescriptionFile && SourceFileNames.IsSafe(file.Relative) && !file.Relative.Split('/').Any(segment => segment.StartsWith('.')))
                .Select(file => new ProjectFile(file.Relative, File.ReadAllText(file.Full).TrimStart('﻿')))
                .ToList();

            if (string.IsNullOrWhiteSpace(description?.Name) || description.Entry is null || files.All(file => file.Path != description.Entry))
            {
                logger.LogWarning("The template in {Folder} has no name, or its entry is not among its files", folder);
                return null;
            }

            var kind = string.Equals(description.Kind, "server", StringComparison.OrdinalIgnoreCase) ? ProjectKind.Server : ProjectKind.Program;
            var template = new ProjectTemplate(Path.GetFileName(folder), description.Name, description.Description ?? string.Empty, kind, description.Entry, files);
            return (description.Order ?? int.MaxValue, template);
        }

        private sealed record TemplateFile(string? Name, string? Description, string? Kind, string? Entry, int? Order);
    }
}
