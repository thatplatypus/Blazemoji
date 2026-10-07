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
    public sealed record OutputLine(long Number, OutputStream Stream, string Text);
}
