using Blazemoji.Components;
using Blazemoji.Emojicode;
using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Interop;
using Blazemoji.Services.Library;
using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Shared.State;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Http;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using NSubstitute;

namespace Blazemoji.Test
{
    /// <summary>
    /// docs/hosting.md tells a host which registrations to make and which files to link.
    /// These read the document, make exactly the registrations it lists, and check that the
    /// editor then has every service it asks for and that every file it names exists. What
    /// they cannot check is the page shell itself: the order of the scripts and how Blazor
    /// is started are only checked by the browser tests against the web host.
    /// </summary>
    public class HostingTests : BunitContext
    {
        public HostingTests()
        {
            JSInterop.Mode = JSRuntimeMode.Loose;

            // What docs/hosting.md lists, in its order.
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            Services.AddToolchainClient(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ToolchainClient:BaseUrl"] = "http://toolchain.test:5290" })
                .Build());
            Services.AddBlazemojiEditor();
            Services.AddSingleton(Substitute.For<IProjectStore>());
            Services.AddSingleton(Substitute.For<ILibraryService>());
        }

        [Fact]
        public void The_registrations_a_host_is_told_to_make_give_the_editor_every_service_it_uses()
        {
            Services.GetRequiredService<RunState>().ShouldNotBeNull();
            Services.GetRequiredService<ProjectState>().ShouldNotBeNull();
            Services.GetRequiredService<RequestState>().ShouldNotBeNull();
            Services.GetRequiredService<LocalStorageFiles>().ShouldNotBeNull();
            Services.GetRequiredService<EmojicodeLanguageInterop>().ShouldNotBeNull();
            Services.GetRequiredService<ICodeIntelligence>().ShouldNotBeNull();
            Services.GetRequiredService<SettingsState>().ShouldNotBeNull();
            Services.GetRequiredService<IPackageLibrary>().ShouldNotBeNull();
            Services.GetRequiredService<IProjectTemplates>().All.ShouldNotBeEmpty();
            Services.GetRequiredService<ISamples>().ShouldBeOfType<FileSamples>();
            Services.GetRequiredService<IToolchain>().ShouldBeOfType<HttpToolchain>();
        }

        [Fact]
        public void The_keyword_catalog_is_registered_whole()
        {
            var keywords = Services.GetServices<EmojicodeKeyword>().ToList();

            keywords.Count.ShouldBeGreaterThan(30);
            keywords.Select(keyword => keyword.GetType()).ShouldBeUnique();
            keywords.ShouldContain(keyword => keyword.Emoji == Emojis.Grape);
        }

        [Fact]
        public async Task State_is_per_session_and_the_catalog_is_shared()
        {
            await using var one = Services.CreateAsyncScope();
            await using var two = Services.CreateAsyncScope();

            one.ServiceProvider.GetRequiredService<ProjectState>().ShouldNotBeSameAs(two.ServiceProvider.GetRequiredService<ProjectState>());
            one.ServiceProvider.GetRequiredService<RunState>().ShouldNotBeSameAs(two.ServiceProvider.GetRequiredService<RunState>());
            one.ServiceProvider.GetRequiredService<LocalStorageFiles>().ShouldNotBeSameAs(two.ServiceProvider.GetRequiredService<LocalStorageFiles>());
            one.ServiceProvider.GetRequiredService<SettingsState>().ShouldNotBeSameAs(two.ServiceProvider.GetRequiredService<SettingsState>());
            one.ServiceProvider.GetRequiredService<ICodeIntelligence>().ShouldBeSameAs(two.ServiceProvider.GetRequiredService<ICodeIntelligence>());
        }

        [Fact]
        public void The_workspace_renders_with_those_registrations_alone()
        {
            // Monaco is not here to make a model, so the one call that must return something does.
            JSInterop.Setup<BlazorMonaco.Editor.TextModel>("blazorMonaco.editor.createModel", _ => true)
                .SetResult(new BlazorMonaco.Editor.TextModel { Id = "model-1", Uri = "inmemory://blazemoji/1" });

            var cut = Render<Workspace>();

            cut.Find("[data-testid=run-button]").ShouldNotBeNull();
            cut.Find("[data-testid=open-file]").TextContent.ShouldContain("main.🍇");
            cut.FindAll("[data-testid=file]").ShouldNotBeEmpty();
        }

        [Fact]
        public async Task The_provider_module_is_loaded_from_the_librarys_own_static_files()
        {
            const string path = "./_content/Blazemoji.Components/js/emojicodeLanguage.js";
            JSInterop.Mode = JSRuntimeMode.Strict;
            var module = JSInterop.SetupModule(path);
            module.SetupModule("register", _ => true).SetupVoid("dispose").SetVoidResult();
            var interop = Services.GetRequiredService<EmojicodeLanguageInterop>();

            await interop.RegisterAsync("emojiscript");

            module.Invocations["register"].ShouldHaveSingleItem();
            File.Exists(InTheLibrary(path)).ShouldBeTrue(path);
        }

        private const string ClipboardScript = "./_content/Blazemoji.Components/js/clipboard.js";

        private (IRenderedComponent<CopyToClipboard> Button, BunitJSModuleInterop Script, Func<int> Copied) RenderCopyButton(bool theBrowserCopies)
        {
            JSInterop.Mode = JSRuntimeMode.Strict;
            var script = JSInterop.SetupModule(ClipboardScript);
            script.Setup<bool>("lastCopySucceeded").SetResult(theBrowserCopies);
            var copied = 0;
            var button = Render<CopyToClipboard>(parameters => parameters
                .Add(copy => copy.ClipboardValue, "🍇")
                .Add(copy => copy.CopiedToClipboard, () => copied++));
            return (button, script, () => copied);
        }

        [Fact]
        public void The_text_to_copy_is_on_the_page_for_a_script_of_the_librarys_own_to_copy_as_the_button_is_pressed()
        {
            // The browser only lets a page write to the clipboard while a press is being
            // handled. A press that goes to the server and comes back has stopped being one by
            // then, as far as Safari is concerned, so the copying is done in the browser.
            var (button, script, _) = RenderCopyButton(theBrowserCopies: true);

            button.Find("[data-copy]").GetAttribute("data-copy").ShouldBe("🍇");
            button.Find("[data-copy] button").ShouldNotBeNull();
            JSInterop.Invocations["import"].ShouldHaveSingleItem().Arguments[0].ShouldBe(ClipboardScript);
            script.Invocations.ShouldBeEmpty();
            File.Exists(InTheLibrary(ClipboardScript)).ShouldBeTrue(ClipboardScript);
        }

        [Fact]
        public async Task A_press_asks_the_browser_whether_it_copied_and_says_so_when_it_did()
        {
            var (button, script, copied) = RenderCopyButton(theBrowserCopies: true);

            await button.Find("button").ClickAsync();

            script.Invocations["lastCopySucceeded"].ShouldHaveSingleItem();
            copied().ShouldBe(1);
        }

        [Fact]
        public async Task A_press_the_browser_could_not_copy_for_is_not_reported_as_copied()
        {
            var (button, _, copied) = RenderCopyButton(theBrowserCopies: false);

            await button.Find("button").ClickAsync();

            copied().ShouldBe(0);
        }

        [Fact]
        public void Every_tab_renders_with_those_registrations_alone()
        {
            Services.GetRequiredService<ILibraryService>().GetSavedAsync().Returns([]);

            Should.NotThrow(() => Render<Library>());
            Should.NotThrow(() => Render<EmojiToolbox>());
            Should.NotThrow(() => Render<OutputPanel>());
            Should.NotThrow(() => Render<RequestPanel>());
            Render<EmojiToolbox>().FindComponents<CopyToClipboard>().ShouldNotBeEmpty();
        }

        [Fact]
        public void The_document_lists_exactly_the_registrations_these_tests_make()
        {
            var listed = Regex.Matches(FirstCodeBlock("csharp"), @"services\.(Add\w+(?:<[^>]+>)?)").Select(match => match.Groups[1].Value);

            listed.ShouldBe(
            [
                "AddMudServices",
                "AddToolchainClient",
                "AddBlazemojiEditor",
                "AddScoped<IProjectStore, YourProjectStore>",
                "AddTransient<ILibraryService, YourLibraryService>",
            ]);
        }

        [Fact]
        public void Every_file_of_the_library_that_the_document_names_exists()
        {
            var named = Regex.Matches(Document(), @"_content/Blazemoji\.Components/[\w./-]+\w").Select(match => match.Value).Distinct().ToList();

            named.ShouldContain("_content/Blazemoji.Components/blazemoji.css");
            named.ShouldAllBe(path => File.Exists(InTheLibrary(path)));
        }

        [Fact]
        public void The_document_says_where_templates_are_set_and_the_setting_is_read_from_there()
        {
            Document().ShouldContain($"\"{ProjectTemplateOptions.SectionName}\"");
            var services = new ServiceCollection();
            services.AddBlazemojiEditor(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [ProjectTemplateOptions.SectionName + ":Path"] = "/somewhere/else" })
                .Build());

            services.BuildServiceProvider().GetRequiredService<IOptions<ProjectTemplateOptions>>().Value.Path.ShouldBe("/somewhere/else");
        }

        [Fact]
        public void The_editors_own_call_brings_everything_its_own_services_need()
        {
            var services = new ServiceCollection();
            services.AddBlazemojiEditor();
            services.AddSingleton(Substitute.For<IProjectStore>());
            using var provider = services.BuildServiceProvider(validateScopes: true);
            using var scope = provider.CreateScope();

            provider.GetRequiredService<IProjectTemplates>().ShouldBeOfType<FileProjectTemplates>();
            provider.GetRequiredService<ICodeIntelligence>().ShouldNotBeNull();
            scope.ServiceProvider.GetRequiredService<ProjectState>().ShouldNotBeNull();
        }

        [Fact]
        public void A_host_that_keeps_projects_on_disk_gets_both_stores_from_one_call_and_their_folder_from_configuration()
        {
            Document().ShouldContain($"\"{FileProjectStoreOptions.SectionName}\"");
            var services = new ServiceCollection();
            services.AddBlazemojiProjectsOnDisk(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [FileProjectStoreOptions.SectionName + ":Root"] = "/somewhere/else" })
                .Build());
            services.AddBlazemojiEditor();
            using var provider = services.BuildServiceProvider(validateScopes: true);
            using var one = provider.CreateScope();
            using var two = provider.CreateScope();

            provider.GetRequiredService<IOptions<FileProjectStoreOptions>>().Value.Root.ShouldBe("/somewhere/else");
            one.ServiceProvider.GetRequiredService<IProjectStore>().ShouldBeOfType<FileProjectStore>();
            one.ServiceProvider.GetRequiredService<ILibraryService>().ShouldBeOfType<FileLibraryService>();
            one.ServiceProvider.GetRequiredService<IProjectStore>().ShouldBeSameAs(two.ServiceProvider.GetRequiredService<IProjectStore>());
            one.ServiceProvider.GetRequiredService<ProjectState>().ShouldNotBeNull();
            one.ServiceProvider.GetRequiredService<ISettingsStore>().ShouldBeOfType<FileSettingsStore>();
            one.ServiceProvider.GetRequiredService<ISettingsStore>().ShouldBeSameAs(two.ServiceProvider.GetRequiredService<ISettingsStore>());
        }

        [Fact]
        public void What_a_host_registers_before_the_editors_call_is_kept()
        {
            var templates = Substitute.For<IProjectTemplates>();
            templates.All.Returns([new ProjectTemplate("own", "Own", "The host's own.", ProjectKind.Program, "main.🍇", [new ProjectFile("main.🍇", "🏁 🍇 🍉")])]);
            var services = new ServiceCollection();
            services.AddSingleton(templates);
            services.AddSingleton(Substitute.For<IProjectStore>());
            services.AddSingleton<ProjectState>();
            services.AddBlazemojiEditor();
            using var provider = services.BuildServiceProvider();
            using var one = provider.CreateScope();
            using var two = provider.CreateScope();

            provider.GetRequiredService<IProjectTemplates>().ShouldBeSameAs(templates);
            one.ServiceProvider.GetRequiredService<ProjectState>().ShouldBeSameAs(two.ServiceProvider.GetRequiredService<ProjectState>());
            provider.GetServices<EmojicodeKeyword>().Count().ShouldBeGreaterThan(30);
        }

        [Fact]
        public void The_document_says_what_a_host_does_for_settings_and_uses_the_word_in_one_sense()
        {
            var document = Document();

            document.ShouldContain("`ISettingsStore`");
            document.ShouldContain("`LocalStorageSettingsStore`");
            document.ShouldContain("`FileSettingsStore`");
            document.ShouldContain("settings.json");
            document.ShouldContain("SettingHosts.Desktop");
            document.ShouldContain("`SettingsState`");
            document.ShouldContain("## Adding a setting");
            document.ShouldNotContain("## The one setting");
        }

        [Fact]
        public void The_library_styles_what_it_draws_without_bootstrap()
        {
            var markup = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "Blazemoji.Components"), "*.razor", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .Select(File.ReadAllText)
                .ToList();
            var styles = File.ReadAllText(Path.Combine(RepositoryRoot(), "Blazemoji.Components", "wwwroot", "blazemoji.css"));
            var classes = markup
                .SelectMany(text => Regex.Matches(text, "[Cc]lass=\"([^\"@]*)\"").SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                .ToHashSet();

            // Bootstrap's spacing is p-2 and m-3; MudBlazor's, which the library has, is pa-2 and ma-3.
            classes.Where(name => Regex.IsMatch(name, "^[pm]-(\\d+|auto)$")).ShouldBeEmpty();
            foreach (var bootstraps in new[] { "text-nowrap", "text-md-center" }.Where(classes.Contains))
                styles.ShouldContain("." + bootstraps);
        }

        private static string Document() => File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "hosting.md"));

        private static string FirstCodeBlock(string language)
        {
            var document = Document();
            var start = document.IndexOf("```" + language, StringComparison.Ordinal);
            start.ShouldBeGreaterThanOrEqualTo(0);
            return document[start..document.IndexOf("```", start + 3, StringComparison.Ordinal)];
        }

        /// <summary>Where a path under <c>_content/Blazemoji.Components/</c> is in the source tree.</summary>
        private static string InTheLibrary(string contentPath) =>
            Path.Combine(RepositoryRoot(), "Blazemoji.Components", "wwwroot", contentPath[(contentPath.IndexOf("Blazemoji.Components/", StringComparison.Ordinal) + "Blazemoji.Components/".Length)..]);

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Blazemoji.sln")))
                directory = directory.Parent;

            return directory.ShouldNotBeNull("the tests run from inside the repository").FullName;
        }
    }
}
