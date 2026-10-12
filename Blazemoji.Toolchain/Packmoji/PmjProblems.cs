namespace Blazemoji.Toolchain.Packmoji
{
    /// <summary>
    /// Which of the problems pmj reports someone who does not run this machine may read, and
    /// in what words. pmj reports two kinds. One is about the project's two files and the
    /// packages they name: its author's to read and to mend, and passed on. The other is
    /// about the machine pmj runs on: it names that machine's folders and carries a disk's or
    /// a socket's own words, and it is the log's.
    /// </summary>
    public static class PmjProblems
    {
        /// <summary>pmj's code for a lockfile that is missing, or that no longer answers the manifest.</summary>
        public const string LockOutOfDate = "lock.out-of-date";

        public const string CompilerSaidReason = "what the compiler said is in the toolchain's log";

        // pmj words this reason with the compiler's own path, which is not under any folder a
        // caller can name, so the words are fixed.
        public const string CompilerTooOldReason = "the compiler this toolchain has is older than that";

        public const string GitHubSaidReason = "what was asked of GitHub, and how that went, is in the log";

        private const string CompilerTooOldCode = "compiler.too-old";
        private const string GitHubPrefix = "github.";

        // The codes of the problems pmj finds in a project's two files and in the packages
        // they name. A code that is not here is about the machine or is not known.
        private static readonly string[] ProjectPrefixes =
        [
            "file.", "json.", "key.", "name.", "version.", "requirement.", "kind.", "description.",
            "license.", "repository.", "dependency.", "path.", "glob.", "link.", "list.", "entry.",
            "lock.", "pin.", "sha256.", "verified.", "resolve.", "release.", "archive.",
            "attestation.", "native.", "scope.",
        ];

        private static readonly string[] ProjectCodes = ["compiler.invalid", CompilerTooOldCode, "package.not-found"];

        private static readonly string[] CompilerCodes = ["build.compile-failed", "build.native-failed", "build.archive-failed", "build.link-failed"];

        /// <param name="places">Folders of this machine. A problem that names one is not passed on, whatever its code.</param>
        /// <param name="asksGitHub">
        /// Whether the pmj that reported it may ask GitHub. Where it may not, a problem with
        /// GitHub is this machine's fault and no news for the visitor.
        /// </param>
        /// <returns>The problem as it may be shown, or null when it is the log's alone.</returns>
        public static PackageProblem? ForAVisitor(PackageProblem problem, IEnumerable<string> places, bool asksGitHub)
        {
            PackageProblem shown;
            if (CompilerCodes.Contains(problem.Code))
                shown = problem with { Reason = CompilerSaidReason };
            else if (problem.Code == CompilerTooOldCode)
                shown = problem with { Reason = CompilerTooOldReason };
            else if (asksGitHub && problem.Code.StartsWith(GitHubPrefix, StringComparison.Ordinal))
                shown = problem with { Reason = GitHubSaidReason };
            else if (ProjectCodes.Contains(problem.Code) || ProjectPrefixes.Any(prefix => problem.Code.StartsWith(prefix, StringComparison.Ordinal)))
                shown = problem;
            else
                return null;

            return places.Where(place => !string.IsNullOrEmpty(place)).Any(place => Names(shown, place)) ? null : shown;
        }

        /// <param name="who">Who has no pmj, as the start of a sentence: "This toolchain", "This copy of Blazemoji".</param>
        public static PackageProblem NoPackmoji(string who) => new(
            PackageProblem.Error,
            PackageProblemCodes.NoPackmoji,
            $"{who} has no Packmoji.",
            "the pmj program is not where it is looked for",
            "a project with no packmoji.json needs none; for one that has, use a copy that came with pmj, or say where pmj is with Packmoji:Path");

        public static PackageProblem TimedOut() => new(
            PackageProblem.Error,
            PackageProblemCodes.TimedOut,
            "The project's packages took too long.",
            "pmj was stopped when it ran out of time",
            "try again: what was fetched or built is not fetched or built a second time");

        /// <param name="who">Whose log holds the reason: "the toolchain", "Blazemoji".</param>
        public static PackageProblem Unreadable(string who) => new(
            PackageProblem.Error,
            PackageProblemCodes.Unreadable,
            "The project's packages could not be worked out.",
            $"{who} could not use what Packmoji told it",
            $"try again; if it happens again, the reason is in {who}'s log");

        private static bool Names(PackageProblem problem, string place) =>
            problem.Message.Contains(place, StringComparison.Ordinal)
            || problem.Reason.Contains(place, StringComparison.Ordinal)
            || problem.Fix.Contains(place, StringComparison.Ordinal)
            || (problem.File?.Contains(place, StringComparison.Ordinal) ?? false);
    }
}
