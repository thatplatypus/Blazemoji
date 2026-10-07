namespace Blazemoji.Shared.State
{
    /// <summary>
    /// What to compile and how to run it.
    /// </summary>
    /// <param name="Files">Every source file by its path in the project.</param>
    /// <param name="Entry">The path in <paramref name="Files"/> handed to the compiler.</param>
    /// <param name="Server">The program listens for HTTP requests until it is stopped.</param>
    public sealed record RunTarget(IReadOnlyDictionary<string, string> Files, string Entry, bool Server);
}
