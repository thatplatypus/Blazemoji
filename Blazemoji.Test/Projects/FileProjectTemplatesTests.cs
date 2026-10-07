using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Toolchain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Projects
{
    public sealed class FileProjectTemplatesTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "templates-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private FileProjectTemplates Create(string? root = null) =>
            new(Options.Create(new ProjectTemplateOptions { Path = root ?? _root }), NullLogger<FileProjectTemplates>.Instance);

        private void Write(string relativePath, string content)
        {
            var path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        private void WriteTemplate(string id, string name, int order, string kind = "program", string entry = "main.🍇")
        {
            Write($"{id}/template.json", $$"""{ "name": "{{name}}", "description": "About {{name}}.", "kind": "{{kind}}", "entry": "{{entry}}", "order": {{order}} }""");
            Write($"{id}/{entry}", "🏁 🍇 🍉");
        }

        [Fact]
        public void A_template_is_a_folder_with_a_description_and_its_files()
        {
            WriteTemplate("api", "An API", 1, kind: "server", entry: "app/main.🍇");
            Write("api/app/routes.🍇", "routes");
            Write("api/shared/util.🍇", "util");

            var template = Create().All.ShouldHaveSingleItem();

            template.Id.ShouldBe("api");
            template.Name.ShouldBe("An API");
            template.Description.ShouldBe("About An API.");
            template.Kind.ShouldBe(ProjectKind.Server);
            template.Entry.ShouldBe("app/main.🍇");
            template.Files.OrderBy(f => f.Path, StringComparer.Ordinal).ShouldBe(
            [
                new ProjectFile("app/main.🍇", "🏁 🍇 🍉"),
                new ProjectFile("app/routes.🍇", "routes"),
                new ProjectFile("shared/util.🍇", "util"),
            ]);
        }

        [Fact]
        public void Templates_are_offered_in_their_stated_order()
        {
            WriteTemplate("zebra", "Zebra", 1);
            WriteTemplate("apple", "Apple", 3);
            WriteTemplate("mango", "Mango", 2);

            Create().All.Select(t => t.Name).ShouldBe(["Zebra", "Mango", "Apple"]);
        }

        [Fact]
        public void A_folder_that_is_not_a_usable_template_is_left_out()
        {
            WriteTemplate("good", "Good", 1);
            Write("no-description/main.🍇", "🏁 🍇 🍉");
            Write("broken-json/template.json", "{ not json");
            Write("broken-json/main.🍇", "🏁 🍇 🍉");
            Write("missing-entry/template.json", """{ "name": "Missing", "description": "", "kind": "program", "entry": "main.🍇" }""");
            Write("missing-entry/other.🍇", "🏁 🍇 🍉");
            Write("no-name/template.json", """{ "description": "", "kind": "program", "entry": "main.🍇" }""");
            Write("no-name/main.🍇", "🏁 🍇 🍉");

            Create().All.ShouldHaveSingleItem().Name.ShouldBe("Good");
        }

        [Fact]
        public void A_file_the_toolchain_would_refuse_is_not_part_of_a_template()
        {
            WriteTemplate("good", "Good", 1);
            Write("good/has space.🍇", "x");
            Write("good/.DS_Store", "x");

            Create().All.ShouldHaveSingleItem().Files.Select(f => f.Path).ShouldBe(["main.🍇"]);
        }

        [Fact]
        public void With_no_templates_on_disk_there_is_still_one_to_start_from()
        {
            var template = Create(Path.Combine(_root, "does-not-exist")).All.ShouldHaveSingleItem();

            template.Name.ShouldBe("Hello World");
            template.Entry.ShouldBe("main.🍇");
            template.Files.ShouldHaveSingleItem().Content.ShouldContain("🏁");
        }

        [Fact]
        public void The_templates_shipped_with_the_app_are_found_and_well_formed()
        {
            var shipped = new FileProjectTemplates(Options.Create(new ProjectTemplateOptions()), NullLogger<FileProjectTemplates>.Instance).All;

            shipped.Select(t => t.Id).ShouldContain("hello-world");
            shipped.Select(t => t.Id).ShouldContain("two-files");
            shipped[0].Id.ShouldBe("hello-world");
            foreach (var template in shipped)
            {
                template.Files.ShouldContain(file => file.Path == template.Entry, template.Id);
                template.Files.ShouldAllBe(file => SourceFileNames.IsSafe(file.Path));
                template.Description.ShouldNotBeNullOrWhiteSpace(template.Id);
            }
        }
    }
}
