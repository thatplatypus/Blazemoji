namespace Blazemoji.Toolchain.Http
{
    /// <summary>
    /// Turns a <see cref="ProgramRequest"/> into an HTTP request and an HTTP response into the
    /// parts of a <see cref="ProgramResponse"/>. Shared by whatever talks to the program
    /// itself and by the client of the service that does.
    /// </summary>
    public static class ProgramHttp
    {
        // Headers that describe one connection and mean nothing on the next one. Host and
        // Content-Length are left to the HTTP client, which knows the real values.
        private static readonly HashSet<string> NotForwarded = new(StringComparer.OrdinalIgnoreCase)
        {
            "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "Proxy-Connection",
            "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host", "Content-Length",
        };

        private static readonly HashSet<string> MethodsWithABody = new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH" };

        /// <summary>True for a header that belongs to one connection and is not passed along.</summary>
        public static bool IsHopByHop(string name) =>
            NotForwarded.Contains(name) && !IsContentLength(name) && !name.Equals("Host", StringComparison.OrdinalIgnoreCase);

        public static bool IsContentLength(string name) => name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);

        /// <summary>True for a header an HTTP message can carry: a token for a name, and a value without control characters.</summary>
        public static bool CanBeSentAsHeader(string name, string value) => IsToken(name) && !value.Any(char.IsControl);

        /// <param name="address">
        /// Given the request's path, with a leading slash and any query string, returns where
        /// to send it, or null to refuse.
        /// </param>
        /// <returns>Null when the method, path or a header cannot be sent as HTTP.</returns>
        public static HttpRequestMessage? CreateMessage(ProgramRequest request, Func<string, Uri?> address)
        {
            if (!IsToken(request.Method) || NormalizePath(request.Path) is not { } path || address(path) is not { } uri)
                return null;

            var content = new ByteArrayContent(request.Body);
            var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);

            var hasContentHeader = false;
            foreach (var (name, value) in request.Headers)
            {
                if (!CanBeSentAsHeader(name, value))
                {
                    content.Dispose();
                    message.Dispose();
                    return null;
                }

                if (NotForwarded.Contains(name) || message.Headers.TryAddWithoutValidation(name, value))
                    continue;

                hasContentHeader |= content.Headers.TryAddWithoutValidation(name, value);
            }

            if (request.Body.Length > 0 || hasContentHeader || MethodsWithABody.Contains(request.Method))
                message.Content = content;
            else
                content.Dispose();

            return message;
        }

        /// <summary>
        /// Every header of the response, one entry per value, without the ones that describe
        /// the connection it arrived on. Content-Length is kept: it is a fact about the response.
        /// </summary>
        public static List<KeyValuePair<string, string>> ReadHeaders(HttpResponseMessage response)
        {
            var headers = new List<KeyValuePair<string, string>>();
            foreach (var (name, values) in response.Headers.NonValidated.Concat(response.Content.Headers.NonValidated))
            {
                if (IsHopByHop(name))
                    continue;

                foreach (var value in values)
                    headers.Add(new(name, value));
            }

            return headers;
        }

        /// <summary>
        /// A path is refused if it has a control character, a backslash (which a URI parser
        /// reads as a slash) or a <c>.</c> or <c>..</c> segment, written plainly or
        /// percent-encoded. Such a path could otherwise be resolved to somewhere other than
        /// under the address it is appended to, or be turned away by the server before it
        /// reaches the program.
        /// </summary>
        private static string? NormalizePath(string path)
        {
            if (path.Any(char.IsControl) || path.Contains('\\'))
                return null;

            var withSlash = path.StartsWith('/') ? path : "/" + path;
            var queryStart = withSlash.IndexOfAny(['?', '#']);
            var pathOnly = queryStart < 0 ? withSlash : withSlash[..queryStart];

            foreach (var segment in pathOnly.Split('/'))
            {
                var decoded = Uri.UnescapeDataString(segment);
                if (decoded is "." or ".." || decoded.Contains('/') || decoded.Contains('\\') || decoded.Any(char.IsControl))
                    return null;
            }

            return withSlash;
        }

        /// <summary>An HTTP token: what a method or a header name may be made of.</summary>
        private static bool IsToken(string text) =>
            text.Length > 0 && text.All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c));
    }
}
