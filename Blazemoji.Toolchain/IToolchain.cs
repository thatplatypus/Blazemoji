namespace Blazemoji.Toolchain
{
    /// <summary>
    /// Compiles Emojicode sources and runs the result. The shape matches the HTTP service
    /// that will sit in front of it: compile returns a build id, a run is started from a
    /// build id, and a run is observed as a stream of events.
    /// </summary>
    public interface IToolchain
    {
        Task<CompileResult> CompileAsync(CompileRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts a run. A run that cannot be started still yields an <see cref="ExitEvent"/>
        /// with <see cref="RunEndReason.FailedToStart"/>; this method does not throw for that.
        /// </summary>
        Task<IToolchainRun> StartRunAsync(RunRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a build. Unknown ids are ignored.
        /// </summary>
        Task ReleaseBuildAsync(string buildId);
    }

    /// <summary>
    /// One execution of a build. Disposing it stops the program if it is still running.
    /// </summary>
    public interface IToolchainRun : IAsyncDisposable
    {
        string RunId { get; }

        /// <summary>
        /// Output events in arrival order, ending with exactly one <see cref="ExitEvent"/>.
        /// Supports a single reader.
        /// </summary>
        IAsyncEnumerable<RunEvent> ReadEventsAsync(CancellationToken cancellationToken = default);

        Task StopAsync();
    }
}
