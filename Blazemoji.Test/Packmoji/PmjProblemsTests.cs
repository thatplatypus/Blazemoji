using Blazemoji.Toolchain.Packmoji;

namespace Blazemoji.Test.Packmoji
{
    public class PmjProblemsTests
    {
        private static readonly string[] Places = ["/srv/work", "/home/app/.packmoji"];

        private static PackageProblem Problem(string code, string reason = "the reason", string? file = null) =>
            new(PackageProblem.Error, code, "The message.", reason, "the fix", file, file is null ? 0 : 3, file is null ? 0 : 5);

        [Theory]
        [InlineData("json.syntax")]
        [InlineData("key.missing")]
        [InlineData("lock.out-of-date")]
        [InlineData("resolve.line-conflict")]
        [InlineData("package.not-found")]
        [InlineData("scope.not-allowed")]
        [InlineData("release.invalid")]
        public void A_problem_with_the_two_files_or_a_published_package_is_passed_on_as_it_is(string code)
        {
            var problem = Problem(code, file: "packmoji.json");

            PmjProblems.ForAVisitor(problem, Places, asksGitHub: false).ShouldBe(problem);
        }

        [Theory]
        [InlineData("cache.unusable")]
        [InlineData("built.unusable")]
        [InlineData("config.invalid")]
        [InlineData("project.unreadable")]
        [InlineData("compiler.not-found")]
        [InlineData("tool.not-found")]
        [InlineData("something.new")]
        [InlineData("")]
        public void A_problem_with_the_machine_or_one_nobody_knows_is_not_passed_on(string code)
        {
            PmjProblems.ForAVisitor(Problem(code), Places, asksGitHub: true).ShouldBeNull();
        }

        [Theory]
        [InlineData("build.compile-failed", PmjProblems.CompilerSaidReason)]
        [InlineData("build.native-failed", PmjProblems.CompilerSaidReason)]
        [InlineData("build.link-failed", PmjProblems.CompilerSaidReason)]
        [InlineData("build.archive-failed", PmjProblems.CompilerSaidReason)]
        [InlineData("compiler.too-old", PmjProblems.CompilerTooOldReason)]
        public void What_a_tool_said_is_replaced_by_fixed_words_and_the_rest_is_kept(string code, string reason)
        {
            var shown = PmjProblems.ForAVisitor(Problem(code, "/srv/work/x.🍇:1:1: error"), Places, asksGitHub: false).ShouldNotBeNull();

            shown.Reason.ShouldBe(reason);
            shown.Message.ShouldBe("The message.");
            shown.Fix.ShouldBe("the fix");
        }

        [Theory]
        [InlineData("github.unreachable")]
        [InlineData("github.rate-limited")]
        public void A_problem_with_GitHub_is_news_only_where_pmj_may_ask_GitHub_and_never_in_a_sockets_words(string code)
        {
            var reported = Problem(code, "the request failed: Connection refused (10.0.0.7:443)");

            PmjProblems.ForAVisitor(reported, Places, asksGitHub: false).ShouldBeNull();
            var shown = PmjProblems.ForAVisitor(reported, Places, asksGitHub: true).ShouldNotBeNull();
            shown.Reason.ShouldBe(PmjProblems.GitHubSaidReason);
            shown.Message.ShouldBe("The message.");
        }

        [Theory]
        [InlineData("registry.unreachable")]
        [InlineData("registry.mismatch")]
        public void A_problem_with_the_registry_is_news_only_where_pmj_may_ask_one_and_never_in_a_sockets_words(string code)
        {
            var reported = Problem(code, "the request for http://10.0.0.7/registry/v1/packages/a/b.json failed: Connection refused (10.0.0.7:80)");

            PmjProblems.ForAVisitor(reported, Places, asksGitHub: false).ShouldBeNull();
            var shown = PmjProblems.ForAVisitor(reported, Places, asksGitHub: true).ShouldNotBeNull();
            shown.Code.ShouldBe(code);
            shown.Message.ShouldBe(reported.Message);
            shown.Reason.ShouldBe(PmjProblems.RegistrySaidReason);
        }

        [Fact]
        public void A_registry_out_of_reach_is_not_answered_with_advice_for_a_command_line()
        {
            var reported = Problem(PmjProblems.RegistryUnreachable)
                with { Fix = "check the network and the address of the registry, and try again; to ask GitHub itself in the meantime, run the command again with --direct" };

            PmjProblems.ForAVisitor(reported, Places, asksGitHub: true).ShouldNotBeNull().Fix.ShouldBe(PmjProblems.RegistryFix);
        }

        [Fact]
        public void What_to_do_about_a_version_the_registry_does_not_vouch_for_is_pmjs_own_advice()
        {
            var reported = Problem("registry.mismatch") with { Fix = "use another version" };

            PmjProblems.ForAVisitor(reported, Places, asksGitHub: true).ShouldNotBeNull().Fix.ShouldBe("use another version");
        }

        [Fact]
        public void A_problem_that_names_a_folder_of_this_machine_is_not_passed_on_whatever_its_code()
        {
            PmjProblems.ForAVisitor(Problem("lock.mismatch") with { Message = "See /srv/work/sets/1." }, Places, asksGitHub: false).ShouldBeNull();
            PmjProblems.ForAVisitor(Problem("lock.mismatch") with { Fix = "delete /home/app/.packmoji/cache" }, Places, asksGitHub: false).ShouldBeNull();
            PmjProblems.ForAVisitor(Problem("json.syntax", file: "/srv/work/asks/1/packmoji.json"), Places, asksGitHub: false).ShouldBeNull();
            PmjProblems.ForAVisitor(Problem("lock.mismatch", reason: "in /srv/work"), ["", "/srv/work"], asksGitHub: false).ShouldBeNull();
        }

        [Fact]
        public void A_place_that_is_nothing_names_nothing()
        {
            PmjProblems.ForAVisitor(Problem("json.syntax"), ["", null!], asksGitHub: false).ShouldNotBeNull();
        }

        [Fact]
        public void Its_own_problems_say_who_has_them()
        {
            PmjProblems.NoPackmoji("This toolchain").Code.ShouldBe(PackageProblemCodes.NoPackmoji);
            PmjProblems.NoPackmoji("This toolchain").Message.ShouldBe("This toolchain has no Packmoji.");
            PmjProblems.TimedOut().Code.ShouldBe(PackageProblemCodes.TimedOut);
            PmjProblems.Unreadable("the toolchain").Code.ShouldBe(PackageProblemCodes.Unreadable);
            PmjProblems.Unreadable("the toolchain").Fix.ShouldContain("the toolchain's log");
        }
    }
}
