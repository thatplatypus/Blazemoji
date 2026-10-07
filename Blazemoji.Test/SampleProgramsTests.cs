using System.Runtime.InteropServices;
using Blazemoji.Contracts.Models;
using Blazemoji.Services.Compiler;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blazemoji.Test
{
    [Trait("Category", "Toolchain")]
    public class SampleProgramsTests
    {
        private const string SkipReason = "The Emojicode compiler only runs on linux/amd64. Run the Docker test stage.";
        private const string NonTerminatingSample = "InfiniteLoop.🍇";

        private static readonly string SamplesDirectory = Path.Combine(AppContext.BaseDirectory, "Emojicode", "Samples");

        private static bool ToolchainAvailable =>
            OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

        public SampleProgramsTests()
        {
            // CompilerService resolves emojicodec, ./packages and its output file from the working directory.
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        }

        public static TheoryData<string> TerminatingSamples()
        {
            var samples = new TheoryData<string>();
            foreach (var path in Directory.EnumerateFiles(SamplesDirectory).Order())
            {
                var name = Path.GetFileName(path);
                if (name != NonTerminatingSample)
                    samples.Add(name);
            }

            return samples;
        }

        [Fact]
        public void Samples_are_copied_next_to_the_tests()
        {
            var shipped = Directory.EnumerateFiles(SamplesDirectory).Select(Path.GetFileName).ToList();

            shipped.Count.ShouldBeGreaterThanOrEqualTo(10);
            shipped.ShouldContain("HelloWorld.🍇");
            shipped.ShouldContain(NonTerminatingSample);
        }

        [Theory]
        [MemberData(nameof(TerminatingSamples))]
        public async Task Sample_compiles_and_runs_with_output(string sampleFile)
        {
            Assert.SkipUnless(ToolchainAvailable, SkipReason);

            var result = await RunSampleAsync(sampleFile);

            result.Error.ShouldBeFalse(result.Message);
            result.Result.ShouldNotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task HelloWorld_prints_its_greeting()
        {
            Assert.SkipUnless(ToolchainAvailable, SkipReason);

            var result = await RunSampleAsync("HelloWorld.🍇");

            result.Error.ShouldBeFalse(result.Message);
            result.Result.ShouldContain("Hello World!");
        }

        private static async Task<EmojicodeResult> RunSampleAsync(string sampleFile)
        {
            var code = await File.ReadAllTextAsync(Path.Combine(SamplesDirectory, sampleFile));
            var compiler = new CompilerService(NullLogger<CompilerService>.Instance);

            return await compiler.CompileAndExecuteAsync(code);
        }
    }
}
