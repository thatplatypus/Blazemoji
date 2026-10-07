using System.Text.Json;
using Blazemoji.Toolchain.Service;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Service
{
    /// <summary>
    /// The documentation files that ship with the service, as the default options find them.
    /// </summary>
    public class ShippedPackageDocumentationTests
    {
        private readonly PackageCatalog _catalog = new(Options.Create(new ToolchainServiceOptions()));

        [Fact]
        public void The_five_stock_packages_are_listed()
        {
            // Grapevine is listed too once scripts/build-grapevine.sh has put its report in place.
            _catalog.Names().Where(name => name != "grapevine").ShouldBe(["files", "json", "s", "sockets", "testtube"]);
        }

        [Fact]
        [Trait("Requires", "Grapevine")]
        public void The_grapevine_report_documents_the_routing_methods_of_the_app_type()
        {
            Assert.SkipWhen(_catalog.DocumentationPath("grapevine") is null, "The Grapevine package is not built. Run scripts/build-grapevine.sh.");
            using var document = JsonDocument.Parse(File.ReadAllText(_catalog.DocumentationPath("grapevine")!));

            var app = document.RootElement.GetProperty("types").EnumerateArray().Single(type => type.GetProperty("name").GetString() == "🍷");
            var methods = app.GetProperty("methods").EnumerateArray().Select(method => method.GetProperty("name").GetString()).ToList();

            // The report writes ✏ without the variation selector that the source has after it.
            methods.ShouldContain("📥");
            methods.ShouldContain("📮");
            methods.ShouldContain("✏");
            methods.ShouldContain("🗑");
            methods.ShouldContain("🧱");
        }

        [Theory]
        [InlineData("s", "🔡")]
        [InlineData("files", "📄")]
        [InlineData("json", "🚧🔸🌸")]
        [InlineData("sockets", "🏄")]
        [InlineData("testtube", "🧪")]
        public void Each_report_lists_types_with_methods(string package, string expectedType)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_catalog.DocumentationPath(package)!));

            var types = document.RootElement.GetProperty("types");
            types.GetArrayLength().ShouldBeGreaterThan(0);
            types.EnumerateArray().Select(type => type.GetProperty("name").GetString()).ShouldContain(expectedType);
            types.EnumerateArray().Select(HasMethodList).ShouldAllBe(hasMethods => hasMethods);
        }

        private static bool HasMethodList(JsonElement type) =>
            type.TryGetProperty("methods", out var methods) && methods.ValueKind == JsonValueKind.Array;

        [Fact]
        public void A_string_method_carries_its_name_parameters_return_type_and_documentation()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_catalog.DocumentationPath("s")!));
            var stringType = document.RootElement.GetProperty("types").EnumerateArray().First(type => type.GetProperty("name").GetString() == "🔡");

            var method = stringType.GetProperty("methods").EnumerateArray().First(m => m.GetProperty("parameters").GetArrayLength() > 0);

            method.GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
            method.GetProperty("parameters")[0].GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
            method.GetProperty("parameters")[0].GetProperty("type").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
            method.TryGetProperty("documentation", out JsonElement documentation).ShouldBeTrue();
        }
    }
}
