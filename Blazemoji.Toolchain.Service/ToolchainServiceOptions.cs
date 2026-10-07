namespace Blazemoji.Toolchain.Service
{
    public sealed class ToolchainServiceOptions
    {
        public const string SectionName = "ToolchainService";

        /// <summary>
        /// Programs that may be running at once. A request beyond this is refused, not queued.
        /// </summary>
        public int MaxConcurrentRuns { get; set; } = 8;

        /// <summary>
        /// Compiles that may be running at once. A request beyond this is refused, not queued.
        /// </summary>
        public int MaxConcurrentCompiles { get; set; } = 4;

        /// <summary>
        /// Finished runs kept readable at once. Beyond this the one that ended longest ago is dropped.
        /// </summary>
        public int MaxRetainedRuns { get; set; } = 32;

        /// <summary>
        /// Roughly how much memory the output of finished runs may hold together. Beyond this
        /// the run that ended longest ago is dropped. The run that ended last is always kept.
        /// </summary>
        public long MaxRetainedBytes { get; set; } = 64L * 1024 * 1024;

        /// <summary>
        /// The largest request body the service reads, for any request.
        /// </summary>
        public long MaxRequestBodyBytes { get; set; } = 4L * 1024 * 1024;

        /// <summary>
        /// How long a finished run and its output stay readable.
        /// </summary>
        public TimeSpan RunRetention { get; set; } = TimeSpan.FromMinutes(2);

        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// The largest request body passed on to a server program.
        /// </summary>
        public long MaxProxiedRequestBytes { get; set; } = 1024 * 1024;

        /// <summary>
        /// One folder per package, each holding the compiler's <c>documentation.json</c>.
        /// </summary>
        public string PackageDocumentationPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "package-docs");
    }
}
