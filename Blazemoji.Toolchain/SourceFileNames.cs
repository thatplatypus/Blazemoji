using System.Text;

namespace Blazemoji.Toolchain
{
    /// <summary>
    /// Source file names come from the caller, are written to disk, and reach the compiler's
    /// command line. The compiler in turn builds its link command from the entry file's name
    /// and hands it to a shell. So a name is restricted to forward-slash relative paths that
    /// stay inside the build directory and whose characters mean nothing to a shell: ASCII
    /// letters, digits, dot, underscore and hyphen, plus any non-ASCII character that is not
    /// whitespace or a control (emoji names such as <c>🏛</c> are normal in Emojicode).
    /// </summary>
    public static class SourceFileNames
    {
        public static bool IsSafe(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            foreach (var segment in name.Split('/'))
            {
                if (!IsSafeSegment(segment))
                    return false;
            }

            return true;
        }

        private static bool IsSafeSegment(string segment)
        {
            // A leading hyphen would be read by the compiler as an option.
            if (segment.Length == 0 || segment == "." || segment == ".." || segment[0] == '-')
                return false;

            foreach (var rune in segment.EnumerateRunes())
            {
                var allowed = rune.IsAscii
                    ? Rune.IsLetterOrDigit(rune) || rune.Value is '.' or '_' or '-'
                    : rune != Rune.ReplacementChar && !Rune.IsWhiteSpace(rune) && !Rune.IsControl(rune);

                if (!allowed)
                    return false;
            }

            return true;
        }
    }
}
