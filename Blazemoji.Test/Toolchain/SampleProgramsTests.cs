using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Local;

namespace Blazemoji.Test.Toolchain
{
    public class SampleProgramsTests
    {
        private const string NonTerminatingSample = "InfiniteLoop.🍇";
        private const string RandomSample = "RandomNumber.🍇";

        private static readonly string SamplesDirectory = Path.Combine(AppContext.BaseDirectory, "Emojicode", "Samples");

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

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

        public static TheoryData<string> DeterministicSamples()
        {
            var samples = new TheoryData<string>();
            foreach (var row in TerminatingSamples())
            {
                if (row.Data != RandomSample)
                    samples.Add(row.Data);
            }

            return samples;
        }

        /// <summary>
        /// The same binary is run twice: once to completion with its output collected in one go,
        /// and once through the event stream. Both must agree to the byte.
        /// </summary>
        [Theory]
        [Trait("Category", "Toolchain")]
        [MemberData(nameof(DeterministicSamples))]
        public async Task Streamed_output_matches_a_plain_run_of_the_same_binary(string sampleFile)
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();
            var build = await toolchain.CompileAsync(ToolchainFixture.SingleFile(await ReadSampleAsync(sampleFile)), Cancellation);
            build.Ok.ShouldBeTrue();
            var program = Path.Combine(toolchain.WorkRoot, "builds", build.BuildId!, "program");

            var plain = await ProcessRunner.RunAsync(program, [], Path.GetTempPath(), TimeSpan.FromSeconds(30), Cancellation);
            await using var run = await toolchain.StartRunAsync(new RunRequest(build.BuildId!), Cancellation);
            var streamed = await run.RunToEndAsync(Cancellation);

            plain.Stdout.ShouldNotBeNullOrWhiteSpace();
            streamed.Stdout.ShouldBe(plain.Stdout);
            streamed.Exit.ExitCode.ShouldBe(plain.ExitCode);
        }

        [Theory]
        [Trait("Category", "Toolchain")]
        [MemberData(nameof(TerminatingSamples))]
        public async Task Sample_compiles_and_runs_with_output(string sampleFile)
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var finished = await toolchain.CompileAndRunAsync(await ReadSampleAsync(sampleFile), Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.Exited);
            finished.Exit.ExitCode.ShouldBe(0);
            finished.Stdout.ShouldNotBeNullOrWhiteSpace();
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task HelloWorld_prints_its_greeting()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();

            var finished = await toolchain.CompileAndRunAsync(await ReadSampleAsync("HelloWorld.🍇"), Cancellation);

            finished.Stdout.ShouldContain("Hello World!");
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task The_endless_sample_is_ended_at_the_output_limit()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create(options => options.MaxOutputBytes = 262_144);

            var finished = await toolchain.CompileAndRunAsync(await ReadSampleAsync(NonTerminatingSample), Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.OutputLimit);
            finished.Stdout.ShouldStartWith("Let's see what happens!");
        }

        private static Task<string> ReadSampleAsync(string sampleFile) =>
            File.ReadAllTextAsync(Path.Combine(SamplesDirectory, sampleFile), Cancellation);
    }
}
