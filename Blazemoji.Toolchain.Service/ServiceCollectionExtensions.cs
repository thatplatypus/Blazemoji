using Blazemoji.Toolchain.Http;
using Blazemoji.Toolchain.Local;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Blazemoji.Toolchain.Service
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddToolchainService(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<ToolchainOptions>(configuration.GetSection(ToolchainOptions.SectionName));
            services.Configure<ToolchainServiceOptions>(configuration.GetSection(ToolchainServiceOptions.SectionName));

            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<LocalToolchain>();
            services.AddSingleton<IToolchain>(provider => provider.GetRequiredService<LocalToolchain>());
            services.AddSingleton<IBuildStore>(provider => provider.GetRequiredService<LocalToolchain>());
            services.AddSingleton<RunRegistry>();
            services.AddSingleton<CompileGate>();
            services.AddSingleton<PackageCatalog>();
            services.AddHostedService<RunPumpService>();

            services.AddProblemDetails();
            services.ConfigureHttpJsonOptions(json =>
            {
                json.SerializerOptions.PropertyNamingPolicy = ToolchainJson.Options.PropertyNamingPolicy;
                json.SerializerOptions.DefaultIgnoreCondition = ToolchainJson.Options.DefaultIgnoreCondition;
                json.SerializerOptions.Encoder = ToolchainJson.Options.Encoder;
            });

            return services;
        }
    }
}
