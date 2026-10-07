using Blazemoji.Emojicode;
using Blazemoji.Emojicode.Intelligence;

namespace Blazemoji.Test.Intelligence
{
    public class CodeIntelligenceTests
    {
        private const string Cursor = "‸";

        private const string App = "🆕🍷❗️ ➡️ app\n";

        private static readonly CodeIntelligence Intelligence = new(Keywords(), new EmojiNames());

        private static List<EmojicodeKeyword> Keywords() =>
            typeof(EmojicodeKeyword).Assembly.GetTypes()
                .Where(type => type.IsSubclassOf(typeof(EmojicodeKeyword)))
                .Select(type => (EmojicodeKeyword)Activator.CreateInstance(type)!)
                .ToList();

        /// <summary>The text with the cursor mark taken out, and where the mark was.</summary>
        private static (string Text, int Offset) At(string marked)
        {
            var offset = marked.IndexOf(Cursor, StringComparison.Ordinal);
            offset.ShouldBeGreaterThanOrEqualTo(0, "the test text needs a cursor mark");
            return (marked.Remove(offset, Cursor.Length), offset);
        }

        private static IReadOnlyList<CompletionEntry> Complete(string marked)
        {
            var (text, offset) = At(marked);
            return Intelligence.Complete(text, offset, TestPackages.All);
        }

        private static string Apply(string marked, CompletionEntry entry)
        {
            var (text, _) = At(marked);
            return text[..entry.ReplaceStart] + entry.Insert + text[entry.ReplaceEnd..];
        }

        private static SignatureInfo? Signature(string marked)
        {
            var (text, offset) = At(marked);
            return Intelligence.Signature(text, offset, TestPackages.All);
        }

        private static HoverInfo? Hover(string marked)
        {
            var (text, offset) = At(marked);
            return Intelligence.Hover(text, offset, TestPackages.All);
        }

        // ---- receiver first, then a dot -------------------------------------------------

        [Fact]
        public void A_dot_after_a_variable_offers_the_methods_of_its_type_in_their_own_order()
        {
            var entries = Complete(App + "app.‸");

            entries.Select(entry => entry.Label).ShouldBe(["📥 path handler", "📮 path handler", "✏ path handler", "🗂 prefix", "🚀 port", "🛑"]);
            entries.ShouldAllBe(entry => entry.Kind == CompletionKind.Method);
            entries.Select(entry => entry.Order).ShouldBe([0, 1, 2, 3, 4, 5]);
        }

        [Fact]
        public void Each_method_says_what_it_belongs_to_what_it_returns_and_what_it_does()
        {
            var group = Complete(App + "app.‸").Single(entry => entry.Label.StartsWith("🗂"));

            group.Detail.ShouldBe("🍷 ➡️ 🗂");
            group.Documentation.ShouldContain("🗂 app prefix 🔡❗️ ➡️ 🗂");
            group.Documentation.ShouldContain("A route group under *prefix*.");
        }

        [Fact]
        public void Accepting_a_method_rewrites_the_receiver_and_dot_into_a_call_ready_for_its_arguments()
        {
            const string marked = App + "  app.‸";
            var get = Complete(marked).Single(entry => entry.Label.StartsWith("📥"));

            Apply(marked, get).ShouldBe(App + "  📥 app ");
        }

        [Fact]
        public void A_method_without_parameters_is_written_out_whole()
        {
            const string marked = App + "app.‸";
            var stop = Complete(marked).Single(entry => entry.Label == "🛑");

            Apply(marked, stop).ShouldBe(App + "🛑 app❗️");
        }

        [Fact]
        public void Letters_after_the_dot_narrow_the_list_by_emoji_name_parameter_name_or_description()
        {
            Complete(App + "app.inb‸").Select(entry => entry.Label).ShouldBe(["📥 path handler"]);
            Complete(App + "app.pre‸").Select(entry => entry.Label).ShouldBe(["🗂 prefix"]);
            Complete(App + "app.POST‸").Select(entry => entry.Label).ShouldBe(["📮 path handler"]);
        }

        [Fact]
        public void The_letters_typed_after_the_dot_are_replaced_too()
        {
            const string marked = App + "app.inb‸ 🔤/🔤";
            var get = Complete(marked).ShouldHaveSingleItem();

            Apply(marked, get).ShouldBe(App + "📥 app  🔤/🔤");
        }

        [Fact]
        public void A_method_the_report_has_no_words_for_still_shows_its_signature_and_the_emojis_name()
        {
            var edit = Complete(App + "app.‸").Single(entry => entry.Label.StartsWith("✏"));

            edit.Documentation.ShouldContain("✏ app path 🔡 handler 🍇📨➡️📬🍉❗️");
            edit.Documentation.ShouldContain("pencil2");
        }

        [Fact]
        public void A_closure_parameter_works_as_a_receiver()
        {
            var entries = Complete(App + "📥 app 🔤/🔤 🍇🎍🥡 r 📨 ➡️ 📬\n  r.‸\n🍉❗️");

            entries.Select(entry => entry.Label).ShouldBe(["🏷 name", "📄"]);
        }

        [Fact]
        public void A_variable_of_a_standard_type_gets_the_standard_packages_methods()
        {
            var entries = Complete("🔤hello🔤 ➡️ greeting\ngreeting.‸");

            entries.Select(entry => entry.Label).ShouldContain("😀");
            entries.Count.ShouldBeGreaterThan(10);
        }

        [Fact]
        public void A_dot_after_something_whose_type_is_not_known_offers_nothing()
        {
            Complete("mystery.‸").ShouldBeEmpty();
            Complete(App + "8080.‸").ShouldBeEmpty();
        }

        // ---- name search ----------------------------------------------------------------

        [Fact]
        public void A_word_finds_methods_of_nearby_types_before_keywords_and_plain_emoji()
        {
            var entries = Complete(App + "inbox‸");

            entries[0].Label.ShouldBe("📥 path handler");
            entries[0].Kind.ShouldBe(CompletionKind.Method);
            entries[0].Detail.ShouldBe("🍷 (app)");
            entries.ShouldContain(entry => entry.Kind == CompletionKind.Emoji && entry.Insert == "📥");
            entries.First(entry => entry.Kind == CompletionKind.Method).Order.ShouldBeLessThan(entries.First(entry => entry.Kind == CompletionKind.Emoji).Order);
        }

        [Fact]
        public void Accepting_a_method_found_by_name_replaces_the_word_with_the_emoji_ready_for_its_receiver()
        {
            const string marked = App + "  inbox‸";
            var method = Complete(marked)[0];

            Apply(marked, method).ShouldBe(App + "  📥 ");
        }

        [Fact]
        public void A_colon_and_a_word_finds_emoji_and_replaces_the_colon_too()
        {
            const string marked = "😀 :grap‸";
            var grapes = Complete(marked).First(entry => entry.Kind == CompletionKind.Emoji);

            grapes.Label.ShouldBe("🍇 grapes");
            Apply(marked, grapes).ShouldBe("😀 🍇");
        }

        [Fact]
        public void A_name_search_matches_methods_by_their_emojis_name_only()
        {
            // The standard package's text methods talk about "graphemes", and one has a "length"
            // parameter. Neither makes them an answer to someone asking for an emoji by name.
            const string source = "🔤x🔤 ➡️ key\n";

            Complete(source + ":grap‸")[0].Insert.ShouldBe("🍇");
            Complete(source + "leng‸").ShouldNotContain(entry => entry.Kind == CompletionKind.Method);
        }

        [Fact]
        public void A_colon_search_leaves_variables_out()
        {
            Complete(App + ":ap‸").ShouldNotContain(entry => entry.Kind == CompletionKind.Variable);
        }

        [Fact]
        public void A_word_finds_the_variables_that_start_with_it_first()
        {
            var entries = Complete(App + "ap‸");

            entries[0].ShouldBe(entries.Single(entry => entry.Kind == CompletionKind.Variable));
            entries[0].Label.ShouldBe("app");
            entries[0].Detail.ShouldBe("🍷");
        }

        [Fact]
        public void A_variable_typed_out_in_full_is_not_offered_back()
        {
            Complete(App + "app‸").ShouldNotContain(entry => entry.Kind == CompletionKind.Variable);
        }

        [Theory]
        [InlineData("if", "↪️")]
        [InlineData("print", "😀")]
        [InlineData("return", "↩️")]
        public void A_word_finds_the_keyword_that_means_it(string word, string emoji)
        {
            var keyword = Complete(word + "‸").First(entry => entry.Kind == CompletionKind.Keyword);

            EmojiText.Bare(keyword.Insert).ShouldBe(EmojiText.Bare(emoji));
        }

        [Fact]
        public void A_keyword_comes_with_its_description_and_its_example()
        {
            var assign = Complete("assign‸").First(entry => entry.Kind == CompletionKind.Keyword && entry.Insert.StartsWith("➡"));

            assign.Documentation.ShouldContain("1 ➡️ x");
        }

        [Fact]
        public void One_letter_is_not_enough_to_search_on_unless_it_follows_a_colon()
        {
            Complete("g‸").ShouldBeEmpty();
            Complete(":g‸").ShouldNotBeEmpty();
        }

        [Theory]
        [InlineData("😀 🔤inbox‸")]
        [InlineData("😀 🔤closed🔤‸")]
        [InlineData("💭 inbox‸")]
        [InlineData("‸")]
        [InlineData("📥 ‸")]
        [InlineData("8080‸")]
        public void Nothing_is_offered_inside_strings_and_comments_or_where_nothing_was_typed(string marked)
        {
            Complete(marked).ShouldBeEmpty();
        }

        // ---- signature help -------------------------------------------------------------

        [Fact]
        public void Once_the_receiver_is_typed_the_parameters_are_shown_with_the_first_one_current()
        {
            var signature = Signature(App + "📥 app ‸").ShouldNotBeNull();

            signature.Label.ShouldBe("📥 app path 🔡 handler 🍇📨➡️📬🍉❗️");
            signature.Parameters.Select(parameter => parameter.Label).ShouldBe(["path 🔡", "handler 🍇📨➡️📬🍉"]);
            signature.ActiveParameter.ShouldBe(0);
            signature.Documentation.ShouldBe("Registers a GET route.");
        }

        [Fact]
        public void Each_argument_typed_moves_on_to_the_next_parameter()
        {
            Signature(App + "📥 app 🔤/todos🔤 ‸")!.ActiveParameter.ShouldBe(1);
            Signature(App + "📥 app 🔤/todos🔤 handler ‸")!.ActiveParameter.ShouldBe(2);
        }

        [Fact]
        public void A_finished_call_shows_nothing()
        {
            Signature(App + "📥 app 🔤/todos🔤 handler❗️ ‸").ShouldBeNull();
        }

        [Fact]
        public void A_call_inside_an_argument_has_its_own_parameters_and_then_counts_as_one_argument()
        {
            const string outer = App + "🆕🍷❗️ ➡️ other\n";

            Signature(outer + "🚀 app 🚀 other ‸")!.Label.ShouldBe("🚀 other port 🔢❗️ ➡️ 🔢");
            var back = Signature(outer + "🚀 app 🚀 other 8080❗️ ‸").ShouldNotBeNull();
            back.Label.ShouldBe("🚀 app port 🔢❗️ ➡️ 🔢");
            back.ActiveParameter.ShouldBe(1);
        }

        [Fact]
        public void An_argument_built_with_an_operator_is_one_argument()
        {
            Signature(App + "🚀 app 8000 ➕ 80 ‸")!.ActiveParameter.ShouldBe(1);
        }

        [Fact]
        public void A_closure_argument_closed_on_the_line_is_one_argument_and_an_open_one_hides_the_parameters()
        {
            Signature(App + "📥 app 🔤/🔤 🍇 r 📨 ➡️ 📬 ↩️ ✅🐇📬 🔤x🔤❗️ 🍉 ‸")!.ActiveParameter.ShouldBe(2);
            Signature(App + "📥 app 🔤/🔤 🍇🎍🥡 r 📨 ➡️ 📬 ‸").ShouldBeNull();
        }

        [Fact]
        public void A_type_method_and_an_initializer_show_their_parameters_too()
        {
            Signature("✅🐇📬 ‸")!.Label.ShouldBe("✅🐇📬 text 🔡❗️ ➡️ 📬");
            Signature("🆕🍷 ‸")!.Label.ShouldBe("🆕🍷❗️");
        }

        [Fact]
        public void A_method_the_receivers_type_does_not_have_shows_nothing()
        {
            Signature(App + "🦄 app ‸").ShouldBeNull();
            Signature("📥 unknown ‸").ShouldBeNull();
        }

        [Fact]
        public void Only_the_line_the_cursor_is_on_counts()
        {
            Signature(App + "📥 app 🔤/🔤\n‸").ShouldBeNull();
        }

        // ---- hover ----------------------------------------------------------------------

        [Fact]
        public void Hovering_a_method_in_a_call_shows_its_signature_and_documentation()
        {
            var hover = Hover(App + "‸📥 app 🔤/🔤 handler❗️").ShouldNotBeNull();

            hover.Markdown.ShouldContain("📥 app path 🔡 handler 🍇📨➡️📬🍉❗️");
            hover.Markdown.ShouldContain("Registers a GET route.");
            (hover.End - hover.Start).ShouldBe("📥".Length);
        }

        [Fact]
        public void Hovering_a_type_shows_what_it_is_and_its_documentation()
        {
            Hover("🆕‸🍷❗️ ➡️ app")!.Markdown.ShouldBe("**🍷** class in `web`\n\nThe app: routes and middleware.");
            Hover("‸🆕🍷❗️ ➡️ app")!.Markdown.ShouldStartWith("**🍷** class in `web`");
        }

        [Fact]
        public void Hovering_a_variable_shows_its_type()
        {
            Hover(App + "📥 a‸pp 🔤/🔤 handler❗️")!.Markdown.ShouldBe("`app` is a 🍷\n\nThe app: routes and middleware.");
        }

        [Fact]
        public void Hovering_a_keyword_shows_its_description()
        {
            Hover("‸🏁 🍇 🍉")!.Markdown.ShouldContain("🏁");
            Hover("‸↪️ x 🍇🍉")!.Markdown.ShouldContain("If conditional");
        }

        [Fact]
        public void Hovering_a_method_whose_receiver_is_not_known_says_which_types_have_it()
        {
            Hover("‸📥 something 🔤/🔤❗️")!.Markdown.ShouldBe("**📥** is a method of 🍷, 🗂.");
        }

        [Fact]
        public void Hovering_any_other_emoji_shows_its_name()
        {
            Hover("😀 ‸🦄❗️")!.Markdown.ShouldBe("🦄 unicorn");
        }

        [Fact]
        public void Hovering_a_type_method_call_shows_its_signature()
        {
            Hover("‸✅🐇📬 🔤ok🔤❗️")!.Markdown.ShouldContain("✅🐇📬 text 🔡❗️ ➡️ 📬");
        }

        [Theory]
        [InlineData("😀 🔤he‸llo🔤❗️")]
        [InlineData("80‸80 ➡️ port")]
        [InlineData("my‸stery")]
        [InlineData("   ‸   ")]
        public void Hovering_text_numbers_unknown_names_and_blank_space_shows_nothing(string marked)
        {
            Hover(marked).ShouldBeNull();
        }

        // ---- imports --------------------------------------------------------------------

        [Fact]
        public void A_file_imports_the_standard_package_and_whatever_it_names()
        {
            Intelligence.Imports("📦 grapevine 🏠\n📦 json 🏠\n📦 grapevine 🏠\n🏁 🍇 🍉").ShouldBe(["s", "grapevine", "json"]);
            Intelligence.Imports("🏁 🍇 🍉").ShouldBe(["s"]);
            Intelligence.Imports("💭 📦 files 🏠").ShouldBe(["s"]);
        }

        // ---- found by the independent review -------------------------------------------

        [Fact]
        public void A_dot_on_the_first_line_of_a_block_still_offers_the_receivers_methods()
        {
            var entries = Complete("❗️ 👀 r 📨 ➡️ 📬 🍇\n  ↪️ 🏷 r 🔤id🔤❗️ ➡️ id 🍇\n    r.‸\n  🍉\n🍉");

            entries.Select(entry => entry.Label).ShouldBe(["🏷 name", "📄"]);
        }

        [Fact]
        public void An_operator_is_written_between_its_operands_and_never_as_a_call()
        {
            const string marked = "8080 ➡️ port\nport.‸";
            var entries = Complete(marked);
            var plus = entries.Single(entry => entry.Label.StartsWith("➕"));

            Apply(marked, plus).ShouldBe("8080 ➡️ port\nport ➕ ");
            plus.Documentation.ShouldContain("port ➕ other 🔢 ➡️ 🔢");
            entries.ShouldNotContain(entry => entry.Insert.StartsWith("➕ port") || entry.Insert.StartsWith("🙌 port"));
        }

        [Fact]
        public void A_method_that_is_written_as_an_assignment_is_not_offered_as_a_call()
        {
            var entries = Complete("🆕🍨🐚🔡🍆❗️ ➡️ items\nitems.‸");

            entries.Count(entry => entry.Label.StartsWith("🐽")).ShouldBe(1);
            entries.Single(entry => entry.Label.StartsWith("🐽")).Label.ShouldBe("🐽 index");
        }

        [Fact]
        public void Hovering_an_operator_describes_it_with_the_operand_on_its_left()
        {
            var hover = Hover("8 ➡️ a\n9 ➡️ b\na ‸➕ b").ShouldNotBeNull();

            hover.Markdown.ShouldContain("a ➕ other 🔢 ➡️ 🔢");
            hover.Markdown.ShouldNotContain("➕ b");
        }

        [Theory]
        [InlineData("🐇 📒 🍇\n  🆕 anId ‸🔢 aTitle 🔡 isDone 👌 🍇🍉\n🍉", "aTitle")]
        [InlineData("🐇 📒 🍇\n  ❗️ ‸📄 r 📨 ➡️ 📬 🍇🍉\n🍉", "📄 r")]
        [InlineData("🔤x🔤 ➡️ name\n🖍🆕 count ‸🔢\nname ➡️ other", "name")]
        public void A_declaration_is_not_described_as_a_call(string marked, string whatACallWouldName)
        {
            var hover = Hover(marked);

            (hover?.Markdown ?? string.Empty).ShouldNotContain("```emojiscript\n" + whatACallWouldName.Split(' ')[0] + " " + whatACallWouldName.Split(' ').Last());
            (hover?.Markdown ?? string.Empty).ShouldNotContain("❗️");
        }

        [Theory]
        [InlineData("🐇 📒 🍇\n  🆕 anId 🔢 aTitle 🔡 ‸")]
        [InlineData("🐇 📒 🍇\n  ❗️ 📄 r 📨 ‸")]
        [InlineData("🖍🆕 count 🔢 ‸")]
        public void Nothing_is_shown_as_being_called_while_a_declaration_is_typed(string marked)
        {
            Signature(marked).ShouldBeNull();
        }

        [Fact]
        public void An_initializer_with_several_forms_shows_one_an_application_would_call()
        {
            Signature("🆕🔡 ‸")!.Label.ShouldStartWith("🆕🔡 list ");
        }

        [Fact]
        public void The_form_of_an_initializer_follows_the_arguments_given()
        {
            Signature("🆕⏩ ‸")!.Label.ShouldBe("🆕⏩ start 🔢 stop 🔢❗️");
            var withRoomForAStep = Signature("🆕⏩ 0 10 ‸").ShouldNotBeNull();
            withRoomForAStep.Label.ShouldBe("🆕⏩ start 🔢 stop 🔢 step 🔢❗️");
            withRoomForAStep.ActiveParameter.ShouldBe(2);
        }

        [Fact]
        public void A_named_initializer_is_shown_by_its_name()
        {
            Signature("🆕🍨🐚🔢🍆▶️🐴 ‸")!.Label.ShouldBe("🆕🍨▶️🐴 capacity 🔢❗️");
        }

        [Fact]
        public void A_list_literal_is_one_argument_however_many_things_are_in_it()
        {
            Signature("🆕🔡 🍿 🔤a🔤 🔤b🔤 🔤c🔤 🍆 ‸")!.ActiveParameter.ShouldBe(1);
        }

        [Theory]
        [InlineData("📥 app 👍 ‸", 1)]
        [InlineData("📥 app 👇 ‸", 1)]
        [InlineData("🚀 app 🤜8000 ➕ 80🤛 ‸", 1)]
        [InlineData("📥 app 🤜🔤/a🔤🤛 handler ‸", 2)]
        public void An_argument_counts_whatever_it_is_written_with(string call, int active)
        {
            Signature(App + call)!.ActiveParameter.ShouldBe(active);
        }

        [Fact]
        public void A_call_on_something_unknown_is_one_argument_of_the_call_around_it()
        {
            var after = Signature(App + "📥 app 🛣 config❗️ ‸").ShouldNotBeNull();
            after.Label.ShouldStartWith("📥 app ");
            after.ActiveParameter.ShouldBe(1);

            var inside = Signature(App + "📥 app 🛣 config ‸").ShouldNotBeNull();
            inside.Label.ShouldStartWith("📥 app ");
            inside.ActiveParameter.ShouldBe(0);
        }

        [Fact]
        public void An_argument_still_being_typed_is_the_one_marked()
        {
            Signature(App + "📥 app 🔤/todos🔤 hand‸")!.ActiveParameter.ShouldBe(1);
        }

        [Fact]
        public void A_call_inside_a_block_written_on_one_line_shows_its_parameters()
        {
            Signature(App + "↪️ 👍 🍇 📥 app ‸")!.Label.ShouldStartWith("📥 app ");
        }

        [Theory]
        [InlineData("😀")]
        [InlineData("🍺")]
        [InlineData("🤜")]
        [InlineData("🍿")]
        [InlineData("🆕🍷 ")]
        [InlineData("a ➕ ")]
        public void Text_nested_far_deeper_than_anyone_writes_is_answered_and_not_followed_all_the_way_down(string repeated)
        {
            var text = App + string.Concat(Enumerable.Repeat(repeated, 60_000));

            Should.NotThrow(() => Intelligence.Signature(text, text.Length, TestPackages.All));
            Should.NotThrow(() => Intelligence.Hover(text, App.Length + 2, TestPackages.All));
            Should.NotThrow(() => Intelligence.Complete(text + " app.", text.Length + 5, TestPackages.All));
        }

        [Fact]
        public void Every_position_in_the_shipped_templates_gets_an_answer_without_a_failure()
        {
            var templates = Path.Combine(AppContext.BaseDirectory, "Emojicode", "Templates");
            var files = Directory.EnumerateFiles(templates, "*.🍇", SearchOption.AllDirectories).ToList();
            files.ShouldNotBeEmpty();

            foreach (var file in files)
            {
                var text = File.ReadAllText(file);
                foreach (var token in SourceReader.Read(text))
                {
                    Should.NotThrow(() => Intelligence.Signature(text, token.End, TestPackages.All), file);
                    Should.NotThrow(() => Intelligence.Hover(text, token.Start, TestPackages.All), file);
                }
            }
        }
    }
}
