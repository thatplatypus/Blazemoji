using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Toolchain.Service;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Intelligence
{
    /// <summary>
    /// Package documentation for tests: the real standard package, and a small web framework
    /// shaped like Grapevine so that the tests do not need Grapevine built.
    /// </summary>
    internal static class TestPackages
    {
        private static readonly TypeReference Text = new("🔡", "🔡");
        private static readonly TypeReference Handler = new("🍇📨➡️📬🍉", null);

        public static readonly PackageDocumentation Standard = ReadShipped("s");

        public static readonly PackageDocumentation Web = new("web", "A small web framework.",
        [
            new TypeDocumentation("web", "🍷", TypeKind.Class, "The app: routes and middleware.",
            [
                new MethodDocumentation("📥", "❗️", "Registers a GET route.", [new("path", Text), new("handler", Handler)], null),
                new MethodDocumentation("📮", "❗️", "Registers a POST route.", [new("path", Text), new("handler", Handler)], null),
                new MethodDocumentation("✏", "❗️", string.Empty, [new("path", Text), new("handler", Handler)], null),
                new MethodDocumentation("🗂", "❗️", "A route group under *prefix*.", [new("prefix", Text)], new TypeReference("🗂", "🗂")),
                new MethodDocumentation("🚀", "❗️", "Serves the app on *port*.", [new("port", new TypeReference("🔢", "🔢"))], new TypeReference("🔢", "🔢")),
                new MethodDocumentation("🛑", "❗️", "Asks the server to stop.", [], null),
            ],
            [new MethodDocumentation("📝", "❗️", "A request logger.", [], Handler)],
            [new MethodDocumentation(string.Empty, "❗️", "Creates an app with no routes.", [], null)]),

            new TypeDocumentation("web", "🗂", TypeKind.Class, "A group of routes under a prefix.",
            [new MethodDocumentation("📥", "❗️", "Registers a GET route in the group.", [new("path", Text), new("handler", Handler)], null)],
            [],
            []),

            new TypeDocumentation("web", "📨", TypeKind.Class, "A request.",
            [
                new MethodDocumentation("🏷", "❗️", "A header's value.", [new("name", Text)], new TypeReference("🍬🔡", "🔡")),
                new MethodDocumentation("📄", "❗️", "The body as text.", [], Text),
            ],
            [],
            []),

            new TypeDocumentation("web", "📬", TypeKind.Class, "A response.",
            [],
            [new MethodDocumentation("✅", "❗️", "A 200 response with a text body.", [new("text", Text)], new TypeReference("📬", "📬"))],
            []),
        ]);

        public static readonly IReadOnlyList<PackageDocumentation> All = [Standard, Web];

        public static PackageDocumentation ReadShipped(string package)
        {
            var path = new PackageCatalog(Options.Create(new ToolchainServiceOptions())).DocumentationPath(package);
            return PackageDocumentationReader.Read(package, File.ReadAllText(path!))!;
        }
    }
}
