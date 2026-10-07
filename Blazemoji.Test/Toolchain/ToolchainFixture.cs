using System.Runtime.InteropServices;
using Blazemoji.Toolchain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Toolchain
{
    internal static class ToolchainFixture
    {
        public const string SkipReason = "The bundled Emojicode compiler is the linux/amd64 build. Run the Docker test stage.";

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

        /// <summary>
        /// Puts a shell script where a compiled program would be, so that run mechanics can be
        /// tested on any Unix without the Emojicode compiler.
        /// </summary>
        public static string ScriptBuild(LocalToolchain toolchain, string script)
        {
            var buildId = Guid.NewGuid().ToString("N");
            var directory = Path.Combine(toolchain.WorkRoot, "builds", buildId);
            Directory.CreateDirectory(directory);

            var program = Path.Combine(directory, "program");
            File.WriteAllText(program, "#!/bin/sh\n" + script + "\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            return buildId;
        }

        public const string UnixOnly = "Run mechanics are tested with shell scripts, which need a Unix system.";

        public static bool IsUnix => !OperatingSystem.IsWindows();

        public static CompileRequest SingleFile(string code) =>
            new(new Dictionary<string, string> { ["main.🍇"] = code }, "main.🍇");
    }
}
