namespace Blazemoji.Shared.State
{
    public enum OutputStream
    {
        Stdout,
        Stderr,

        /// <summary>
        /// A message from Blazemoji itself, such as why a run was stopped.
        /// </summary>
        System,
    }

    /// <param name="Number">Position in the run's whole output, starting at 1. Stable, so it can key a rendered row.</param>
    /// <param name="Typed">What was typed for the program on this line, after whatever the program had printed on it.</param>
    public sealed record OutputLine(long Number, OutputStream Stream, string Text, string? Typed = null);
}
