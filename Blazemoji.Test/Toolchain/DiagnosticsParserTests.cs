using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Local;

namespace Blazemoji.Test.Toolchain
{
    public class DiagnosticsParserTests
    {
        [Fact]
        public void An_empty_array_means_no_diagnostics()
        {
            DiagnosticsParser.Parse("[]").ShouldBeEmpty();
        }

        [Fact]
        public void An_error_keeps_its_position_file_and_message()
        {
            var diagnostics = DiagnosticsParser.Parse(
                """[{"type":"error","line":2,"character":5,"file":"a.🍇","message":"Variable \"nope\" not defined."}]""");

            diagnostics.ShouldHaveSingleItem().ShouldBe(
                new Diagnostic(DiagnosticSeverity.Error, "a.🍇", 2, 5, "Variable \"nope\" not defined."));
        }

        [Fact]
        public void A_warning_without_a_location_is_kept()
        {
            var diagnostics = DiagnosticsParser.Parse(
                """[{"type":"warning","line":0,"character":0,"file":"","message":"Run-time type information for multiprotocols, callables and type values is not available yet. Casts and other reflection may not behave as expected with these types."}]""");

            var warning = diagnostics.ShouldHaveSingleItem();
            warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
            warning.File.ShouldBeEmpty();
            warning.Line.ShouldBe(0);
            warning.Character.ShouldBe(0);
            warning.Message.ShouldStartWith("Run-time type information");
        }

        [Fact]
        public void Several_diagnostics_keep_their_order()
        {
            var diagnostics = DiagnosticsParser.Parse(
                """[{"type":"error","line":3,"character":1,"file":"b.🍇","message":"Unexpected token BlockEnd."},{"type":"error","line":4,"character":0,"file":"b.🍇","message":"Unexpected end of program."}]""");

            diagnostics.Select(d => (d.Line, d.Character, d.Message)).ShouldBe(
            [
                (3, 1, "Unexpected token BlockEnd."),
                (4, 0, "Unexpected end of program."),
            ]);
        }

        [Fact]
        public void An_unknown_type_is_treated_as_an_error()
        {
            var diagnostics = DiagnosticsParser.Parse(
                """[{"type":"note","line":1,"character":1,"file":"a.🍇","message":"Something new."}]""");

            diagnostics.ShouldHaveSingleItem().Severity.ShouldBe(DiagnosticSeverity.Error);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData("")]
        [InlineData("[1]")]
        public void Anything_other_than_an_array_of_diagnostics_is_rejected(string compilerOutput)
        {
            Should.Throw<FormatException>(() => DiagnosticsParser.Parse(compilerOutput));
        }
    }
}
