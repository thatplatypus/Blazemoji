using System.Diagnostics;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Toolchain
{
    public class PmjProcessTests
    {
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static PmjProcess Pmj(string path, string? home = null) =>
            new(Options.Create(new PackmojiOptions { Path = path, Home = home }), NullLogger<PmjProcess>.Instance);

        private static string Folder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "pmj-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Fact]
        public async Task It_runs_in_the_folder_it_is_given_with_the_arguments_it_is_given()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var folder = Folder();
            var pmj = Pmj(ToolchainFixture.Script("fake-pmj", "pwd -P\nprintf '%s\\n' \"$@\""));

            var result = await pmj.RunAsync(new PmjCommand(folder, ["build", "--dependencies-only", "a b"]), Cancellation);

            result.Outcome.ShouldBe(PmjOutcome.Ran);
            result.ExitCode.ShouldBe(0);
            var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Path.GetFileName(lines[0]).ShouldBe(Path.GetFileName(folder));
            lines[1..].ShouldBe(["build", "--dependencies-only", "a b"]);
        }

        [Fact]
        public async Task It_is_told_where_its_folder_is_and_whatever_else_the_command_says()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var pmj = Pmj(ToolchainFixture.Script("fake-pmj", "echo \"$PACKMOJI_HOME|$EMOJICODEC\""), home: "/the/home");

            var fromOptions = await pmj.RunAsync(
                new PmjCommand(Folder(), [], Environment: new Dictionary<string, string> { [PmjVariables.Compiler] = "/the/compiler" }), Cancellation);
            var fromCommand = await pmj.RunAsync(
                new PmjCommand(Folder(), [], Environment: new Dictionary<string, string> { [PmjVariables.Home] = "/another/home" }), Cancellation);

            fromOptions.Stdout.Trim().ShouldBe("/the/home|/the/compiler");
            fromCommand.Stdout.Trim().ShouldBe("/another/home|");
        }

        [Fact]
        public async Task A_token_for_GitHub_in_this_process_is_not_passed_on_unless_the_command_gives_one()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var pmj = Pmj(ToolchainFixture.Script("fake-pmj", "echo \"[$GITHUB_TOKEN][$GH_TOKEN]\""));
            var before = (Environment.GetEnvironmentVariable("GITHUB_TOKEN"), Environment.GetEnvironmentVariable("GH_TOKEN"));
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "this-process-own");
            Environment.SetEnvironmentVariable("GH_TOKEN", "this-process-own-too");
            try
            {
                var without = await pmj.RunAsync(new PmjCommand(Folder(), []), Cancellation);
                var with = await pmj.RunAsync(
                    new PmjCommand(Folder(), [], Environment: new Dictionary<string, string> { ["GITHUB_TOKEN"] = "given" }), Cancellation);

                without.Stdout.Trim().ShouldBe("[][]");
                with.Stdout.Trim().ShouldBe("[given][]");
            }
            finally
            {
                Environment.SetEnvironmentVariable("GITHUB_TOKEN", before.Item1);
                Environment.SetEnvironmentVariable("GH_TOKEN", before.Item2);
            }
        }

        [Fact]
        public async Task What_it_printed_and_how_it_ended_come_back()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var pmj = Pmj(ToolchainFixture.Script("fake-pmj", "echo out\necho oops >&2\nexit 3"));

            var result = await pmj.RunAsync(new PmjCommand(Folder(), []), Cancellation);

            result.Outcome.ShouldBe(PmjOutcome.Ran);
            result.ExitCode.ShouldBe(3);
            result.Stdout.Trim().ShouldBe("out");
            result.Stderr.Trim().ShouldBe("oops");
        }

        [Fact]
        public async Task A_program_that_is_not_there_is_not_found_and_nothing_is_thrown()
        {
            var pmj = Pmj(Path.Combine(Path.GetTempPath(), "blazemoji-tests", "no-such-pmj-" + Guid.NewGuid().ToString("N")));

            var result = await pmj.RunAsync(new PmjCommand(Folder(), ["--version"]), Cancellation);

            result.Outcome.ShouldBe(PmjOutcome.NotFound);
        }

        [Fact]
        public async Task A_program_that_runs_out_of_time_is_ended()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var pmj = Pmj(ToolchainFixture.Script("fake-pmj", "sleep 30"));
            var clock = Stopwatch.StartNew();

            var result = await pmj.RunAsync(new PmjCommand(Folder(), [], Timeout: TimeSpan.FromMilliseconds(300)), Cancellation);

            result.Outcome.ShouldBe(PmjOutcome.TimedOut);
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
        }

        [Fact]
        public async Task Something_pmj_started_that_outlives_it_and_holds_its_output_does_not_keep_the_caller_waiting()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var pmj = Pmj(ToolchainFixture.Script("fake-pmj", "sleep 30 &\necho done\nexit 0"));
            var clock = Stopwatch.StartNew();

            var result = await pmj.RunAsync(new PmjCommand(Folder(), []), Cancellation);

            result.Outcome.ShouldBe(PmjOutcome.TimedOut);
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20));
        }

        [Fact]
        public async Task Output_larger_than_a_pipe_holds_on_both_streams_is_read_whole()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var pmj = Pmj(ToolchainFixture.Script("fake-pmj",
                "head -c 200000 /dev/zero | tr '\\0' 'a'\nhead -c 200000 /dev/zero | tr '\\0' 'b' >&2\nexit 0"));

            var result = await pmj.RunAsync(new PmjCommand(Folder(), []), Cancellation);

            result.Outcome.ShouldBe(PmjOutcome.Ran);
            result.ExitCode.ShouldBe(0);
            result.Stdout.Length.ShouldBe(200000);
            result.Stderr.Length.ShouldBe(200000);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(172800)]
        public void A_time_limit_that_makes_no_sense_falls_back_to_two_minutes_and_not_to_a_day(int seconds)
        {
            PmjProcess.Usable(TimeSpan.FromSeconds(seconds)).ShouldBe(TimeSpan.FromMinutes(2));
        }

        [Fact]
        public void A_sensible_time_limit_is_kept_as_it_is()
        {
            PmjProcess.Usable(TimeSpan.FromSeconds(30)).ShouldBe(TimeSpan.FromSeconds(30));
        }

        [Fact]
        public async Task A_caller_who_stops_waiting_ends_the_program_and_is_told_so()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var pmj = Pmj(ToolchainFixture.Script("fake-pmj", "sleep 30"));
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            stop.CancelAfter(TimeSpan.FromMilliseconds(300));
            var clock = Stopwatch.StartNew();

            await Should.ThrowAsync<OperationCanceledException>(() => pmj.RunAsync(new PmjCommand(Folder(), []), stop.Token));

            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
        }

        [Fact]
        public async Task What_pmj_prints_past_the_limit_is_thrown_away_and_an_answer_cut_short_is_no_answer()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var pmj = new PmjProcess(
                Options.Create(new PackmojiOptions
                {
                    Path = ToolchainFixture.Script("fake-pmj", "head -c 200000 /dev/zero | tr '\\0' 'o'\nhead -c 200000 /dev/zero | tr '\\0' 'e' >&2\nexit 3"),
                    MaxOutput = 1000,
                }),
                NullLogger<PmjProcess>.Instance);

            var result = await pmj.RunAsync(new PmjCommand(Folder(), []), Cancellation);

            result.Outcome.ShouldBe(PmjOutcome.Ran);
            result.ExitCode.ShouldBe(3);
            result.Stdout.ShouldBeEmpty();
            result.Stderr.Length.ShouldBe(1000);
        }

        [Fact]
        public async Task Output_within_the_limit_is_kept_whole()
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var pmj = new PmjProcess(
                Options.Create(new PackmojiOptions { Path = ToolchainFixture.Script("fake-pmj", "printf 'exactly ten'"), MaxOutput = 11 }),
                NullLogger<PmjProcess>.Instance);

            (await pmj.RunAsync(new PmjCommand(Folder(), []), Cancellation)).Stdout.ShouldBe("exactly ten");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public async Task A_limit_that_makes_no_sense_is_the_default_and_not_none(int most)
        {
            Assert.SkipUnless(ToolchainFixture.IsUnix, ToolchainFixture.UnixOnly);
            var pmj = new PmjProcess(
                Options.Create(new PackmojiOptions { Path = ToolchainFixture.Script("fake-pmj", "printf 'kept'"), MaxOutput = most }),
                NullLogger<PmjProcess>.Instance);

            (await pmj.RunAsync(new PmjCommand(Folder(), []), Cancellation)).Stdout.ShouldBe("kept");
        }
    }
}
