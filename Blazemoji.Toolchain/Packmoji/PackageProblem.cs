namespace Blazemoji.Toolchain.Packmoji
{
    /// <summary>
    /// One thing wrong with a project's packages, in the parts pmj gives a problem: a code that
    /// never changes its meaning, what failed, why, and what to do next. Whoever shows it
    /// decides how, and can tell one kind from another by its code.
    /// </summary>
    /// <param name="Line">Counted from 1. 0 when the problem has no place.</param>
    /// <param name="Column">Counted from 1, in characters. 0 when the problem has no place.</param>
    public sealed record PackageProblem(
        string Severity,
        string Code,
        string Message,
        string Reason,
        string Fix,
        string? File = null,
        int Line = 0,
        int Column = 0)
    {
        public const string Error = "error";
        public const string Warning = "warning";

        public bool IsError => Severity != Warning;
    }

    /// <summary>The codes of the problems Blazemoji raises itself. Every other code is pmj's.</summary>
    public static class PackageProblemCodes
    {
        public const string NoPackmoji = "blazemoji.no-packmoji";
        public const string TimedOut = "blazemoji.timed-out";
        public const string Unreadable = "blazemoji.unreadable";
        public const string Scope = "blazemoji.scope";
        public const string TooLarge = "blazemoji.too-large";
    }
}
