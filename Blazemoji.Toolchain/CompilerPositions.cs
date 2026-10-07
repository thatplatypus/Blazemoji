namespace Blazemoji.Toolchain
{
    /// <summary>
    /// The compiler counts characters from zero on the first line of a file and from one on
    /// every later line (its lexer starts at character 0 and only reaches 1 after a newline).
    /// Diagnostics are normalised so that every line counts from one.
    /// </summary>
    public static class CompilerPositions
    {
        public static Diagnostic Normalize(Diagnostic diagnostic) =>
            diagnostic.Line == 1 ? diagnostic with { Character = diagnostic.Character + 1 } : diagnostic;
    }
}
