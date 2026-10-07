using Blazemoji.Toolchain.Service;

using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddToolchainService(builder.Configuration);
builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
    var service = context.Configuration.GetSection(ToolchainServiceOptions.SectionName).Get<ToolchainServiceOptions>() ?? new ToolchainServiceOptions();
    kestrel.Limits.MaxRequestBodySize = service.MaxRequestBodyBytes;
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapToolchainEndpoints();
app.Run();
