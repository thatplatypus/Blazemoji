using Blazemoji.Toolchain.Packmoji;

namespace Blazemoji.Test.Toolchain
{
    public class PmjInTheImageTests
    {
        [Fact]
        [Trait("Requires", "Packmoji")]
        public async Task The_pmj_that_came_with_the_image_says_which_version_it_is()
        {
            Assert.SkipUnless(PmjFixture.Available, PmjFixture.SkipReason);

            var result = await PmjFixture.Create().RunAsync(
                new PmjCommand(Path.GetTempPath(), ["--version"]), TestContext.Current.CancellationToken);

            result.Outcome.ShouldBe(PmjOutcome.Ran);
            result.ExitCode.ShouldBe(0, result.Stderr);
            result.Stdout.Trim().ShouldMatch(@"^\d+\.\d+\.\d+");
        }

        [Fact]
        [Trait("Requires", "Packmoji")]
        public async Task The_pmj_that_came_with_the_image_is_the_version_the_pin_names()
        {
            Assert.SkipUnless(PmjFixture.Available, PmjFixture.SkipReason);

            var result = await PmjFixture.Create().RunAsync(
                new PmjCommand(Path.GetTempPath(), ["--version"]), TestContext.Current.CancellationToken);

            // pmj says its version and then the commit it was built from: "0.1.0+fbed38c...".
            result.Stdout.Trim().Split('+')[0].ShouldBe(PackmojiPinTests.Version());
        }
    }
}
