using Blazemoji.Toolchain;

namespace Blazemoji.Test
{
    public class DiagnosticPositionsTests
    {
        private static Diagnostic At(int line, int character) =>
            new(DiagnosticSeverity.Error, "main.🍇", line, character, "message");

        [Fact]
        public void An_emoji_before_the_name_shifts_the_column_by_two()
        {
            const string source = "🏁 🍇\n  😀 nope❗️\n🍉";

            DiagnosticPositions.ToEditorRange(At(2, 5), source).ShouldBe(new EditorRange(2, 6, 2, 10));
        }

        [Fact]
        public void A_name_at_the_start_of_a_line_starts_at_column_one()
        {
            DiagnosticPositions.ToEditorRange(At(1, 1), "nope").ShouldBe(new EditorRange(1, 1, 1, 5));
        }

        [Fact]
        public void Each_emoji_before_the_name_counts_as_one_character_and_two_columns()
        {
            DiagnosticPositions.ToEditorRange(At(1, 4), "😀😀 nope").ShouldBe(new EditorRange(1, 6, 1, 10));
        }

        [Fact]
        public void A_variation_selector_is_its_own_character_and_one_column()
        {
            DiagnosticPositions.ToEditorRange(At(1, 4), "▶️ nope").ShouldBe(new EditorRange(1, 4, 1, 8));
        }

        [Fact]
        public void An_emoji_at_the_position_is_marked_on_its_own()
        {
            DiagnosticPositions.ToEditorRange(At(1, 1), "🍺 value").ShouldBe(new EditorRange(1, 1, 1, 3));
        }

        [Fact]
        public void An_emoji_with_a_variation_selector_is_marked_whole()
        {
            DiagnosticPositions.ToEditorRange(At(1, 1), "❗️ value").ShouldBe(new EditorRange(1, 1, 1, 3));
        }

        [Fact]
        public void A_name_stops_at_the_emoji_that_follows_it()
        {
            DiagnosticPositions.ToEditorRange(At(1, 1), "nope❗️").ShouldBe(new EditorRange(1, 1, 1, 5));
        }

        [Fact]
        public void Character_zero_means_the_start_of_the_line()
        {
            DiagnosticPositions.ToEditorRange(At(1, 0), "nope here").ShouldBe(new EditorRange(1, 1, 1, 5));
        }

        [Fact]
        public void A_line_past_the_end_is_clamped_to_the_last_line()
        {
            DiagnosticPositions.ToEditorRange(At(5, 0), "a\nb").ShouldBe(new EditorRange(2, 1, 2, 2));
        }

        [Fact]
        public void A_character_past_the_end_marks_the_last_column_of_the_line()
        {
            DiagnosticPositions.ToEditorRange(At(1, 10), "abc").ShouldBe(new EditorRange(1, 3, 1, 4));
        }

        [Fact]
        public void An_empty_line_gets_an_empty_range_at_its_start()
        {
            DiagnosticPositions.ToEditorRange(At(2, 1), "a\n\nb").ShouldBe(new EditorRange(2, 1, 2, 1));
        }

        [Fact]
        public void A_diagnostic_without_a_line_has_no_range()
        {
            DiagnosticPositions.ToEditorRange(At(0, 0), "nope").ShouldBeNull();
        }

        [Fact]
        public void Carriage_returns_are_not_part_of_the_line()
        {
            const string source = "🏁 🍇\r\n  😀 nope\r\n🍉";

            DiagnosticPositions.ToEditorRange(At(2, 5), source).ShouldBe(new EditorRange(2, 6, 2, 10));
        }

        [Fact]
        public void Half_a_surrogate_pair_in_the_source_counts_as_one_character()
        {
            DiagnosticPositions.ToEditorRange(At(1, 3), "\uD83D nope").ShouldBe(new EditorRange(1, 3, 1, 7));
        }

        [Fact]
        public void Whitespace_at_the_position_is_marked_one_column_wide()
        {
            DiagnosticPositions.ToEditorRange(At(1, 2), "a b").ShouldBe(new EditorRange(1, 2, 1, 3));
        }
    }
}
