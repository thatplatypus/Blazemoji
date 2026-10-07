namespace Blazemoji.Toolchain.Local
{
    public sealed class ToolchainOptions
    {
        public const string SectionName = "Toolchain";

        public string CompilerPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "emojicodec", "emojicodec");

        /// <summary>
        /// The C++ compiler driver used to link a program. The toolchain links programs itself:
        /// see <see cref="LocalToolchain"/>.
        /// </summary>
        public string LinkerPath { get; set; } = "c++";

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
        /// The address space one program may have, in bytes. This counts what is reserved, not
        /// what is used: every thread's stack counts in full, and under amd64 emulation the
        /// emulator's own tables count too. A program with 64 threads was aborted by the emulator
        /// at 1 GiB, so the default leaves room. What a program can really use is bounded by the
        /// container's memory limit.
        /// </summary>
        public long MemoryBytes { get; set; } = 4L * 1024 * 1024 * 1024;

        /// <summary>
        /// Largest file one run may write.
        /// </summary>
        public long MaxFileBytes { get; set; } = 16L * 1024 * 1024;

        public int MaxOpenFiles { get; set; } = 256;

        /// <summary>
        /// A server run has no wall-clock limit. It is ended once it has gone this long without
        /// a request being sent to it.
        /// </summary>
        public TimeSpan ServerIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// The CPU limit for a server run, which lives much longer than a script.
        /// </summary>
        public int ServerCpuSeconds { get; set; } = 300;

        /// <summary>
        /// How long a server program has to answer one request.
        /// </summary>
        public TimeSpan ProgramResponseTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The largest response body accepted from a server program.
        /// </summary>
        public long MaxProgramResponseBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Builds that were never released are deleted once they are older than this.
        /// </summary>
        public TimeSpan BuildLifetime { get; set; } = TimeSpan.FromMinutes(10);
    }
}
