using Blazemoji.Toolchain.Packmoji;

namespace Blazemoji.Test.Packmoji
{
    public class PackageScopesTests
    {
        [Fact]
        public void A_list_allows_the_scopes_it_names_and_no_other()
        {
            var scopes = PackageScopes.Parse("thatplatypus,emojicode", PackageScopes.Any);

            scopes.Allows("@thatplatypus/grapevine").ShouldBeTrue();
            scopes.Allows("@emojicode/anything").ShouldBeTrue();
            scopes.Allows("@someone/grapevine").ShouldBeFalse();
            scopes.Allows("@thatplatypus2/grapevine").ShouldBeFalse();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void With_nothing_said_the_fallback_is_what_holds(string? said)
        {
            PackageScopes.Parse(said, "emojicode").Allows("@emojicode/x").ShouldBeTrue();
            PackageScopes.Parse(said, "emojicode").Allows("@thatplatypus/x").ShouldBeFalse();
            PackageScopes.Parse(said, PackageScopes.Any).Allows("@anyone/x").ShouldBeTrue();
        }

        [Fact]
        public void A_star_allows_any_scope_but_never_a_name_that_is_not_one()
        {
            var scopes = PackageScopes.Parse("thatplatypus, *", "emojicode");

            scopes.Allows("@anyone/x").ShouldBeTrue();
            scopes.Allows("anyone/x").ShouldBeFalse();
            scopes.Allows("@anyone/../x").ShouldBeFalse();
            scopes.Allows(null).ShouldBeFalse();
        }

        [Fact]
        public void Spaces_around_a_scope_are_no_part_of_it()
        {
            PackageScopes.Parse(" thatplatypus , emojicode ", PackageScopes.Any).Allows("@emojicode/x").ShouldBeTrue();
        }

        [Fact]
        public void A_list_of_nothing_usable_allows_nothing()
        {
            var scopes = PackageScopes.Parse("Bad Scope,../x", PackageScopes.Any);

            scopes.Allows("@thatplatypus/x").ShouldBeFalse();
            scopes.Allows("@bad/x").ShouldBeFalse();
        }

        [Fact]
        public void It_says_what_it_allows_in_order_of_name()
        {
            PackageScopes.Parse("thatplatypus,emojicode", PackageScopes.Any).ToString().ShouldBe("emojicode, thatplatypus");
            PackageScopes.Parse("*", "x").ToString().ShouldBe("*");
        }
    }
}
