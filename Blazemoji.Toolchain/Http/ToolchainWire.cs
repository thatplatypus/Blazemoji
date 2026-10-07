using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blazemoji.Toolchain.Http
{
    /// <summary>
    /// The toolchain contract as it appears on the wire. The service and the client both use
    /// these types, so the two cannot drift apart.
    /// </summary>
    public static class ToolchainRoutes
    {
        public const string Compile = "/compile";
        public const string Runs = "/runs";
        public const string Packages = "/packages";
        public const string Health = "/health";

        public static string Run(string runId) => $"/runs/{Uri.EscapeDataString(runId)}";

        public static string RunEvents(string runId) => $"{Run(runId)}/events";

        /// <param name="path">Starts with a slash and may carry a query string.</param>
        public static string RunHttp(string runId, string path) => $"{Run(runId)}/http{path}";

        public static string RunInput(string runId, bool endOfInput) =>
            $"{Run(runId)}/stdin{(endOfInput ? "?eof=true" : string.Empty)}";

        public static string PackageDocumentation(string name) =>
            $"/packages/{Uri.EscapeDataString(name)}/documentation.json";
    }

    public static class ToolchainJson
    {
        /// <summary>
        /// For request and response bodies. The relaxed encoder leaves quotes-free punctuation and
        /// most symbols alone. Emoji outside the Basic Multilingual Plane are still written as
        /// escapes; System.Text.Json offers no way to change that.
        /// </summary>
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// For event payloads, where a null (an unknown exit code) is part of the event.
        /// </summary>
        public static readonly JsonSerializerOptions EventOptions = new(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }

    public static class EventNames
    {
        public const string Stdout = "stdout";
        public const string Stderr = "stderr";
        public const string Exit = "exit";
    }

    public sealed record CompileRequestBody(Dictionary<string, string>? Files, string? Entry, string[]? Packages = null);

    public sealed record CompileResponseBody(bool Ok, IReadOnlyList<DiagnosticBody> Diagnostics, string? BuildId);

    /// <param name="Severity"><c>error</c> or <c>warning</c>.</param>
    public sealed record DiagnosticBody(string Severity, string File, int Line, int Character, string Message)
    {
        public const string Error = "error";
        public const string Warning = "warning";

        public static DiagnosticBody From(Diagnostic diagnostic) => new(
            diagnostic.Severity == DiagnosticSeverity.Warning ? Warning : Error,
            diagnostic.File,
            diagnostic.Line,
            diagnostic.Character,
            diagnostic.Message);

        public Diagnostic ToDiagnostic() => new(
            Severity == Warning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            File,
            Line,
            Character,
            Message);
    }

    /// <param name="Http">Start the run as a server: see <see cref="RunRequest.Server"/>.</param>
    public sealed record StartRunBody(string? BuildId, Dictionary<string, string>? Env = null, bool Http = false);

    public sealed record RunStartedBody(string RunId);

    public sealed record OutputEventData(string Text);

    /// <param name="Reason">One of the <see cref="RunEndReasons"/> names.</param>
    public sealed record ExitEventData(int? ExitCode, string Reason, long DurationMs);

    public static class RunEndReasons
    {
        public static string Name(RunEndReason reason) => reason switch
        {
            RunEndReason.Exited => "exited",
            RunEndReason.Stopped => "stopped",
            RunEndReason.TimedOut => "timedOut",
            RunEndReason.OutputLimit => "outputLimit",
            RunEndReason.Idle => "idle",
            _ => "failedToStart",
        };

        public static RunEndReason Parse(string name) => name switch
        {
            "exited" => RunEndReason.Exited,
            "stopped" => RunEndReason.Stopped,
            "timedOut" => RunEndReason.TimedOut,
            "outputLimit" => RunEndReason.OutputLimit,
            "idle" => RunEndReason.Idle,
            _ => RunEndReason.FailedToStart,
        };
    }

    /// <summary>
    /// On the route that passes requests on to a server program, a response that does not come
    /// from the program carries <see cref="Header"/> with one of these reasons, so that the
    /// program's own 404 or 502 can be told from the service's.
    /// </summary>
    public static class ProxyReasons
    {
        public const string Header = "X-Toolchain-Proxy";

        public const string UnknownRun = "unknown-run";

        public static string Name(ProgramResponseOutcome outcome) => outcome switch
        {
            ProgramResponseOutcome.NotAServer => "not-a-server",
            ProgramResponseOutcome.Ended => "ended",
            ProgramResponseOutcome.NotListening => "not-listening",
            ProgramResponseOutcome.TooLarge => "too-large",
            ProgramResponseOutcome.TimedOut => "timed-out",
            ProgramResponseOutcome.InvalidRequest => "invalid-request",
            _ => "bad-response",
        };

        /// <summary>A run the service no longer knows has ended, as far as a caller is concerned.</summary>
        public static ProgramResponseOutcome Parse(string name) => name switch
        {
            "not-a-server" => ProgramResponseOutcome.NotAServer,
            "ended" or UnknownRun => ProgramResponseOutcome.Ended,
            "not-listening" => ProgramResponseOutcome.NotListening,
            "too-large" => ProgramResponseOutcome.TooLarge,
            "timed-out" => ProgramResponseOutcome.TimedOut,
            "invalid-request" => ProgramResponseOutcome.InvalidRequest,
            _ => ProgramResponseOutcome.BadResponse,
        };
    }
}
