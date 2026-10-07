using Blazemoji.Toolchain;

namespace Blazemoji.Test.Toolchain
{
    public class SourceFileNamesTests
    {
        [Theory]
        [InlineData("main.🍇")]
        [InlineData("lib/util.🍇")]
        [InlineData("a b.🍇")]
        public void Relative_names_inside_the_build_are_safe(string name)
        {
            SourceFileNames.IsSafe(name).ShouldBeTrue();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("/etc/passwd")]
        [InlineData("../x.🍇")]
        [InlineData("a/../../x.🍇")]
        [InlineData("a\\..\\x.🍇")]
        [InlineData("a/./x.🍇")]
        [InlineData("a//x.🍇")]
        [InlineData("a/")]
        [InlineData("x\0.🍇")]
        [InlineData("C:\\x.🍇")]
        [InlineData("C:x.🍇")]
        public void Names_that_could_escape_the_build_are_not(string name)
        {
            SourceFileNames.IsSafe(name).ShouldBeFalse();
        }
    }
}
