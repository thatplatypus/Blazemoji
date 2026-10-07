using System.Text.Json;

namespace Blazemoji.Toolchain
{
    /// <summary>
    /// Reads what <c>emojicodec --json</c> prints on stdout.
    /// </summary>
    public static class DiagnosticsParser
    {
        /// <exception cref="FormatException">The text is not a JSON array of diagnostics.</exception>
        public static IReadOnlyList<Diagnostic> Parse(string compilerStdout)
        {
            try
            {
                using var document = JsonDocument.Parse(compilerStdout);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    throw new FormatException("Compiler output is not a JSON array.");

                var diagnostics = new List<Diagnostic>();
                foreach (var element in document.RootElement.EnumerateArray())
                    diagnostics.Add(ReadDiagnostic(element));

                return diagnostics;
            }
            catch (JsonException exception)
            {
                throw new FormatException("Compiler output is not valid JSON.", exception);
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
            {
                throw new FormatException("Compiler output is not an array of diagnostics.", exception);
            }
        }

        private static Diagnostic ReadDiagnostic(JsonElement element)
        {
            var severity = element.GetProperty("type").GetString() == "warning"
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error;

            return new Diagnostic(
                severity,
                element.GetProperty("file").GetString() ?? string.Empty,
                element.GetProperty("line").GetInt32(),
                element.GetProperty("character").GetInt32(),
                element.GetProperty("message").GetString() ?? string.Empty);
        }
    }
}
