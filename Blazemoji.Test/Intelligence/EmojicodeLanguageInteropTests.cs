using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Interop;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Shared.State;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.Intelligence
{
    public sealed class EmojicodeLanguageInteropTests : BunitContext
    {
        private readonly ICodeIntelligence _intelligence = Substitute.For<ICodeIntelligence>();
        private readonly IPackageLibrary _library = Substitute.For<IPackageLibrary>();
        private readonly ProjectState _project;

        public EmojicodeLanguageInteropTests()
        {
            var template = new ProjectTemplate("t", "T", "", ProjectKind.Program, "main.🍇",
                [new ProjectFile("main.🍇", "📦 grapevine 🏠\n📜 🔤other.🍇🔤"), new ProjectFile("other.🍇", "📦 json 🏠")]);
            var templates = Substitute.For<IProjectTemplates>();
            templates.All.Returns([template]);
            _project = new ProjectState(Substitute.For<IProjectStore>(), templates, NullLogger<ProjectState>.Instance);

            _intelligence.Imports(Arg.Any<string>()).Returns(call => new CodeIntelligence([], new EmojiNames()).Imports(call.Arg<string>()));
            _library.GetAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns([TestPackages.Web]);
        }

        private EmojicodeLanguageInterop Create() =>
            new(JSInterop.JSRuntime, _intelligence, _library, _project, NullLogger<EmojicodeLanguageInterop>.Instance);

        [Fact]
        public async Task Registering_loads_the_module_and_registers_the_providers_once()
        {
            var module = JSInterop.SetupModule("./_content/Blazemoji.Components/js/emojicodeLanguage.js");
            module.SetupModule("register", _ => true).SetupVoid("dispose").SetVoidResult();
            await using var interop = Create();

            await interop.RegisterAsync("emojiscript");
            await interop.RegisterAsync("emojiscript");

            var call = module.Invocations["register"].ShouldHaveSingleItem();
            call.Arguments[0].ShouldBe("emojiscript");
            call.Arguments[1].ShouldBeOfType<DotNetObjectReference<EmojicodeLanguageInterop>>();
        }

        [Fact]
        public async Task A_completion_request_is_answered_with_the_packages_of_every_file_of_the_project()
        {
            _intelligence.Complete("app.", 4, Arg.Any<IReadOnlyList<PackageDocumentation>>())
                .Returns([new CompletionEntry("📥 path handler", CompletionKind.Method, "📥 app ", 0, 4, "🍷", "docs", 0)]);
            await using var interop = Create();

            var answers = await interop.CompleteAsync("app.", 4);

            answers.ShouldHaveSingleItem().ShouldBe(new CompletionAnswer("📥 path handler", "Method", "📥 app ", 0, 4, "🍷", "docs", 0));
            await _library.Received(1).GetAsync(
                Arg.Is<IEnumerable<string>>(names => names.SequenceEqual(new[] { "s", "grapevine", "json" })),
                Arg.Any<CancellationToken>());
            _intelligence.Received(1).Complete("app.", 4, Arg.Is<IReadOnlyList<PackageDocumentation>>(packages => packages.Single() == TestPackages.Web));
        }

        [Fact]
        public async Task An_import_typed_in_the_editor_counts_before_it_has_been_saved_to_the_project()
        {
            await using var interop = Create();

            await interop.HoverAsync("📦 sockets 🏠", 0);

            await _library.Received(1).GetAsync(Arg.Is<IEnumerable<string>>(names => names.Contains("sockets")), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_hover_and_a_signature_are_passed_back_in_the_shape_the_page_expects()
        {
            _intelligence.Hover("📥", 0, Arg.Any<IReadOnlyList<PackageDocumentation>>()).Returns(new HoverInfo(0, 2, "**📥**"));
            _intelligence.Signature("📥 app ", 7, Arg.Any<IReadOnlyList<PackageDocumentation>>())
                .Returns(new SignatureInfo("📥 app path 🔡❗️", "Registers a GET route.", [new SignatureParameter("path 🔡")], 0));
            await using var interop = Create();

            (await interop.HoverAsync("📥", 0)).ShouldBe(new HoverAnswer(0, 2, "**📥**"));
            var signature = (await interop.SignatureAsync("📥 app ", 7)).ShouldNotBeNull();
            signature.Label.ShouldBe("📥 app path 🔡❗️");
            signature.Documentation.ShouldBe("Registers a GET route.");
            signature.Parameters.ShouldBe(["path 🔡"]);
            signature.ActiveParameter.ShouldBe(0);
        }

        [Fact]
        public async Task Nothing_to_say_is_passed_back_as_nothing()
        {
            await using var interop = Create();

            (await interop.CompleteAsync("x", 1)).ShouldBeEmpty();
            (await interop.HoverAsync("x", 0)).ShouldBeNull();
            (await interop.SignatureAsync("x", 1)).ShouldBeNull();
        }

        [Fact]
        public async Task A_failure_never_reaches_the_editor()
        {
            _library.GetAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));
            await using var interop = Create();

            (await interop.CompleteAsync("app.", 4)).ShouldBeEmpty();
            (await interop.HoverAsync("app", 0)).ShouldBeNull();
            (await interop.SignatureAsync("📥 app ", 7)).ShouldBeNull();
        }

        [Fact]
        public async Task Disposing_after_the_page_has_gone_does_not_throw()
        {
            var module = JSInterop.SetupModule("./_content/Blazemoji.Components/js/emojicodeLanguage.js");
            module.SetupModule("register", _ => true).SetupVoid("dispose").SetException(new JSDisconnectedException("gone"));
            var interop = Create();
            await interop.RegisterAsync("emojiscript");

            await Should.NotThrowAsync(async () => await interop.DisposeAsync());
        }
    }
}
