using System.Net;

namespace Blazemoji.Toolchain.ContractTests
{
    public class CompileContractTests
    {
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Health_answers_200()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.Http.GetAsync("health", Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task A_valid_program_compiles_to_a_build()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            var result = await ToolchainService.CompileAsync(Programs.Hello, Cancellation);

            result.GetProperty("ok").GetBoolean().ShouldBeTrue();
            result.GetProperty("diagnostics").GetArrayLength().ShouldBe(0);
            result.GetProperty("buildId").GetString().ShouldNotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task A_program_with_an_error_is_a_200_with_ok_false_a_located_diagnostic_and_no_build()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            var result = await ToolchainService.CompileAsync(Programs.UndefinedVariable, Cancellation);

            result.GetProperty("ok").GetBoolean().ShouldBeFalse();
            (result.TryGetProperty("buildId", out var buildId) && buildId.ValueKind != System.Text.Json.JsonValueKind.Null).ShouldBeFalse();
            var diagnostic = result.GetProperty("diagnostics")[0];
            diagnostic.GetProperty("severity").GetString().ShouldBe("error");
            diagnostic.GetProperty("file").GetString().ShouldBe("main.🍇");
            diagnostic.GetProperty("line").GetInt32().ShouldBe(2);
            diagnostic.GetProperty("character").GetInt32().ShouldBe(5);
            diagnostic.GetProperty("message").GetString().ShouldBe("Variable \"nope\" not defined.");
        }

        [Fact]
        public async Task Characters_are_counted_from_one_on_the_first_line_too()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            var result = await ToolchainService.CompileAsync("🏁 🍇 😀 nope❗️ 🍉\n", Cancellation);

            var diagnostic = result.GetProperty("diagnostics")[0];
            diagnostic.GetProperty("line").GetInt32().ShouldBe(1);
            diagnostic.GetProperty("character").GetInt32().ShouldBe(7);
        }

        [Fact]
        public async Task A_build_with_only_a_warning_is_ok_and_lists_the_warning()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            var result = await ToolchainService.CompileAsync(Programs.RttiWarning, Cancellation);

            result.GetProperty("ok").GetBoolean().ShouldBeTrue();
            result.GetProperty("buildId").GetString().ShouldNotBeNullOrWhiteSpace();
            var diagnostics = result.GetProperty("diagnostics").EnumerateArray().ToList();
            diagnostics.ShouldNotBeEmpty();
            diagnostics.ShouldAllBe(d => d.GetProperty("severity").GetString() == "warning");
        }

        [Fact]
        public async Task Several_files_compile_together_when_the_entry_includes_the_others()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var files = new Dictionary<string, string>
            {
                ["main.🍇"] = "📜 🔤greeting.🍇🔤\n\n🏁 🍇\n  😀 🆕🙋❗️❗️\n🍉\n",
                ["greeting.🍇"] = "🐇 🙋 🍇\n  🆕 🍇🍉\n\n  ❗️ 😀 🍇\n    😀 🔤from another file🔤❗️\n  🍉\n🍉\n",
            };

            using var response = await ToolchainService.PostCompileAsync(new { files, entry = "main.🍇" }, Cancellation);
            var result = await ToolchainService.ReadJsonAsync(response, Cancellation);

            result.GetProperty("ok").GetBoolean().ShouldBeTrue(result.GetRawText());
            var runId = await ToolchainService.StartRunAsync(result.GetProperty("buildId").GetString()!, Cancellation);
            var events = await ToolchainService.ReadEventsAsync(runId, Cancellation);
            ToolchainService.Text(events, "stdout").ShouldBe("from another file\n");
        }

        [Fact]
        public async Task Naming_a_stock_package_is_accepted()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var files = new Dictionary<string, string> { ["main.🍇"] = Programs.Hello };

            using var response = await ToolchainService.PostCompileAsync(new { files, entry = "main.🍇", packages = new[] { "s" } }, Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task A_request_without_files_is_a_400_problem()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.PostCompileAsync(new { entry = "main.🍇" }, Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, Cancellation);
        }

        [Fact]
        public async Task A_body_that_is_not_json_is_a_400()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.Http.PostAsync("compile", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task A_body_far_larger_than_any_program_is_a_413()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var files = new Dictionary<string, string> { ["main.🍇"] = Programs.Hello + "💭 " + new string('x', 16 * 1024 * 1024) };

            // Asking first is what lets the answer be read: a service is free to hang up on a
            // body this size, and then there is no status to see.
            using var request = new HttpRequestMessage(HttpMethod.Post, "compile") { Content = ToolchainService.Json(new { files, entry = "main.🍇" }) };
            request.Headers.ExpectContinue = true;
            using var response = await ToolchainService.Http.SendAsync(request, Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        }

        [Fact]
        public async Task An_entry_that_is_not_one_of_the_files_is_a_400_problem()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var files = new Dictionary<string, string> { ["main.🍇"] = Programs.Hello };

            using var response = await ToolchainService.PostCompileAsync(new { files, entry = "other.🍇" }, Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, Cancellation);
        }

        [Theory]
        [InlineData("../main.🍇")]
        [InlineData("/tmp/main.🍇")]
        [InlineData("a;b.🍇")]
        [InlineData("-o")]
        public async Task A_file_name_that_could_escape_or_be_misread_is_a_400_problem(string name)
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var files = new Dictionary<string, string> { [name] = Programs.Hello };

            using var response = await ToolchainService.PostCompileAsync(new { files, entry = name }, Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, Cancellation);
        }

        [Fact]
        public async Task An_unknown_package_is_a_400_problem()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var files = new Dictionary<string, string> { ["main.🍇"] = Programs.Hello };

            using var response = await ToolchainService.PostCompileAsync(new { files, entry = "main.🍇", packages = new[] { "no-such-package" } }, Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, Cancellation);
        }

        [Fact]
        public async Task The_package_list_includes_the_standard_package()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.Http.GetAsync("packages", Cancellation);
            var names = (await ToolchainService.ReadJsonAsync(response, Cancellation)).EnumerateArray().Select(n => n.GetString()).ToList();

            names.ShouldContain("s");
        }

        [Fact]
        public async Task Package_documentation_is_the_compilers_report_with_types_and_methods()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.Http.GetAsync("packages/s/documentation.json", Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            ToolchainService.MediaType(response).ShouldBe("application/json");
            var types = (await ToolchainService.ReadJsonAsync(response, Cancellation)).GetProperty("types").EnumerateArray().ToList();
            var stringType = types.First(t => t.GetProperty("name").GetString() == "🔡");
            stringType.GetProperty("methods").GetArrayLength().ShouldBeGreaterThan(0);
        }

        [Fact]
        public async Task Documentation_for_an_unknown_package_is_a_404()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.Http.GetAsync("packages/no-such-package/documentation.json", Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }
}
