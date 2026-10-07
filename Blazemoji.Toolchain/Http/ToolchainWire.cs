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

    public sealed record StartRunBody(string? BuildId, Dictionary<string, string>? Env = null);

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
            _ => "failedToStart",
        };

        public static RunEndReason Parse(string name) => name switch
        {
            "exited" => RunEndReason.Exited,
            "stopped" => RunEndReason.Stopped,
            "timedOut" => RunEndReason.TimedOut,
            "outputLimit" => RunEndReason.OutputLimit,
            _ => RunEndReason.FailedToStart,
        };
    }
}
