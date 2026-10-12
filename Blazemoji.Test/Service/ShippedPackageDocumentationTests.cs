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
            // And no others: a report left here by hand would be offered for a package that no
            // program is linked against.
            _catalog.Names().ShouldBe(["files", "json", "s", "sockets", "testtube"]);
        }

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
