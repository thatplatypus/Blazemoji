namespace Blazemoji.Toolchain.Packmoji
{
    /// <param name="WorkingDirectory">The project's folder. It has to exist.</param>
    /// <param name="Timeout">Null takes <see cref="PackmojiOptions.Timeout"/>.</param>
    /// <param name="Environment">Variables pmj is given on top of this process's own. One here wins over <see cref="PackmojiOptions"/>.</param>
    public sealed record PmjCommand(
        string WorkingDirectory,
        IReadOnlyList<string> Arguments,
        TimeSpan? Timeout = null,
        IReadOnlyDictionary<string, string>? Environment = null);

    public enum PmjOutcome
    {
        /// <summary>pmj ran to its end. Its exit status says how.</summary>
        Ran,

        /// <summary>There is no pmj to run.</summary>
        NotFound,

        /// <summary>pmj was ended because it ran out of time.</summary>
        TimedOut,
    }

    public sealed record PmjResult(PmjOutcome Outcome, int ExitCode, string Stdout, string Stderr);

    /// <summary>
    /// Runs Packmoji's command. Everything Blazemoji asks of Packmoji goes through here, so a
    /// test can stand in for the program and a later pmj changes one class.
    /// </summary>
    public interface IPmj
    {
        /// <summary>
        /// Runs one command to its end. That pmj is missing, or ran out of time, is an outcome
        /// and not an exception. Only the caller's own cancellation throws.
        /// </summary>
        Task<PmjResult> RunAsync(PmjCommand command, CancellationToken cancellationToken = default);
    }

    /// <summary>The environment variables pmj reads, as its guide to the command line names them.</summary>
    public static class PmjVariables
    {
        public const string Home = "PACKMOJI_HOME";
        public const string GitHub = "PACKMOJI_GITHUB";
        public const string GitHubApi = "PACKMOJI_GITHUB_API";
        public const string Compiler = "EMOJICODEC";
        public const string CompilerPackages = "EMOJICODE_PACKAGES_PATH";
        public const string CompilerInclude = "EMOJICODE_INCLUDE";
        public const string Cxx = "CXX";

        public static IReadOnlyList<string> Tokens { get; } = ["GITHUB_TOKEN", "GH_TOKEN"];
    }
}
