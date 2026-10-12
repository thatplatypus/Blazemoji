namespace Blazemoji.Toolchain.Packmoji
{
    /// <summary>
    /// What whoever runs a host or the toolchain service says about Packmoji. The names are
    /// the same on both sides, so one set of variables configures two processes in one container.
    /// </summary>
    public sealed class PackmojiOptions
    {
        public const string SectionName = "Packmoji";

        /// <summary>The pmj program. By default the one that came beside the app.</summary>
        public string Path { get; set; } =
            System.IO.Path.Combine(AppContext.BaseDirectory, "pmj", OperatingSystem.IsWindows() ? "pmj.exe" : "pmj");

        /// <summary>The folder pmj keeps its cache and its built packages in. Null leaves it to whoever reads this.</summary>
        public string? Home { get; set; }

        /// <summary>
        /// The scopes whose packages are fetched and built, separated by commas, or <c>*</c> for
        /// any. Null is no scope at all, so whoever runs this says whose packages it takes.
        /// </summary>
        public string? Scopes { get; set; }

        /// <summary>How long one pmj command that builds nothing may take.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// The most pmj may print, in characters, on each of its two streams. What it prints
        /// beyond that is read and thrown away, so that a project written to make pmj talk
        /// cannot fill this process's memory. An answer that was cut short is not used.
        /// </summary>
        public int MaxOutput { get; set; } = 4 * 1024 * 1024;

        /// <summary>How many pmj commands a host runs at once for the people using it. One beyond that is told to ask again.</summary>
        public int MaxAtOnce { get; set; } = 4;
    }
}
