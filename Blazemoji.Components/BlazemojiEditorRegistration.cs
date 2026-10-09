using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Interop;
using Blazemoji.Services.Layout;
using Blazemoji.Services.Library;
using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
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
        /// keyword catalog, the code intelligence, the project templates, the sample programs
        /// and the state.
        /// The host adds the rest: MudBlazor, the toolchain client, and its own
        /// <see cref="IProjectStore"/> and <c>ILibraryService</c>. docs/hosting.md has the list.
        /// </summary>
        /// <param name="configuration">
        /// Where <see cref="ProjectTemplateOptions"/> and <see cref="SampleOptions"/> are read
        /// from, each under its own <c>SectionName</c>. Without it the defaults apply.
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
            {
                services.Configure<ProjectTemplateOptions>(configuration.GetSection(ProjectTemplateOptions.SectionName));
                services.Configure<SampleOptions>(configuration.GetSection(SampleOptions.SectionName));
            }

            foreach (var keyword in EmojicodeCatalog.All())
                services.AddSingleton(keyword);

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IEmojiNames, EmojiNames>();
            services.TryAddSingleton<ICodeIntelligence, CodeIntelligence>();
            services.TryAddSingleton<IProjectTemplates, FileProjectTemplates>();
            services.TryAddSingleton<ISamples, FileSamples>();

            // One of each per session: per circuit on a server, per window on a desktop.
            services.TryAddScoped<IPackageLibrary, PackageLibrary>();
            services.TryAddScoped<EmojicodeLanguageInterop>();
            services.TryAddScoped<ClipboardInterop>();
            services.TryAddScoped<SplitViewInterop>();
            services.TryAddScoped<ProgramInputInterop>();
            services.TryAddScoped<RunState>();
            services.TryAddScoped<ProjectState>();
            services.TryAddScoped<RequestState>();
            services.TryAddScoped<LayoutState>();
            services.TryAddScoped<LocalStorageFiles>();

            return services;
        }

        /// <summary>
        /// For a host with a disk of its own: projects as folders, and what is saved from the
        /// editor as files beside them, with the layout and the settings in a folder of the app's own beside them,
        /// under <see cref="FileProjectStoreOptions.Root"/>. One
        /// of each for the life of the app, since each remembers what it last read and wrote.
        /// </summary>
        /// <param name="configuration">
        /// Where <see cref="FileProjectStoreOptions"/> is read from, under
        /// <see cref="FileProjectStoreOptions.SectionName"/>. Without it the defaults apply.
        /// </param>
        public static IServiceCollection AddBlazemojiProjectsOnDisk(this IServiceCollection services, IConfiguration? configuration = null)
        {
            services.AddOptions();
            if (configuration is not null)
                services.Configure<FileProjectStoreOptions>(configuration.GetSection(FileProjectStoreOptions.SectionName));

            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IProjectStore, FileProjectStore>();
            services.AddSingleton<ILibraryService, FileLibraryService>();
            services.AddSingleton<ILayoutStore, FileLayoutStore>();
            services.AddSingleton<ISettingsStore, FileSettingsStore>();

            return services;
        }
    }
}
