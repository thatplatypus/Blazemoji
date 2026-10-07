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

        /// <summary>
        /// Appends text to the program's standard input.
        /// </summary>
        /// <param name="endOfInput">Closes standard input after writing, so the program sees the end of its input.</param>
        /// <exception cref="InvalidOperationException">The run has ended or its input is already closed.</exception>
        Task WriteInputAsync(string text, bool endOfInput = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends an HTTP request to a server program and returns its whole response. Every way
        /// this can fail to produce a response is an outcome on the result, not an exception.
        /// </summary>
        Task<ProgramResponse> SendHttpAsync(ProgramRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Lets a host answer "does this build exist" without starting a run.
    /// </summary>
    public interface IBuildStore
    {
        bool HasBuild(string buildId);
    }
}
