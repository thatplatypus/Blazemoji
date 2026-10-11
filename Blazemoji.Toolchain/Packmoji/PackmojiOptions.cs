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
        /// any. Null takes the default of whoever reads this.
        /// </summary>
        public string? Scopes { get; set; }

        /// <summary>How long one pmj command that builds nothing may take.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
    }
}
