using Blazemoji.Services.Packages;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Shared.State;
using Blazemoji.Toolchain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Blazemoji.Test.State
{
    /// <summary>What a test needs of the packages' state when packages are not what it is about.</summary>
    internal static class TestPackages
    {
        /// <summary>A project state with one template and nothing saved, as a page has before it has loaded.</summary>
        public static ProjectState Projects()
        {
            var templates = Substitute.For<IProjectTemplates>();
            templates.All.Returns([new ProjectTemplate("hello-world", "Hello World", "One file.", ProjectKind.Program, "main.🍇", [new ProjectFile("main.🍇", "🏁 🍇 🍉")])]);
            return new ProjectState(Substitute.For<IProjectStore>(), templates, NullLogger<ProjectState>.Instance);
        }

        public static PackagesState State(
            IPackageManager? manager = null,
            IToolchainPackages? toolchain = null,
            ProjectState? projects = null,
            TimeProvider? clock = null) =>
            new(
                manager ?? Substitute.For<IPackageManager>(),
                toolchain ?? Substitute.For<IToolchainPackages>(),
                projects ?? Projects(),
                new PackageUploads(),
                clock ?? TimeProvider.System,
                NullLogger<PackagesState>.Instance);
    }
}
