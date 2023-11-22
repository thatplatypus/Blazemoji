using Blazemoji.Compiler;
using Blazemoji.Services.Compiler;
using Blazemoji.Services.Execution;
using MassTransit;
using System.Text.Encodings.Web;
using System.Text.Unicode;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging();

builder.Services.AddScoped<ICompilerService, CompilerService>();
builder.Services.AddScoped<ICodeExecutionService, CodeExecutionService>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumersFromNamespaceContaining(typeof(CodeExecutionConsumer));

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.ConfigureJsonSerializerOptions(options =>
        {
            options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
            options.WriteIndented = true;
            return options;
        });

        cfg.Host("host.docker.internal", "/", h => {
            {
                h.Username("guest");
                h.Password("guest");
            } });

        cfg.ConfigureEndpoints(context);
    });

    //x.UsingAzureServiceBus((context, cfg) =>
    //{
    //    cfg.Host(builder.Configuration.GetConnectionString("AzureServiceBus"));
    //
    //    cfg.ConfigureEndpoints(context);
    //});
});

//builder.Services.AddHostedService<CodeExecutionConsumer>();

var host = builder.Build();
host.Run();