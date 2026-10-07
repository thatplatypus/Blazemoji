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
            _catalog.Names().ShouldBe(["files", "json", "s", "sockets", "testtube"]);
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
