namespace Blazemoji.Shared.Models.Projects
{
    public enum ProjectKind
    {
        /// <summary>Runs to the end and stops.</summary>
        Program,

        /// <summary>Listens for HTTP requests until it is stopped.</summary>
        Server,
    }

    /// <param name="Path">Relative, with forward slashes between folders: <c>app/main.🍇</c>.</param>
    public sealed record ProjectFile(string Path, string Content);

    /// <summary>
    /// A set of source files that are compiled together.
    /// </summary>
    /// <param name="Entry">The path of the file handed to the compiler. Always one of <paramref name="Files"/>.</param>
    public sealed record Project(string Id, string Name, ProjectKind Kind, string Entry, IReadOnlyList<ProjectFile> Files)
    {
        public ProjectFile? Find(string path) => Files.FirstOrDefault(file => file.Path == path);
    }

    public sealed record ProjectSummary(string Id, string Name);

    /// <summary>
    /// A starting point for a new project.
    /// </summary>
    public sealed record ProjectTemplate(string Id, string Name, string Description, ProjectKind Kind, string Entry, IReadOnlyList<ProjectFile> Files);
}
