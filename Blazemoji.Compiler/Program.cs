using Blazemoji.Compiler;
using Blazemoji.Services.Compiler;
using MassTransit;
using System.Text.Encodings.Web;
using System.Text.Unicode;

var builder = Host.CreateApplicationBuilder(args);

bool isContainer = bool.TryParse(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), out bool inContainer) && inContainer;

builder.Services.AddLogging();

builder.Services.AddScoped<ICompilerService, CompilerService>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumersFromNamespaceContaining(typeof(CodeExecutionConsumer));

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(isContainer ? "host.docker.internal" : "localhost", "/", h => {
            {
                h.Username("guest");
                h.Password("guest");
            } });

        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();