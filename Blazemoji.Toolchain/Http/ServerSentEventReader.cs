using System.Runtime.CompilerServices;
using System.Text;

namespace Blazemoji.Toolchain.Http
{
    public sealed record ServerSentEvent(string? Id, string Name, string Data);

    /// <summary>
    /// Reads a <c>text/event-stream</c> body one event at a time.
    /// </summary>
    public static class ServerSentEventReader
    {
        public static async IAsyncEnumerable<ServerSentEvent> ReadAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            string? id = null;
            string? name = null;
            StringBuilder? data = null;

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (line.Length == 0)
                {
                    if (name is not null || data is not null)
                        yield return new ServerSentEvent(id, name ?? "message", data?.ToString() ?? string.Empty);

                    id = null;
                    name = null;
                    data = null;
                    continue;
                }

                if (line[0] == ':')
                    continue;

                var colon = line.IndexOf(':');
                var field = colon < 0 ? line : line[..colon];
                var value = colon < 0 ? string.Empty : line[(colon + 1)..];
                if (value.StartsWith(' '))
                    value = value[1..];

                switch (field)
                {
                    case "id":
                        id = value;
                        break;
                    case "event":
                        name = value;
                        break;
                    case "data":
                        data = data is null ? new StringBuilder(value) : data.Append('\n').Append(value);
                        break;
                }
            }
        }
    }
}
