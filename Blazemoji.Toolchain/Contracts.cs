namespace Blazemoji.Toolchain
{
    /// <param name="Files">Source files by relative name, for example <c>main.🍇</c>.</param>
    /// <param name="Entry">The name in <paramref name="Files"/> to hand to the compiler.</param>
    /// <param name="CheckOnly">
    /// Compile for the diagnostics alone: nothing is linked, no build is kept, and the result
    /// has no build id. This is what an editor asks for while someone is typing.
    /// </param>
    public sealed record CompileRequest(IReadOnlyDictionary<string, string> Files, string Entry, bool CheckOnly = false);

    /// <param name="BuildId">Set only when <paramref name="Ok"/> is true.</param>
    public sealed record CompileResult(bool Ok, IReadOnlyList<Diagnostic> Diagnostics, string? BuildId);

    /// <param name="Line">1-based. 0 when the compiler gave no location.</param>
    /// <param name="Character">1-based, counted in Unicode code points. 0 when unknown.</param>
    public sealed record Diagnostic(DiagnosticSeverity Severity, string File, int Line, int Character, string Message);

    public enum DiagnosticSeverity
    {
        Error,
        Warning,
    }

    /// <param name="Timeout">Overrides the toolchain's own time limit for this run, where the toolchain allows it.</param>
    /// <param name="Server">
    /// The program is a server. It is told which port to listen on through the <c>PORT</c>
    /// environment variable, has no wall-clock limit, and is ended once it has gone the
    /// toolchain's idle time without a request through <see cref="IToolchainRun.SendHttpAsync"/>.
    /// </param>
    public sealed record RunRequest(
        string BuildId,
        IReadOnlyDictionary<string, string>? Environment = null,
        TimeSpan? Timeout = null,
        bool Server = false);

    public abstract record RunEvent;

    public sealed record StdoutEvent(string Text) : RunEvent;

    public sealed record StderrEvent(string Text) : RunEvent;

    /// <param name="ExitCode">Null when the process never started.</param>
    public sealed record ExitEvent(int? ExitCode, RunEndReason Reason, TimeSpan Duration) : RunEvent;

    public enum RunEndReason
    {
        Exited,
        Stopped,
        TimedOut,
        OutputLimit,
        FailedToStart,
        Idle,
    }

    /// <summary>An HTTP request for a running server program.</summary>
    /// <param name="Path">Starts with a slash and may carry a query string.</param>
    /// <param name="Headers">In order, and a name may repeat.</param>
    public sealed record ProgramRequest(
        string Method,
        string Path,
        IReadOnlyList<KeyValuePair<string, string>> Headers,
        byte[] Body);

    /// <summary>
    /// What came of a <see cref="ProgramRequest"/>. The status, headers and body are the
    /// program's own and are only meaningful when <paramref name="Outcome"/> is
    /// <see cref="ProgramResponseOutcome.Answered"/>.
    /// </summary>
    public sealed record ProgramResponse(
        ProgramResponseOutcome Outcome,
        int StatusCode,
        string? ReasonPhrase,
        IReadOnlyList<KeyValuePair<string, string>> Headers,
        byte[] Body,
        TimeSpan Duration)
    {
        public static ProgramResponse Without(ProgramResponseOutcome outcome, TimeSpan duration = default) =>
            new(outcome, 0, null, [], [], duration);
    }

    public enum ProgramResponseOutcome
    {
        /// <summary>The program sent a response.</summary>
        Answered,

        /// <summary>The run was not started as a server.</summary>
        NotAServer,

        /// <summary>The run has ended.</summary>
        Ended,

        /// <summary>Nothing accepted a connection on the program's port. Usual while it starts.</summary>
        NotListening,

        /// <summary>The program closed the connection, sent something that is not HTTP, or sent too much.</summary>
        BadResponse,

        /// <summary>The request body is over the limit.</summary>
        TooLarge,

        /// <summary>The program did not answer in time.</summary>
        TimedOut,

        /// <summary>The method, path or a header could not be sent as HTTP.</summary>
        InvalidRequest,

        /// <summary>The toolchain itself could not be reached.</summary>
        Unavailable,
    }
}
