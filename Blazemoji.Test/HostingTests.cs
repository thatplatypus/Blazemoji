using Blazemoji.Components;
using Blazemoji.Emojicode;
using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Interop;
using Blazemoji.Services.Library;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.State;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Http;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;

namespace Blazemoji.Test
{
    /// <summary>
    /// docs/hosting.md tells a host which registrations it must make. These build a container
    /// from exactly those and check that the editor has everything it asks for, so that the
    /// document and the code cannot drift apart.
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
            Services.GetRequiredService<IPackageLibrary>().ShouldNotBeNull();
            Services.GetRequiredService<IProjectTemplates>().All.ShouldNotBeEmpty();
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
        public void The_provider_module_is_loaded_from_the_librarys_own_static_files()
        {
            var module = JSInterop.SetupModule("./_content/Blazemoji.Components/js/emojicodeLanguage.js");
            module.SetupModule("register", _ => true);
            var interop = Services.GetRequiredService<EmojicodeLanguageInterop>();

            Should.NotThrow(() => interop.RegisterAsync("emojiscript").GetAwaiter().GetResult());

            module.Invocations["register"].ShouldHaveSingleItem();
        }
    }
}
