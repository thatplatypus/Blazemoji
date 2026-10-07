using System.Text;

namespace Blazemoji.Shared.State
{
    /// <summary>
    /// Turns chunks of program output into whole lines. A line longer than the limit is handed
    /// over in pieces so that a program that never prints a newline cannot grow memory without bound.
    /// </summary>
    internal sealed class OutputAssembler(int maxLineLength)
    {
        private readonly StringBuilder _pending = new();
        private bool _cutSinceLastNewline;

        public IReadOnlyList<string> Append(string chunk)
        {
            List<string>? lines = null;

            foreach (var character in chunk)
            {
                if (character == '\n')
                {
                    if (_pending.Length > 0 || !_cutSinceLastNewline)
                        (lines ??= []).Add(TakeLine());

                    _cutSinceLastNewline = false;
                    continue;
                }

                _pending.Append(character);
                if (_pending.Length >= maxLineLength && !char.IsHighSurrogate(character))
                {
                    (lines ??= []).Add(Take());
                    _cutSinceLastNewline = true;
                }
            }

            return lines ?? [];
        }

        /// <returns>Text that has not been ended by a newline, or null when there is none.</returns>
        public string? Flush()
        {
            _cutSinceLastNewline = false;
            return _pending.Length == 0 ? null : Take();
        }

        private string TakeLine()
        {
            if (_pending.Length > 0 && _pending[^1] == '\r')
                _pending.Length--;

            return Take();
        }

        private string Take()
        {
            var text = _pending.ToString();
            _pending.Clear();
            return text;
        }
    }
}
