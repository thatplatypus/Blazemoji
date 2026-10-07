using Blazemoji.Toolchain;

namespace Blazemoji.Test.Toolchain
{
    public class SourceFileNamesTests
    {
        [Theory]
        [InlineData("main.🍇")]
        [InlineData("lib/util.🍇")]
        [InlineData("my-file_2.🍇")]
        [InlineData("↘️🔸🔡.🍇")]
        [InlineData("🏛")]
        [InlineData("héllo.🍇")]
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

        /// <summary>
        /// The compiler builds its link command from the entry file's name and hands it to a
        /// shell, so a name must not be able to say anything to a shell or to the compiler.
        /// </summary>
        [Theory]
        [InlineData("a;touch pwned;.🍇")]
        [InlineData("$(id).🍇")]
        [InlineData("`id`.🍇")]
        [InlineData("a b.🍇")]
        [InlineData("a\tb.🍇")]
        [InlineData("a\nb.🍇")]
        [InlineData("a|b.🍇")]
        [InlineData("a&b.🍇")]
        [InlineData("a>b.🍇")]
        [InlineData("a<b.🍇")]
        [InlineData("a*.🍇")]
        [InlineData("a?.🍇")]
        [InlineData("a'b.🍇")]
        [InlineData("a\"b.🍇")]
        [InlineData("a(b).🍇")]
        [InlineData("a{b}.🍇")]
        [InlineData("a!b.🍇")]
        [InlineData("~a.🍇")]
        [InlineData("#a.🍇")]
        [InlineData("-o")]
        [InlineData("--help")]
        [InlineData("lib/-x.🍇")]
        [InlineData("a\u2028b.🍇")]
        [InlineData("a\u00A0b.🍇")]
        public void Names_that_a_shell_or_the_compiler_could_misread_are_not_safe(string name)
        {
            SourceFileNames.IsSafe(name).ShouldBeFalse();
        }
    }
}
