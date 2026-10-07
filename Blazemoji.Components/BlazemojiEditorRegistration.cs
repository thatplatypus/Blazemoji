using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Interop;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Blazemoji
{
    public static class BlazemojiEditorRegistration
    {
        /// <summary>
        /// Registers what the editor's components need that does not depend on the host: the
        /// keyword catalog, the code intelligence, the project templates and the state.
        /// The host adds the rest: MudBlazor, the toolchain client, and its own
        /// <see cref="IProjectStore"/> and <c>ILibraryService</c>. docs/hosting.md has the list.
        /// </summary>
        public static IServiceCollection AddBlazemojiEditor(this IServiceCollection services)
        {
            foreach (var keyword in EmojicodeCatalog.All())
                services.AddSingleton(keyword);

            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IEmojiNames, EmojiNames>();
            services.AddSingleton<ICodeIntelligence, CodeIntelligence>();
            services.AddSingleton<IProjectTemplates, FileProjectTemplates>();

            // One of each per session: per circuit on a server, per window on a desktop.
            services.AddScoped<IPackageLibrary, PackageLibrary>();
            services.AddScoped<EmojicodeLanguageInterop>();
            services.AddScoped<RunState>();
            services.AddScoped<ProjectState>();
            services.AddScoped<RequestState>();
            services.AddScoped<LocalStorageFiles>();

            return services;
        }
    }
}
