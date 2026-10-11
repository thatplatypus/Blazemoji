using Blazemoji.Toolchain.Packmoji;

namespace Blazemoji.Test.Packmoji
{
    public class PmjJsonTests
    {
        private const string Tree = """
            {
              "ok": true,
              "diagnostics": [],
              "omittedDiagnostics": 0,
              "project": { "name": "@thatplatypus/hello", "version": "0.1.0" },
              "dependencies": [ { "name": "@thatplatypus/hello_greeter", "requirement": "0.1", "version": "0.1.0" } ],
              "devDependencies": [],
              "packages": [
                {
                  "name": "@thatplatypus/hello_greeter",
                  "version": "0.1.0",
                  "source": "github.com/thatplatypus/packmoji",
                  "sha256": "9da45105dcb3746b7e01d8bd0e702ef3100c90be1be7aa650ea0a9367b8fae3a",
                  "verified": "checksum",
                  "dependencies": [ { "name": "@thatplatypus/hello_words", "version": "0.1.0" } ]
                },
                {
                  "name": "@thatplatypus/hello_words",
                  "version": "0.1.0",
                  "source": "github.com/thatplatypus/packmoji",
                  "sha256": "8502cea823dc837cf74985e796e3d5604035095ec715289ad5c10540c73759f8",
                  "verified": "checksum",
                  "dependencies": []
                }
              ]
            }
            """;

        private const string OutOfDate = """
            {
              "ok": false,
              "diagnostics": [
                {
                  "severity": "error",
                  "code": "lock.out-of-date",
                  "message": "packmoji.lock no longer answers what packmoji.json asks for.",
                  "reason": "this command reads what is locked, and chooses nothing itself",
                  "fix": "run pmj install, which writes packmoji.lock"
                }
              ],
              "omittedDiagnostics": 0
            }
            """;

        private const string BadManifest = """
            {
              "ok": false,
              "diagnostics": [
                {
                  "severity": "error",
                  "code": "json.syntax",
                  "message": "The file is not valid JSON.",
                  "reason": "strict JSON is required: no comments, no trailing commas, every string closed, and nothing after the final brace",
                  "fix": "correct the JSON at this position",
                  "location": { "file": "packmoji.json", "line": 1, "column": 47 }
                }
              ],
              "omittedDiagnostics": 0
            }
            """;

        private const string Build = """
            {
              "ok": true,
              "diagnostics": [],
              "omittedDiagnostics": 0,
              "compiler": {
                "path": "/tl/emojicodec/emojicodec",
                "version": "1.0.0-beta.2",
                "sha256": "08da67b417a11db6a3647ccc3e563f541706fa5f38426b89c19ab3f68ecbaa91"
              },
              "packagesDirectory": "/tmp/set/packages",
              "packages": [
                {
                  "name": "@thatplatypus/hello_greeter",
                  "version": "0.1.0",
                  "bareName": "hello_greeter",
                  "directory": "/tmp/set/packages/hello_greeter",
                  "link": [ "m" ],
                  "built": true
                },
                {
                  "name": "@thatplatypus/hello_words",
                  "version": "0.1.0",
                  "bareName": "hello_words",
                  "directory": "/tmp/set/packages/hello_words",
                  "link": [],
                  "built": true
                }
              ]
            }
            """;

        private const string NoCompiler = """
            {
              "ok": false,
              "diagnostics": [
                {
                  "severity": "error",
                  "code": "compiler.not-found",
                  "message": "The Emojicode compiler was not found.",
                  "reason": "no program called emojicodec is in any directory of PATH",
                  "fix": "install Emojicode, or set EMOJICODEC to where it is"
                }
              ],
              "omittedDiagnostics": 0,
              "packagesDirectory": "/tmp/set/packages",
              "packages": []
            }
            """;

        [Fact]
        public void A_tree_gives_every_locked_package_with_its_digest()
        {
            var tree = PmjJson.ReadTree(Tree).ShouldNotBeNull();

            tree.Report.Ok.ShouldBeTrue();
            tree.Report.Problems.ShouldBeEmpty();
            tree.Packages.ShouldBe(
            [
                new LockedPackage("@thatplatypus/hello_greeter", "0.1.0", "9da45105dcb3746b7e01d8bd0e702ef3100c90be1be7aa650ea0a9367b8fae3a"),
                new LockedPackage("@thatplatypus/hello_words", "0.1.0", "8502cea823dc837cf74985e796e3d5604035095ec715289ad5c10540c73759f8"),
            ]);
        }

        [Fact]
        public void A_problem_keeps_its_code_and_its_three_texts()
        {
            var tree = PmjJson.ReadTree(OutOfDate).ShouldNotBeNull();

            tree.Report.Ok.ShouldBeFalse();
            tree.Packages.ShouldBeEmpty();
            tree.Report.Problems.ShouldHaveSingleItem().ShouldBe(new PackageProblem(
                PackageProblem.Error,
                "lock.out-of-date",
                "packmoji.lock no longer answers what packmoji.json asks for.",
                "this command reads what is locked, and chooses nothing itself",
                "run pmj install, which writes packmoji.lock"));
        }

        [Fact]
        public void A_problem_with_a_place_keeps_its_file_line_and_column()
        {
            var problem = PmjJson.ReadTree(BadManifest).ShouldNotBeNull().Report.Problems.ShouldHaveSingleItem();

            problem.Code.ShouldBe("json.syntax");
            problem.File.ShouldBe("packmoji.json");
            problem.Line.ShouldBe(1);
            problem.Column.ShouldBe(47);
            problem.IsError.ShouldBeTrue();
        }

        [Fact]
        public void A_build_gives_each_package_with_the_name_it_is_imported_by_and_what_it_links()
        {
            var build = PmjJson.ReadBuild(Build).ShouldNotBeNull();

            build.Report.Ok.ShouldBeTrue();
            build.Packages.Count.ShouldBe(2);
            build.Packages[0].Name.ShouldBe("@thatplatypus/hello_greeter");
            build.Packages[0].Version.ShouldBe("0.1.0");
            build.Packages[0].BareName.ShouldBe("hello_greeter");
            build.Packages[0].Link.ShouldBe(["m"]);
            build.Packages[1].BareName.ShouldBe("hello_words");
            build.Packages[1].Link.ShouldBeEmpty();
        }

        [Fact]
        public void A_build_that_was_stopped_gives_its_problem_and_no_package()
        {
            var build = PmjJson.ReadBuild(NoCompiler).ShouldNotBeNull();

            build.Report.Ok.ShouldBeFalse();
            build.Packages.ShouldBeEmpty();
            build.Report.Problems.ShouldHaveSingleItem().Code.ShouldBe("compiler.not-found");
        }

        [Fact]
        public void A_warning_is_not_an_error()
        {
            const string yanked = """{"ok":true,"diagnostics":[{"severity":"warning","code":"resolve.yanked-locked","message":"m","reason":"r","fix":"f"}],"packages":[]}""";

            var problem = PmjJson.ReadTree(yanked).ShouldNotBeNull().Report.Problems.ShouldHaveSingleItem();

            problem.Severity.ShouldBe(PackageProblem.Warning);
            problem.IsError.ShouldBeFalse();
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("null")]
        [InlineData("""{"diagnostics":[]}""")]
        [InlineData("""{"ok":"yes"}""")]
        [InlineData("error[lock.out-of-date]: There is no packmoji.lock.")]
        public void Text_that_is_not_what_pmj_prints_is_nothing_and_nothing_is_thrown(string stdout)
        {
            PmjJson.ReadTree(stdout).ShouldBeNull();
            PmjJson.ReadBuild(stdout).ShouldBeNull();
        }

        [Fact]
        public void Diagnostics_of_the_wrong_kind_are_left_out_and_nothing_is_thrown()
        {
            const string odd = """{"ok":true,"diagnostics":["x",7,{"code":5,"location":"here"}],"packages":[]}""";

            var problem = PmjJson.ReadTree(odd).ShouldNotBeNull().Report.Problems.ShouldHaveSingleItem();

            problem.Code.ShouldBeEmpty();
            problem.File.ShouldBeNull();
            problem.Line.ShouldBe(0);
        }

        [Theory]
        [InlineData("""{"ok":true,"packages":[3]}""")]
        [InlineData("""{"ok":true,"packages":[{"name":4,"version":"1.0.0","sha256":"x","bareName":"b"}]}""")]
        [InlineData("""{"ok":true,"packages":[{"name":"@a/b","version":"1.0.0"}]}""")]
        [InlineData("""{"ok":true,"packages":[{"name":"","version":"1.0.0","sha256":"x","bareName":"b"}]}""")]
        public void A_package_of_the_wrong_kind_makes_the_whole_text_nothing(string stdout)
        {
            PmjJson.ReadTree(stdout).ShouldBeNull();
            PmjJson.ReadBuild(stdout).ShouldBeNull();
        }

        [Fact]
        public void A_library_to_link_that_is_not_text_makes_a_build_nothing()
        {
            const string number = """{"ok":true,"packages":[{"name":"@a/b","version":"1.0.0","bareName":"b","link":["m",2]}]}""";
            const string text = """{"ok":true,"packages":[{"name":"@a/b","version":"1.0.0","bareName":"b","link":"m"}]}""";

            PmjJson.ReadBuild(number).ShouldBeNull();
            PmjJson.ReadBuild(text).ShouldBeNull();
        }

        [Fact]
        public void A_string_that_cannot_be_decoded_is_nothing_and_nothing_is_thrown()
        {
            // An escaped lone surrogate is valid JSON to the parser and fails only when the string is read.
            const string lone = """{"ok":true,"diagnostics":[],"packages":[{"name":"\ud83d","version":"1.0.0","sha256":"x"}]}""";

            PmjJson.ReadTree(lone).ShouldBeNull();
        }

        // The answers pmj at 61b4275 gave for a project that asks for Grapevine 0.1.0.
        private const string Resolved = """
            {
              "ok": true,
              "diagnostics": [],
              "omittedDiagnostics": 0,
              "written": true,
              "changes": [
                { "change": "added", "name": "@thatplatypus/crypto", "to": "0.1.0" }
              ],
              "packages": [
                {
                  "name": "@thatplatypus/crypto",
                  "version": "0.1.0",
                  "source": "github.com/thatplatypus/grapevine",
                  "sha256": "8150116d875b82482156009d92283a3f890e95e7ed129876a1aeff5fe389b604",
                  "verified": "checksum",
                  "dependencies": []
                }
              ]
            }
            """;

        private const string InstallRefused = """
            {
              "ok": false,
              "diagnostics": [
                {
                  "severity": "error",
                  "code": "lock.out-of-date",
                  "message": "There is no packmoji.lock, and --locked forbids writing one.",
                  "reason": "with --locked, pmj installs exactly what the lockfile holds and chooses nothing",
                  "fix": "run pmj install without --locked, and commit the packmoji.lock it writes"
                }
              ],
              "omittedDiagnostics": 0
            }
            """;

        [Fact]
        public void An_install_that_resolved_says_it_wrote_and_what_is_locked_now()
        {
            var install = PmjJson.ReadInstall(Resolved).ShouldNotBeNull();

            install.Report.Ok.ShouldBeTrue();
            install.Written.ShouldBeTrue();
            install.Packages.ShouldBe([new LockedPackage("@thatplatypus/crypto", "0.1.0", "8150116d875b82482156009d92283a3f890e95e7ed129876a1aeff5fe389b604")]);
        }

        [Fact]
        public void An_install_with_nothing_to_resolve_wrote_nothing()
        {
            PmjJson.ReadInstall(Resolved.Replace("\"written\": true", "\"written\": false")).ShouldNotBeNull().Written.ShouldBeFalse();
        }

        [Fact]
        public void An_install_that_was_refused_has_its_problem_and_no_packages()
        {
            var install = PmjJson.ReadInstall(InstallRefused).ShouldNotBeNull();

            install.Report.Ok.ShouldBeFalse();
            install.Written.ShouldBeFalse();
            install.Packages.ShouldBeEmpty();
            install.Report.Problems.ShouldHaveSingleItem().Code.ShouldBe(PmjProblems.LockOutOfDate);
        }

        [Theory]
        [InlineData("")]
        [InlineData("error: Unrecognized command or argument '--nonsense'.")]
        [InlineData("""{"ok":true,"packages":[{"name":"@a/b","version":"1.0.0"}]}""")]
        public void An_install_answer_that_is_not_pmjs_is_nothing(string stdout)
        {
            PmjJson.ReadInstall(stdout).ShouldBeNull();
        }
    }
}
