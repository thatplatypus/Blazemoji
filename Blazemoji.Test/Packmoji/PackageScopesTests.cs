using Blazemoji.Toolchain.Packmoji;

namespace Blazemoji.Test.Packmoji
{
    public class PackageScopesTests
    {
        [Fact]
        public void A_list_allows_the_scopes_it_names_and_no_other()
        {
            var scopes = PackageScopes.Parse("thatplatypus,emojicode");

            scopes.Allows("@thatplatypus/grapevine").ShouldBeTrue();
            scopes.Allows("@emojicode/anything").ShouldBeTrue();
            scopes.Allows("@someone/grapevine").ShouldBeFalse();
            scopes.Allows("@thatplatypus2/grapevine").ShouldBeFalse();
            scopes.AllowsNothing.ShouldBeFalse();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(" , ,")]
        public void With_nothing_said_nothing_is_allowed(string? said)
        {
            var scopes = PackageScopes.Parse(said);

            scopes.Allows("@thatplatypus/x").ShouldBeFalse();
            scopes.Allows("@emojicode/x").ShouldBeFalse();
            scopes.Allows("@anyone/x").ShouldBeFalse();
            scopes.AllowsNothing.ShouldBeTrue();
        }

        [Fact]
        public void A_star_allows_any_scope_but_never_a_name_that_is_not_one()
        {
            var scopes = PackageScopes.Parse("thatplatypus, *");

            scopes.Allows("@anyone/x").ShouldBeTrue();
            scopes.Allows("anyone/x").ShouldBeFalse();
            scopes.Allows("@anyone/../x").ShouldBeFalse();
            scopes.Allows(null).ShouldBeFalse();
            scopes.AllowsNothing.ShouldBeFalse();
        }

        [Fact]
        public void Spaces_around_a_scope_are_no_part_of_it()
        {
            PackageScopes.Parse(" thatplatypus , emojicode ").Allows("@emojicode/x").ShouldBeTrue();
        }

        [Fact]
        public void A_list_of_nothing_usable_allows_nothing()
        {
            var scopes = PackageScopes.Parse("Bad Scope,../x");

            scopes.Allows("@thatplatypus/x").ShouldBeFalse();
            scopes.Allows("@bad/x").ShouldBeFalse();
            scopes.AllowsNothing.ShouldBeTrue();
        }

        [Fact]
        public void It_says_what_it_allows_in_order_of_name()
        {
            PackageScopes.Parse("thatplatypus,emojicode").ToString().ShouldBe("emojicode, thatplatypus");
            PackageScopes.Parse("*").ToString().ShouldBe("*");
        }

        [Fact]
        public void pmj_is_given_the_list_in_order_and_is_given_nothing_for_any_scope_or_for_none()
        {
            PackageScopes.Parse("thatplatypus, emojicode").ForPmj.ShouldBe("emojicode,thatplatypus");
            PackageScopes.Parse("*").ForPmj.ShouldBeNull();
            PackageScopes.Parse(null).ForPmj.ShouldBeNull();
        }
    }
}
