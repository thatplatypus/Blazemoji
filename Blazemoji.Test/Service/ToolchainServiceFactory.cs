using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Service;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Blazemoji.Test.Service
{
    /// <summary>
    /// The real service hosted in memory, with the toolchain replaced by one the test scripts.
    /// </summary>
    public sealed class ToolchainServiceFactory : WebApplicationFactory<ToolchainServiceOptions>
    {
        public const string KnownBuild = "0123456789abcdef0123456789abcdef";

        private readonly string _packageDocs = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "package-docs-" + Guid.NewGuid().ToString("N"));

        public IToolchain Toolchain { get; } = Substitute.For<IToolchain, IBuildStore>();

        public FakeTimeProvider Time { get; } = new();

        public int MaxConcurrentRuns { get; set; } = 8;

        public int MaxConcurrentCompiles { get; set; } = 4;

        public long MaxProxiedRequestBytes { get; set; } = 1024 * 1024;

        public ToolchainServiceFactory()
        {
            ((IBuildStore)Toolchain).HasBuild(KnownBuild).Returns(true);

            Directory.CreateDirectory(Path.Combine(_packageDocs, "s"));
            File.WriteAllText(Path.Combine(_packageDocs, "s", "documentation.json"), """{"documentation":"","types":[{"name":"🔡"}]}""");
            Directory.CreateDirectory(Path.Combine(_packageDocs, "json"));
            File.WriteAllText(Path.Combine(_packageDocs, "json", "documentation.json"), """{"documentation":"","types":[]}""");
            Directory.CreateDirectory(Path.Combine(_packageDocs, "not-a-package"));
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");

            // For the tests that have the service listen on a real socket. The address is set
            // here, before the app is built, and not by asking the factory for a port: the
            // factory sets that after the build, by which time the app's own thread may have
            // started listening, and then it throws. It lost that race on a busy machine about
            // one run in twelve. The .NET container images also tell web apps to listen on
            // 8080, which every such test would then fight over, so that is cleared.
            builder.UseUrls("http://127.0.0.1:0");
            builder.UseSetting(WebHostDefaults.HttpPortsKey, string.Empty);
            builder.UseSetting(WebHostDefaults.HttpsPortsKey, string.Empty);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IToolchain>();
                services.RemoveAll<IBuildStore>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(Toolchain);
                services.AddSingleton((IBuildStore)Toolchain);
                services.AddSingleton<TimeProvider>(Time);
                services.Configure<ToolchainServiceOptions>(options =>
                {
                    options.MaxConcurrentRuns = MaxConcurrentRuns;
                    options.MaxConcurrentCompiles = MaxConcurrentCompiles;
                    options.MaxProxiedRequestBytes = MaxProxiedRequestBytes;
                    options.PackageDocumentationPath = _packageDocs;
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_packageDocs))
                Directory.Delete(_packageDocs, recursive: true);
        }
    }
}
