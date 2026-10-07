using Blazemoji.Toolchain;

namespace Blazemoji.Test.Toolchain
{
    public class CompilerPositionsTests
    {
        private static Diagnostic At(int line, int character) => new(DiagnosticSeverity.Error, "main.🍇", line, character, "message");

        [Fact]
        public void The_first_line_is_shifted_to_count_from_one_like_every_other_line()
        {
            CompilerPositions.Normalize(At(1, 6)).ShouldBe(At(1, 7));
            CompilerPositions.Normalize(At(1, 0)).ShouldBe(At(1, 1));
        }

        [Fact]
        public void Later_lines_are_left_alone()
        {
            CompilerPositions.Normalize(At(2, 5)).ShouldBe(At(2, 5));
            CompilerPositions.Normalize(At(4, 0)).ShouldBe(At(4, 0));
        }

        [Fact]
        public void A_diagnostic_without_a_location_is_left_alone()
        {
            CompilerPositions.Normalize(At(0, 0)).ShouldBe(At(0, 0));
        }
    }
}
