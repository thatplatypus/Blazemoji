namespace Blazemoji.Toolchain
{
    /// <param name="Files">Source files by relative name, for example <c>main.🍇</c>.</param>
    /// <param name="Entry">The name in <paramref name="Files"/> to hand to the compiler.</param>
    public sealed record CompileRequest(IReadOnlyDictionary<string, string> Files, string Entry);

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
    public sealed record RunRequest(
        string BuildId,
        IReadOnlyDictionary<string, string>? Environment = null,
        TimeSpan? Timeout = null);

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
    }
}
