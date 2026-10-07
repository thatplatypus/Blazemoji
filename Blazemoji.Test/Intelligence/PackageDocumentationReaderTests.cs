using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Toolchain.Service;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Intelligence
{
    public class PackageDocumentationReaderTests
    {
        private static PackageDocumentation Read(string package)
        {
            var path = new PackageCatalog(Options.Create(new ToolchainServiceOptions())).DocumentationPath(package);
            return PackageDocumentationReader.Read(package, File.ReadAllText(path!)).ShouldNotBeNull();
        }

        [Fact]
        public void The_standard_package_has_its_types_with_their_kinds_and_documentation()
        {
            var s = Read("s");

            s.Name.ShouldBe("s");
            s.Documentation.ShouldContain("standard library");
            var text = s.Find("🔡").ShouldNotBeNull();
            text.Kind.ShouldBe(TypeKind.Class);
            text.Package.ShouldBe("s");
            text.Documentation.ShouldContain("Strings.");
            s.Find("🔢")!.Kind.ShouldBe(TypeKind.ValueType);
            s.Find("🍡")!.Kind.ShouldBe(TypeKind.Protocol);
        }

        [Fact]
        public void A_method_has_its_name_mood_documentation_parameters_and_return_type()
        {
            var text = Read("s").Find("🔡")!;

            var print = text.Methods.Single(method => method.Name == "😀");
            print.Mood.ShouldBe("❗️");
            print.Documentation.ShouldBe("Puts this 🔡 to the standard output.");
            print.Parameters.ShouldBeEmpty();
            print.ReturnType.ShouldBeNull();

            var split = text.Methods.Single(method => method.Name == "🔫");
            split.Parameters.ShouldHaveSingleItem().ShouldBe(new ParameterDocumentation("separator", new TypeReference("🔡", "🔡")));
            split.ReturnType.ShouldBe(new TypeReference("🍨🐚🔡🍆", "🍨"));
        }

        [Fact]
        public void An_initializer_has_its_parameters_and_the_unnamed_one_has_no_name()
        {
            var text = Read("s").Find("🔡")!;

            text.Initializers.ShouldContain(initializer => initializer.Name.Length == 0 && initializer.Parameters.Count == 2);
        }

        [Fact]
        public void An_optional_is_written_with_its_emoji_and_still_names_the_type_inside()
        {
            var system = Read("s").Find("💻")!;

            var environment = system.TypeMethods.Single(method => method.Name == "🌳");

            environment.ReturnType.ShouldBe(new TypeReference("🍬🔡", "🔡"));
        }

        [Fact]
        public void A_callable_parameter_is_written_as_a_closure_type_and_names_no_type()
        {
            var list = Read("s").Find("🍨")!;

            var sort = list.Methods.Single(method => method.Name == "🦁");

            sort.Parameters.ShouldHaveSingleItem().Type.ShouldBe(new TypeReference("🍇Element Element➡️🔢🍉", null));
        }

        [Fact]
        public void A_generic_parameter_is_written_by_its_name_and_names_no_type()
        {
            var list = Read("s").Find("🍨")!;

            var append = list.Methods.Single(method => method.Name == "🐻");

            append.Parameters.ShouldHaveSingleItem().ShouldBe(new ParameterDocumentation("item", new TypeReference("Element", null)));
        }

        [Fact]
        public void Documentation_is_trimmed_and_its_indentation_removed()
        {
            var text = Read("s").Find("🔡")!;

            text.Documentation.ShouldStartWith("Strings.");
            text.Documentation.ShouldContain("\nIn Emojicode strings are strictly used");
            text.Documentation.ShouldNotContain("\n  In Emojicode");
        }

        [Theory]
        [InlineData("files")]
        [InlineData("json")]
        [InlineData("sockets")]
        [InlineData("testtube")]
        public void Every_stock_report_can_be_read(string package)
        {
            var documentation = Read(package);

            documentation.Types.ShouldNotBeEmpty();
            documentation.Types.SelectMany(type => type.Methods).ShouldAllBe(method => method.Name.Length > 0);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("{\"types\":\"nope\"}")]
        public void Text_that_is_not_a_report_reads_as_nothing(string json)
        {
            PackageDocumentationReader.Read("x", json).ShouldBeNull();
        }

        [Fact]
        public void A_report_with_shapes_this_reader_does_not_know_is_still_read()
        {
            const string json = """
                { "documentation": "", "types": [ { "type": "Mystery", "name": "🦄", "documentation": null,
                  "methods": [ { "name": "✨", "mood": "❗️", "documentation": " Sparkles. ",
                                 "returnType": { "type": "SomethingNew", "detail": 1 },
                                 "parameters": [ { "name": "amount", "type": { "type": "SomethingElse" } } ] } ] } ] }
                """;

            var type = PackageDocumentationReader.Read("x", json)!.Types.ShouldHaveSingleItem();

            type.Kind.ShouldBe(TypeKind.Other);
            type.Documentation.ShouldBe(string.Empty);
            type.TypeMethods.ShouldBeEmpty();
            var method = type.Methods.ShouldHaveSingleItem();
            method.ReturnType.ShouldBe(new TypeReference("?", null));
            method.Parameters.ShouldHaveSingleItem().Type.ShouldBe(new TypeReference("?", null));
        }
    }
}
