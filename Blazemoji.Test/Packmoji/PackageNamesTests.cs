using Blazemoji.Toolchain.Packmoji;

namespace Blazemoji.Test.Packmoji
{
    public class PackageNamesTests
    {
        private const string Digest = "8502cea823dc837cf74985e796e3d5604035095ec715289ad5c10540c73759f8";

        [Theory]
        [InlineData("@thatplatypus/grapevine", "thatplatypus", "grapevine")]
        [InlineData("@emojicode/hello_words", "emojicode", "hello_words")]
        [InlineData("@a-b/c9", "a-b", "c9")]
        public void A_full_name_is_split_into_its_scope_and_its_name(string fullName, string scope, string name)
        {
            PackageNames.TryParse(fullName, out var foundScope, out var foundName).ShouldBeTrue();

            foundScope.ShouldBe(scope);
            foundName.ShouldBe(name);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("grapevine")]
        [InlineData("@thatplatypus")]
        [InlineData("@thatplatypus/")]
        [InlineData("@/grapevine")]
        [InlineData("@thatplatypus/grape/vine")]
        [InlineData("@thatplatypus/../etc")]
        [InlineData("@../x")]
        [InlineData("@ThatPlatypus/grapevine")]
        [InlineData("@thatplatypus/Grapevine")]
        [InlineData("@thatplatypus/grape-vine")]
        [InlineData("@thatplatypus/9lives")]
        [InlineData("@-x/grapevine")]
        [InlineData("@that platypus/grapevine")]
        [InlineData("@thatplatypus/grapevine\n")]
        [InlineData("@thatplatypus/grape.vine")]
        public void What_is_not_a_full_name_is_refused(string? fullName)
        {
            PackageNames.TryParse(fullName, out var scope, out var name).ShouldBeFalse();

            scope.ShouldBeEmpty();
            name.ShouldBeEmpty();
        }

        [Fact]
        public void A_name_or_a_scope_that_is_too_long_is_refused()
        {
            PackageNames.IsScope(new string('a', 39)).ShouldBeTrue();
            PackageNames.IsScope(new string('a', 40)).ShouldBeFalse();
            PackageNames.IsBareName(new string('a', 64)).ShouldBeTrue();
            PackageNames.IsBareName(new string('a', 65)).ShouldBeFalse();
        }

        [Theory]
        [InlineData("0.1.0")]
        [InlineData("10.20.30")]
        [InlineData("1.0.0-beta.2")]
        public void A_version_is_accepted(string version) => PackageNames.IsVersion(version).ShouldBeTrue();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("v1.0.0")]
        [InlineData("1.0.0+build")]
        [InlineData("../1")]
        [InlineData("1/2")]
        [InlineData("1.0.0 ")]
        [InlineData(".1")]
        [InlineData("1\\2")]
        public void What_is_not_a_version_is_refused(string? version) => PackageNames.IsVersion(version).ShouldBeFalse();

        [Fact]
        public void A_version_that_is_too_long_is_refused()
        {
            PackageNames.IsVersion("1." + new string('0', 62)).ShouldBeTrue();
            PackageNames.IsVersion("1." + new string('0', 63)).ShouldBeFalse();
        }

        [Fact]
        public void A_digest_is_sixty_four_hexadecimal_digits_in_lowercase()
        {
            PackageNames.IsDigest(Digest).ShouldBeTrue();
            PackageNames.IsDigest(Digest.ToUpperInvariant()).ShouldBeFalse();
            PackageNames.IsDigest(Digest[1..]).ShouldBeFalse();
            PackageNames.IsDigest(Digest + "0").ShouldBeFalse();
            PackageNames.IsDigest("g" + Digest[1..]).ShouldBeFalse();
            PackageNames.IsDigest(null).ShouldBeFalse();
        }

        [Theory]
        [InlineData("m")]
        [InlineData("pthread")]
        [InlineData("stdc++")]
        [InlineData("_x")]
        [InlineData("ssl.1")]
        [InlineData("z-ng")]
        public void A_library_to_link_is_accepted(string library) => PackageNames.IsLinkLibrary(library).ShouldBeTrue();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("-lm")]
        [InlineData("a b")]
        [InlineData("../x")]
        [InlineData("a/b")]
        [InlineData(".hidden")]
        public void What_could_be_an_option_or_a_path_is_not_a_library(string? library) => PackageNames.IsLinkLibrary(library).ShouldBeFalse();

        [Fact]
        public void A_locked_package_is_well_formed_only_when_every_part_is()
        {
            new LockedPackage("@thatplatypus/hello_words", "0.1.0", Digest).IsWellFormed.ShouldBeTrue();
            new LockedPackage("hello_words", "0.1.0", Digest).IsWellFormed.ShouldBeFalse();
            new LockedPackage("@thatplatypus/hello_words", "../0.1.0", Digest).IsWellFormed.ShouldBeFalse();
            new LockedPackage("@thatplatypus/hello_words", "0.1.0", "abc").IsWellFormed.ShouldBeFalse();
        }

        [Theory]
        [InlineData("@thatplatypus/con")]
        [InlineData("@thatplatypus/nul")]
        [InlineData("@thatplatypus/com1")]
        [InlineData("@thatplatypus/lpt9")]
        [InlineData("@aux/thing")]
        [InlineData("@prn/thing")]
        public void A_name_Windows_keeps_for_a_device_is_not_a_package(string fullName)
        {
            PackageNames.TryParse(fullName, out var scope, out var name).ShouldBeFalse();
        }

        [Theory]
        [InlineData("@thatplatypus/console")]
        [InlineData("@thatplatypus/com10")]
        [InlineData("@thatplatypus/null_things")]
        public void A_name_that_only_begins_like_a_device_is_a_package(string fullName)
        {
            PackageNames.TryParse(fullName, out var scope, out var name).ShouldBeTrue();
        }

        [Fact]
        public void A_version_that_ends_in_a_dot_is_not_one_since_Windows_would_drop_the_dot_from_a_folder()
        {
            PackageNames.IsVersion("1.0.").ShouldBeFalse();
            PackageNames.IsVersion("1.0.0").ShouldBeTrue();
        }
    }
}
