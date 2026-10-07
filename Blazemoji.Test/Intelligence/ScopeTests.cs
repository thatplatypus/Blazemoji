using Blazemoji.Emojicode.Intelligence;

namespace Blazemoji.Test.Intelligence
{
    public class ScopeTests
    {
        private static Scope Read(string source) =>
            Scope.Read(SourceReader.Read(source), new TypeIndex(TestPackages.All));

        private static string? TypeAtEnd(string source, string variable) => Read(source).TypeOf(variable, source.Length);

        [Fact]
        public void A_variable_assigned_from_an_initializer_has_that_type()
        {
            TypeAtEnd("🆕🍷❗️ ➡️ app", "app").ShouldBe("🍷");
        }

        [Fact]
        public void A_mutable_variable_is_read_the_same_way()
        {
            TypeAtEnd("🆕🍷❗️ ➡️ 🖍🆕 app", "app").ShouldBe("🍷");
        }

        [Fact]
        public void A_generic_initializer_gives_the_type_without_its_arguments()
        {
            TypeAtEnd("🆕🍨🐚🔡🍆❗️ ➡️ 🖍🆕items", "items").ShouldBe("🍨");
        }

        [Theory]
        [InlineData("🔤hello🔤 ➡️ greeting", "greeting", "🔡")]
        [InlineData("8080 ➡️ 🖍🆕port", "port", "🔢")]
        [InlineData("3.5 ➡️ ratio", "ratio", "💯")]
        [InlineData("👍 ➡️ done", "done", "👌")]
        public void A_variable_assigned_from_a_literal_has_the_literals_type(string source, string variable, string type)
        {
            TypeAtEnd(source, variable).ShouldBe(type);
        }

        [Fact]
        public void A_variable_assigned_from_a_documented_call_has_its_return_type()
        {
            TypeAtEnd("🆕🍷❗️ ➡️ app\n🗂 app 🔤/admin🔤❗️ ➡️ admin", "admin").ShouldBe("🗂");
        }

        [Fact]
        public void A_variable_assigned_from_a_type_method_has_its_return_type()
        {
            TypeAtEnd("✅🐇📬 🔤ok🔤❗️ ➡️ response", "response").ShouldBe("📬");
        }

        [Fact]
        public void A_variable_copied_from_another_has_the_same_type()
        {
            TypeAtEnd("🆕🍷❗️ ➡️ app\napp ➡️ same", "same").ShouldBe("🍷");
        }

        [Fact]
        public void An_optional_unwrapped_by_a_condition_gives_the_type_inside()
        {
            const string source = "🍇🎍🥡 r 📨 ➡️ 📬\n  ↪️ 🏷 r 🔤x-api-key🔤❗️ ➡️ key 🍇\n  🍉\n🍉";

            TypeAtEnd(source, "key").ShouldBe("🔡");
        }

        [Fact]
        public void A_forced_unwrap_does_not_hide_the_type()
        {
            TypeAtEnd("🍺 🆕🍷❗️ ➡️ app", "app").ShouldBe("🍷");
        }

        [Fact]
        public void A_closure_parameter_has_its_declared_type()
        {
            const string source = "📥 app 🔤/🔤 🍇🎍🥡 r 📨 ➡️ 📬\n  ↩️ ✅🐇📬 🔤hi🔤❗️\n🍉❗️";

            TypeAtEnd(source, "r").ShouldBe("📨");
        }

        [Fact]
        public void A_closure_with_two_parameters_declares_both_and_a_closure_typed_one_has_no_type()
        {
            const string source = "🧱 app 🍇🎍🥡 r 📨 next 🍇📨➡️📬🍉 ➡️ 📬\n  ↩️ ⁉️ next r❗️\n🍉❗️";

            var scope = Read(source);

            scope.TypeOf("r", source.Length).ShouldBe("📨");
            scope.TypeOf("next", source.Length).ShouldBeNull();
        }

        [Fact]
        public void A_method_parameter_has_its_declared_type()
        {
            const string source = "🐇 📒 🍇\n  ❗️ 📋 r 📨 ➡️ 📬 🍇\n    ↩️ ✅🐇📬 🔤x🔤❗️\n  🍉\n🍉";

            TypeAtEnd(source, "r").ShouldBe("📨");
        }

        [Fact]
        public void A_type_method_parameter_has_its_declared_type()
        {
            const string source = "🐇 🥣 🍇\n  🐇❗️ 🔡 body 🍯🐚⚪️🍆 key 🔡 ➡️ 🍬🔡 🍇\n  🍉\n🍉";

            var scope = Read(source);

            scope.TypeOf("body", source.Length).ShouldBe("🍯");
            scope.TypeOf("key", source.Length).ShouldBe("🔡");
        }

        [Fact]
        public void An_initializer_parameter_and_an_instance_variable_have_their_declared_types()
        {
            const string source = "🐇 📒 🍇\n  🖍🆕 store 🏪\n\n  🆕 aStore 🏪 🍇\n    aStore ➡️ 🖍store\n  🍉\n🍉";

            var scope = Read(source);

            scope.TypeOf("store", source.Length).ShouldBe("🏪");
            scope.TypeOf("aStore", source.Length).ShouldBe("🏪");
        }

        [Fact]
        public void The_value_of_an_error_check_has_the_type_of_what_was_checked()
        {
            const string source = "🆕🍷❗️ ➡️ app\n🆗 code 🚀 app 8080❗️ 🍇\n🍉";

            TypeAtEnd(source, "code").ShouldBe("🔢");
        }

        [Fact]
        public void A_call_statement_is_not_mistaken_for_a_declaration()
        {
            const string source = "🆕🍷❗️ ➡️ app\n📥 app 🔤/🔤 handler❗️\n🛑 app❗️";

            Read(source).Variables.Select(variable => variable.Name).ShouldBe(["app"]);
        }

        [Fact]
        public void The_first_line_of_a_block_is_not_read_as_the_blocks_parameters()
        {
            const string source = "❗️ 👀 r 📨 ➡️ 📬 🍇\n  ↪️ 🏷 r 🔤id🔤❗️ ➡️ id 🍇\n    r.\n  🍉\n🍉";

            var scope = Read(source);

            scope.TypeOf("r", source.IndexOf("r.", StringComparison.Ordinal)).ShouldBe("📨");
            scope.Variables.ShouldNotContain(variable => variable.TypeName == ".");
        }

        [Theory]
        [InlineData("0 ➡️ 🖍🆕 passed\n↪️ 👍 🍇\n  passed ➕ 1 ➡️ 🖍passed\n🍉", "passed", "🔢")]
        [InlineData("🖍🆕 total 🔢\ntotal ➕ 1 ➡️ 🖍total", "total", "🔢")]
        [InlineData("0 ➡️ 🖍🆕 total\n↪️ 👍 🍇 total ➕ 1 ➡️ 🖍total 🍉", "total", "🔢")]
        public void An_operator_after_a_name_never_becomes_the_names_type(string source, string variable, string type)
        {
            var scope = Read(source);

            scope.TypeOf(variable, source.Length).ShouldBe(type);
            scope.Variables.ShouldAllBe(declared => declared.TypeName == type);
        }

        [Theory]
        [InlineData("8 ➡️ count\ncount ▶️ 0 ➡️ any", "any")]
        [InlineData("🔤a🔤 ➡️ name\nname 🙌 🔤x🔤 ➡️ same", "same")]
        [InlineData("🔤a🔤 ➡️ fragment\n📐 fragment❗️ 🙌 0 ➡️ 🖍🆕found", "found")]
        [InlineData("8 ➡️ count\ncount ➕ 1 ▶️ 5 ➡️ many", "many")]
        public void A_comparison_is_a_boolean_whatever_its_left_side_is(string source, string variable)
        {
            TypeAtEnd(source, variable).ShouldBe("👌");
        }

        [Fact]
        public void Arithmetic_has_the_type_its_operator_returns()
        {
            TypeAtEnd("8 ➡️ count\ncount ➕ 1 ➡️ more", "more").ShouldBe("🔢");
        }

        [Fact]
        public void A_call_that_is_only_part_of_an_expression_does_not_give_the_whole_its_type()
        {
            const string source = "🆕🍷❗️ ➡️ app\n🚀 app 8080❗️ 🦄 2 ➡️ odd";

            TypeAtEnd(source, "odd").ShouldBeNull();
        }

        [Fact]
        public void A_call_with_a_call_inside_it_still_has_its_own_type()
        {
            const string source = "🆕🍷❗️ ➡️ app\n🗂 app 🔡 🚀 app 1❗️ 10❗️❗️ ➡️ group";

            TypeAtEnd(source, "group").ShouldBe("🗂");
        }

        [Fact]
        public void A_closure_parameter_whose_type_is_declared_in_the_file_has_that_type()
        {
            const string source = "🐇 🏪 🍇🍉\n🐰 names 🍇 item 🏪\n  item\n🍉❗️";

            TypeAtEnd(source, "item").ShouldBe("🏪");
        }

        [Fact]
        public void Lines_that_declare_are_told_apart_from_lines_that_call()
        {
            const string source = "🐇 📒 🍇\n  🖍🆕 count 🔢\n  🆕 anId 🔢 aTitle 🔡 🍇🍉\n  ❗️ 📄 r 📨 ➡️ 📬 🍇\n    📄 r❗️\n  🍉\n🍉";
            var tokens = SourceReader.Read(source);
            var scope = Scope.Read(tokens, new TypeIndex(TestPackages.All));

            bool Declares(string line) => scope.IsDeclaration(tokens.ToList().FindIndex(token => token.Start == source.IndexOf(line, StringComparison.Ordinal)));

            Declares("🖍🆕 count").ShouldBeTrue();
            Declares("🆕 anId").ShouldBeTrue();
            Declares("❗️ 📄 r 📨").ShouldBeTrue();
            Declares("📄 r❗️").ShouldBeFalse();
        }

        [Fact]
        public void What_cannot_be_seen_stays_unknown()
        {
            var scope = Read("🤷 something 1 2❗️ ➡️ mystery\n🔂 item 🐽 things 0❗️ 🍇🍉");

            scope.TypeOf("mystery", 1000).ShouldBeNull();
            scope.TypeOf("item", 1000).ShouldBeNull();
            scope.TypeOf("never-declared", 1000).ShouldBeNull();
        }

        [Fact]
        public void The_nearest_declaration_above_wins_and_one_below_is_still_found()
        {
            const string source = "🔤a🔤 ➡️ x\n8 ➡️ x\n";

            var scope = Read(source);

            scope.TypeOf("x", "🔤a🔤 ➡️ x".Length).ShouldBe("🔡");
            scope.TypeOf("x", source.Length).ShouldBe("🔢");
            scope.TypeOf("x", 0).ShouldBe("🔡");
        }

        [Fact]
        public void Variables_are_listed_nearest_first_and_once_each()
        {
            const string source = "🔤a🔤 ➡️ far\n8 ➡️ near\n9 ➡️ near\n";

            Read(source).Near(source.Length).Select(variable => variable.Name).ShouldBe(["near", "far"]);
        }

        [Fact]
        public void Code_in_comments_and_strings_declares_nothing()
        {
            Read("💭 🆕🍷❗️ ➡️ app\n😀 🔤🆕🍷❗️ ➡️ app🔤❗️").Variables.ShouldBeEmpty();
        }
    }
}
