using System.Text;
using System.Text.Json;
using Blazemoji.Toolchain;

namespace Blazemoji.Shared.State
{
    /// <summary>One request sent to the running program, and what came of it.</summary>
    /// <param name="Headers">As typed: one <c>Name: value</c> per line.</param>
    public sealed record Exchange(int Number, string Method, string Path, string Headers, string Body, ProgramResponse Response);

    /// <summary>
    /// Owns the requests sent to a running server program in this session and their answers.
    /// The run itself belongs to <see cref="RunState"/>.
    /// </summary>
    public sealed class RequestState(RunState runState)
    {
        public const int MaxExchanges = 10;

        private readonly List<Exchange> _exchanges = [];
        private int _sent;

        public event Action? StateChanged;

        /// <summary>Newest first.</summary>
        public IReadOnlyList<Exchange> Exchanges => _exchanges;

        public Exchange? Selected { get; private set; }

        public bool Sending { get; private set; }

        /// <returns>Why the request could not be made, or null once it has been answered or has failed.</returns>
        public async Task<string?> SendAsync(string method, string path, string headers, string body, CancellationToken cancellationToken = default)
        {
            var cleanMethod = method.Trim().ToUpperInvariant();
            if (cleanMethod.Length == 0)
                return "Choose a method.";

            var cleanPath = path.Trim();
            if (cleanPath.Length == 0)
                return "Give a path, for example /todos.";

            if (!cleanPath.StartsWith('/'))
                cleanPath = "/" + cleanPath;

            if (ParseHeaders(headers) is not { } parsedHeaders)
                return "Write each header on its own line as Name: value.";

            Sending = true;
            NotifyStateChanged();

            try
            {
                var request = new ProgramRequest(cleanMethod, cleanPath, parsedHeaders, Encoding.UTF8.GetBytes(body));
                var response = await runState.SendHttpAsync(request, cancellationToken);

                var exchange = new Exchange(++_sent, cleanMethod, cleanPath, headers, body, response);
                _exchanges.Insert(0, exchange);
                if (_exchanges.Count > MaxExchanges)
                    _exchanges.RemoveRange(MaxExchanges, _exchanges.Count - MaxExchanges);

                Selected = exchange;
                return null;
            }
            finally
            {
                Sending = false;
                NotifyStateChanged();
            }
        }

        public void Select(int number)
        {
            if (_exchanges.FirstOrDefault(exchange => exchange.Number == number) is not { } exchange || exchange == Selected)
                return;

            Selected = exchange;
            NotifyStateChanged();
        }

        /// <summary>
        /// What to tell someone whose request the program did not answer. Null when it did.
        /// </summary>
        public static string? Describe(ProgramResponseOutcome outcome) => outcome switch
        {
            ProgramResponseOutcome.Answered => null,
            ProgramResponseOutcome.NotListening => "The program is not accepting connections yet. Give it a moment and send again.",
            ProgramResponseOutcome.Ended => "The program is not running. Run it, then send again.",
            ProgramResponseOutcome.NotAServer => "This program was not started as a web server. Set the project to run as a web server and run it again.",
            ProgramResponseOutcome.TooLarge => "The request body is too large to send.",
            ProgramResponseOutcome.TimedOut => "The program did not answer in time.",
            ProgramResponseOutcome.InvalidRequest => "The method, path or a header cannot be sent as HTTP.",
            ProgramResponseOutcome.Unavailable => "The toolchain service could not be reached.",
            _ => "The program closed the connection or sent something that is not HTTP.",
        };

        /// <summary>
        /// The usual words for a status code, for a program that sent the number alone.
        /// </summary>
        public static string ReasonPhrase(int statusCode) => statusCode switch
        {
            200 => "OK",
            201 => "Created",
            202 => "Accepted",
            204 => "No Content",
            301 => "Moved Permanently",
            302 => "Found",
            303 => "See Other",
            304 => "Not Modified",
            307 => "Temporary Redirect",
            308 => "Permanent Redirect",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            406 => "Not Acceptable",
            408 => "Request Timeout",
            409 => "Conflict",
            410 => "Gone",
            411 => "Length Required",
            413 => "Content Too Large",
            414 => "URI Too Long",
            415 => "Unsupported Media Type",
            418 => "I'm a teapot",
            422 => "Unprocessable Content",
            426 => "Upgrade Required",
            429 => "Too Many Requests",
            431 => "Request Header Fields Too Large",
            500 => "Internal Server Error",
            501 => "Not Implemented",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            504 => "Gateway Timeout",
            505 => "HTTP Version Not Supported",
            _ => string.Empty,
        };

        /// <summary>
        /// The body of an answer as text to show: JSON laid out over several lines, other text
        /// as it is, and a description in place of anything that is not text.
        /// </summary>
        public static string BodyText(ProgramResponse response)
        {
            if (response.Body.Length == 0)
                return string.Empty;

            string text;
            try
            {
                text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(response.Body);
            }
            catch (DecoderFallbackException)
            {
                return $"{response.Body.Length} bytes that are not text.";
            }

            if (text.Contains('\0'))
                return $"{response.Body.Length} bytes that are not text.";

            var contentType = response.Headers.FirstOrDefault(header => header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                return text;

            try
            {
                using var document = JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                return text;
            }

            return Indent(text);
        }

        /// <summary>
        /// Lays valid JSON out over several lines without rewriting what its strings contain.
        /// Serialising it again would turn every emoji into a pair of escapes, which is the one
        /// thing a person reading an Emojicode program's output does not want.
        /// </summary>
        private static string Indent(string json)
        {
            var result = new StringBuilder(json.Length * 2);
            var depth = 0;
            var inString = false;

            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];
                if (inString)
                {
                    result.Append(c);
                    if (c == '\\' && i + 1 < json.Length)
                        result.Append(json[++i]);
                    else if (c == '"')
                        inString = false;

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        result.Append(c);
                        break;

                    case '{' or '[':
                        var close = NextSignificant(json, i + 1);
                        if (close < json.Length && json[close] is '}' or ']')
                        {
                            result.Append(c).Append(json[close]);
                            i = close;
                        }
                        else
                        {
                            result.Append(c);
                            NewLine(result, ++depth);
                        }

                        break;

                    case '}' or ']':
                        NewLine(result, --depth);
                        result.Append(c);
                        break;

                    case ',':
                        result.Append(c);
                        NewLine(result, depth);
                        break;

                    case ':':
                        result.Append(": ");
                        break;

                    default:
                        if (!char.IsWhiteSpace(c))
                            result.Append(c);
                        break;
                }
            }

            return result.ToString();
        }

        private static int NextSignificant(string json, int from)
        {
            while (from < json.Length && char.IsWhiteSpace(json[from]))
                from++;

            return from;
        }

        private static void NewLine(StringBuilder result, int depth) =>
            result.Append('\n').Append(' ', Math.Max(depth, 0) * 2);

        private static List<KeyValuePair<string, string>>? ParseHeaders(string text)
        {
            var headers = new List<KeyValuePair<string, string>>();
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;

                var colon = trimmed.IndexOf(':');
                if (colon <= 0)
                    return null;

                headers.Add(new(trimmed[..colon].Trim(), trimmed[(colon + 1)..].Trim()));
            }

            return headers;
        }

        private void NotifyStateChanged() => StateChanged?.Invoke();
    }
}
