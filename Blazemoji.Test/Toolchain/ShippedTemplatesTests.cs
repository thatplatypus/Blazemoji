using Blazemoji.Services.Projects;
using Blazemoji.Toolchain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// The project templates, compiled for real. They live with the other tests that start
    /// processes, which the Docker script runs apart from the rest.
    /// </summary>
    public class ShippedTemplatesTests
    {
        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task Every_template_that_ships_without_extra_packages_compiles()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();
            var templates = new FileProjectTemplates(Options.Create(new ProjectTemplateOptions()), NullLogger<FileProjectTemplates>.Instance).All;

            foreach (var template in templates.Where(template => template.Id != "grapevine-todo"))
            {
                var request = new CompileRequest(template.Files.ToDictionary(file => file.Path, file => file.Content), template.Entry);
                var build = await toolchain.CompileAsync(request, TestContext.Current.CancellationToken);

                build.Ok.ShouldBeTrue(template.Id + ": " + string.Join("; ", build.Diagnostics.Select(d => $"{d.File} {d.Line}:{d.Character} {d.Message}")));
            }
        }

        [Fact]
        [Trait("Category", "Toolchain")]
        public async Task The_two_file_template_prints_its_greeting()
        {
            Assert.SkipUnless(ToolchainFixture.Available, ToolchainFixture.SkipReason);
            await using var toolchain = ToolchainFixture.Create();
            var template = new FileProjectTemplates(Options.Create(new ProjectTemplateOptions()), NullLogger<FileProjectTemplates>.Instance)
                .All.Single(candidate => candidate.Id == "two-files");

            var build = await toolchain.CompileAsync(
                new CompileRequest(template.Files.ToDictionary(file => file.Path, file => file.Content), template.Entry),
                TestContext.Current.CancellationToken);
            build.Ok.ShouldBeTrue(string.Join("; ", build.Diagnostics.Select(d => $"{d.File} {d.Line}:{d.Character} {d.Message}")));
            await using var run = await toolchain.StartRunAsync(new RunRequest(build.BuildId!), TestContext.Current.CancellationToken);

            (await run.RunToEndAsync(TestContext.Current.CancellationToken)).Stdout.ShouldBe("Hello, World!\n");
        }
    }
}
