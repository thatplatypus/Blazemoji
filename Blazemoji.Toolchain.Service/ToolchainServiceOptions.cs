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
        /// How long a finished run and its output stay readable.
        /// </summary>
        public TimeSpan RunRetention { get; set; } = TimeSpan.FromMinutes(2);

        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// One folder per package, each holding the compiler's <c>documentation.json</c>.
        /// </summary>
        public string PackageDocumentationPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "package-docs");
    }
}
