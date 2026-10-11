using Blazemoji.Toolchain.Packmoji;

namespace Blazemoji.Test.Packmoji
{
    public class PackmojiCacheTests
    {
        private const string Digest = "8502cea823dc837cf74985e796e3d5604035095ec715289ad5c10540c73759f8";

        [Fact]
        public void An_archive_lies_where_pmj_keeps_it()
        {
            var home = Path.Combine(Path.GetTempPath(), "home");

            PackmojiCache.ArchivePath(home, new LockedPackage("@thatplatypus/hello_words", "0.1.0", Digest))
                .ShouldBe(Path.Combine(home, "cache", "thatplatypus", "hello_words", "0.1.0", Digest + ".pmj.tar.gz"));
        }

        [Theory]
        [InlineData("@thatplatypus/../../etc", "0.1.0", Digest)]
        [InlineData("@thatplatypus/hello_words", "../../0.1.0", Digest)]
        [InlineData("@thatplatypus/hello_words", "0.1.0", "../../passwd")]
        [InlineData("hello_words", "0.1.0", Digest)]
        public void Nothing_that_is_not_well_formed_becomes_a_path(string name, string version, string sha256)
        {
            PackmojiCache.ArchivePath("/home", new LockedPackage(name, version, sha256)).ShouldBeNull();
        }
    }
}
