using Blazemoji.Emojicode;
using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Services.Projects;
using Blazemoji.Toolchain.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Intelligence
{
    /// <summary>
    /// The editor's help against the real thing: Grapevine's own documentation report and the
    /// source of its Todo sample. Needs scripts/build-grapevine.sh to have been run.
    /// </summary>
    [Trait("Requires", "Grapevine")]
    public class GrapevineIntelligenceTests
    {
        private const string SkipReason = "The Grapevine package is not built. Run scripts/build-grapevine.sh.";

        private static readonly CodeIntelligence Intelligence = new(
            typeof(EmojicodeKeyword).Assembly.GetTypes()
                .Where(type => type.IsSubclassOf(typeof(EmojicodeKeyword)))
                .Select(type => (EmojicodeKeyword)Activator.CreateInstance(type)!),
            new EmojiNames());

        private static string? ReportPath => new PackageCatalog(Options.Create(new ToolchainServiceOptions())).DocumentationPath("grapevine");

        private static IReadOnlyList<PackageDocumentation> Packages() =>
            [TestPackages.Standard, PackageDocumentationReader.Read("grapevine", File.ReadAllText(ReportPath!))!];

        private static string Startup() =>
            new FileProjectTemplates(Options.Create(new ProjectTemplateOptions()), NullLogger<FileProjectTemplates>.Instance)
                .All.Single(template => template.Id == "grapevine-todo")
                .Files.Single(file => file.Path == "startup.🍇").Content;

        /// <summary>The sample's startup file with a line added straight after the app is created.</summary>
        private static (string Text, int Offset) AfterTheAppIsCreated(string typed)
        {
            var startup = Startup();
            var line = startup.IndexOf("➡️ app", StringComparison.Ordinal);
            var end = startup.IndexOf('\n', line);
            var text = startup[..(end + 1)] + "    " + typed + "\n" + startup[(end + 1)..];
            return (text, end + 1 + 4 + typed.Length);
        }

        [Fact]
        public void A_dot_after_the_app_offers_its_routing_methods()
        {
            Assert.SkipWhen(ReportPath is null, SkipReason);
            var (text, offset) = AfterTheAppIsCreated("app.");

            var entries = Intelligence.Complete(text, offset, Packages());

            var names = entries.Select(entry => entry.Label.Split(' ')[0]).ToList();
            names.ShouldContain("📥");
            names.ShouldContain("📮");
            names.ShouldContain("✏");
            names.ShouldContain("🗑");
            names.ShouldContain("🧱");
            entries.Single(entry => entry.Label.StartsWith("🧱")).Documentation.ShouldContain("Adds middleware to the pipeline.");
            entries.Single(entry => entry.Label.StartsWith("📥")).Documentation.ShouldContain("📥 app path 🔡 handler 🍇📨➡️📬🍉❗️");
        }

        [Fact]
        public void Once_the_app_is_the_receiver_a_route_shows_its_parameters()
        {
            Assert.SkipWhen(ReportPath is null, SkipReason);
            var (text, offset) = AfterTheAppIsCreated("📥 app ");

            var signature = Intelligence.Signature(text, offset, Packages()).ShouldNotBeNull();

            signature.Label.ShouldBe("📥 app path 🔡 handler 🍇📨➡️📬🍉❗️");
            signature.ActiveParameter.ShouldBe(0);
        }

        [Fact]
        public void The_request_inside_a_handler_offers_the_request_types_methods()
        {
            Assert.SkipWhen(ReportPath is null, SkipReason);
            var startup = Startup();
            var handler = startup.IndexOf("📥 app 🔤/hello/:name🔤", StringComparison.Ordinal);
            var end = startup.IndexOf('\n', handler);
            var text = startup[..(end + 1)] + "      r." + startup[end..];

            var entries = Intelligence.Complete(text, end + 1 + "      r.".Length, Packages());

            entries.Count.ShouldBeGreaterThan(10);
            entries.ShouldContain(entry => entry.Label.StartsWith("🏷"));
        }

        [Fact]
        public void A_route_group_made_from_the_app_is_known_to_be_one()
        {
            Assert.SkipWhen(ReportPath is null, SkipReason);
            var startup = Startup();
            var group = startup.IndexOf("➡️ admin", StringComparison.Ordinal);
            var end = startup.IndexOf('\n', group);
            var text = startup[..(end + 1)] + "    admin." + startup[end..];

            var entries = Intelligence.Complete(text, end + 1 + "    admin.".Length, Packages());

            entries.ShouldNotBeEmpty();
            entries.ShouldAllBe(entry => entry.Detail.StartsWith("🗂"));
        }

        [Fact]
        public void The_sample_imports_grapevine()
        {
            Assert.SkipWhen(ReportPath is null, SkipReason);
            var main = new FileProjectTemplates(Options.Create(new ProjectTemplateOptions()), NullLogger<FileProjectTemplates>.Instance)
                .All.Single(template => template.Id == "grapevine-todo")
                .Files.Single(file => file.Path == "main.🍇").Content;

            Intelligence.Imports(main).ShouldBe(["s", "grapevine"]);
        }
    }
}
