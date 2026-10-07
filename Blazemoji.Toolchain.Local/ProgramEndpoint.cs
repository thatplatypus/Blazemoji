using System.Diagnostics;
using System.Net;
using System.Text;
using Blazemoji.Toolchain.Http;

namespace Blazemoji.Toolchain.Local
{
    /// <summary>
    /// Talks HTTP to a server program on the loopback port it was told to listen on. Each run
    /// has its own, so a connection kept alive to one program is never offered to the next
    /// program that happens to be given the same port.
    /// </summary>
    internal sealed class ProgramEndpoint(int port, TimeSpan responseTimeout, long maxResponseBytes) : IDisposable
    {
        private readonly HttpMessageInvoker _http = new(new SocketsHttpHandler
        {
            UseProxy = false,
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8,
            ResponseHeaderEncodingSelector = (_, _) => Encoding.UTF8,
        });

        public int Port => port;

        /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
        public async Task<ProgramResponse> SendAsync(ProgramRequest request, CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();

            // The path always follows a slash, so nothing in it can be read as the host.
            using var message = ProgramHttp.CreateMessage(
                request,
                path => Uri.TryCreate($"http://127.0.0.1:{port}{path}", UriKind.Absolute, out var uri) ? uri : null);
            if (message is null)
                return ProgramResponse.Without(ProgramResponseOutcome.InvalidRequest);

            message.Version = HttpVersion.Version11;
            message.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(responseTimeout);

            try
            {
                using var response = await _http.SendAsync(message, deadline.Token);

                var body = await ReadBodyAsync(response, deadline.Token);
                if (body is null)
                    return ProgramResponse.Without(ProgramResponseOutcome.BadResponse, clock.Elapsed);

                return new ProgramResponse(
                    ProgramResponseOutcome.Answered,
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    ProgramHttp.ReadHeaders(response),
                    body,
                    clock.Elapsed);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ProgramResponse.Without(ProgramResponseOutcome.TimedOut, clock.Elapsed);
            }
            catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.ConnectionError)
            {
                return ProgramResponse.Without(ProgramResponseOutcome.NotListening, clock.Elapsed);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                return ProgramResponse.Without(ProgramResponseOutcome.BadResponse, clock.Elapsed);
            }
        }

        public void Dispose() => _http.Dispose();

        private async Task<byte[]?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var body = new MemoryStream();
            var buffer = new byte[16 * 1024];

            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (body.Length + read > maxResponseBytes)
                    return null;

                body.Write(buffer, 0, read);
            }

            return body.ToArray();
        }
    }
}
