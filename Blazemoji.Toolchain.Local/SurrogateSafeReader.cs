namespace Blazemoji.Toolchain.Local
{
    /// <summary>
    /// Reads text in chunks that never end between the two halves of a surrogate pair, so that
    /// each chunk is a valid string on its own. Emoji are surrogate pairs, and Emojicode output
    /// is full of them.
    /// </summary>
    internal sealed class SurrogateSafeReader(TextReader reader, int bufferSize = 4096)
    {
        private readonly char[] _buffer = new char[Math.Max(bufferSize, 2)];
        private char? _heldBack;

        /// <returns>The next chunk, or null at the end of the stream.</returns>
        public async Task<string?> ReadChunkAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var length = 0;
                if (_heldBack is { } held)
                {
                    _buffer[0] = held;
                    length = 1;
                    _heldBack = null;
                }

                var read = await reader.ReadAsync(_buffer.AsMemory(length), cancellationToken);
                length += read;

                if (read == 0)
                    return length == 0 ? null : new string(_buffer, 0, length);

                if (!char.IsHighSurrogate(_buffer[length - 1]))
                    return new string(_buffer, 0, length);

                _heldBack = _buffer[length - 1];
                length--;

                if (length > 0)
                    return new string(_buffer, 0, length);
            }
        }
    }
}
