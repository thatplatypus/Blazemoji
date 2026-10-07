using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Interop;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.State;
using Microsoft.Extensions.Configuration;
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
        /// <param name="configuration">
        /// Where <see cref="ProjectTemplateOptions"/> is read from, under
        /// <see cref="ProjectTemplateOptions.SectionName"/>. Without it the defaults apply.
        /// </param>
        /// <remarks>
        /// Anything the host registered before this call is kept. That is how a host supplies
        /// its own templates, or makes the state classes singletons where there is one user.
        /// </remarks>
        public static IServiceCollection AddBlazemojiEditor(this IServiceCollection services, IConfiguration? configuration = null)
        {
            services.AddOptions();
            services.AddLogging();
            if (configuration is not null)
                services.Configure<ProjectTemplateOptions>(configuration.GetSection(ProjectTemplateOptions.SectionName));

            foreach (var keyword in EmojicodeCatalog.All())
                services.AddSingleton(keyword);

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IEmojiNames, EmojiNames>();
            services.TryAddSingleton<ICodeIntelligence, CodeIntelligence>();
            services.TryAddSingleton<IProjectTemplates, FileProjectTemplates>();

            // One of each per session: per circuit on a server, per window on a desktop.
            services.TryAddScoped<IPackageLibrary, PackageLibrary>();
            services.TryAddScoped<EmojicodeLanguageInterop>();
            services.TryAddScoped<ClipboardInterop>();
            services.TryAddScoped<RunState>();
            services.TryAddScoped<ProjectState>();
            services.TryAddScoped<RequestState>();
            services.TryAddScoped<LocalStorageFiles>();

            return services;
        }
    }
}
