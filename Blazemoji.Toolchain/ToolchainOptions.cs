namespace Blazemoji.Toolchain
{
    public sealed class ToolchainOptions
    {
        public const string SectionName = "Toolchain";

        public string CompilerPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "emojicodec", "emojicodec");

        /// <summary>
        /// Passed to the compiler as a package search path.
        /// </summary>
        public string PackagesPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "packages");

        /// <summary>
        /// Parent of every build and run directory.
        /// </summary>
        public string WorkRoot { get; set; } = Path.Combine(Path.GetTempPath(), "blazemoji");

        public TimeSpan CompileTimeout { get; set; } = TimeSpan.FromSeconds(60);

        public TimeSpan RunTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Combined stdout and stderr, in UTF-8 bytes, after which a run is ended.
        /// </summary>
        public long MaxOutputBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Processor time one run may use, in seconds. Applied with prlimit where it exists.
        /// </summary>
        public int CpuSeconds { get; set; } = 20;

        /// <summary>
        /// Address space one run may map.
        /// </summary>
        public long MemoryBytes { get; set; } = 1024L * 1024 * 1024;

        /// <summary>
        /// Largest file one run may write.
        /// </summary>
        public long MaxFileBytes { get; set; } = 16L * 1024 * 1024;

        public int MaxOpenFiles { get; set; } = 256;

        /// <summary>
        /// Builds that were never released are deleted once they are older than this.
        /// </summary>
        public TimeSpan BuildLifetime { get; set; } = TimeSpan.FromMinutes(10);
    }
}
