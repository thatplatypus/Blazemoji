using System.Runtime.InteropServices;
using Blazemoji.Toolchain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Toolchain
{
    internal static class ToolchainFixture
    {
        public const string SkipReason = "The Emojicode compiler only runs on linux/amd64. Run the Docker test stage.";

        public static bool Available =>
            OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

        /// <summary>
        /// A toolchain with its own empty work root, using the compiler and packages copied next to the tests.
        /// </summary>
        public static LocalToolchain Create(Action<ToolchainOptions>? configure = null)
        {
            var options = new ToolchainOptions
            {
                WorkRoot = Path.Combine(Path.GetTempPath(), "blazemoji-tests", Guid.NewGuid().ToString("N")),
            };
            configure?.Invoke(options);

            return new LocalToolchain(Options.Create(options), NullLogger<LocalToolchain>.Instance);
        }

        public static CompileRequest SingleFile(string code) =>
            new(new Dictionary<string, string> { ["main.🍇"] = code }, "main.🍇");
    }
}
