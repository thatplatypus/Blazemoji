using Blazemoji.Shared.Models.Projects;

namespace Blazemoji.Services.Projects
{
    public interface IProjectTemplates
    {
        /// <summary>In the order they should be offered. Never empty.</summary>
        IReadOnlyList<ProjectTemplate> All { get; }
    }
}
