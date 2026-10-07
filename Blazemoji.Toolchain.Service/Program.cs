using System.Text;
using Blazemoji.Toolchain.Service;

using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddToolchainService(builder.Configuration);
builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
    var service = context.Configuration.GetSection(ToolchainServiceOptions.SectionName).Get<ToolchainServiceOptions>() ?? new ToolchainServiceOptions();
    kestrel.Limits.MaxRequestBodySize = service.MaxRequestBodyBytes;

    // A program's response headers are passed on as it wrote them, and an Emojicode program
    // may well write an emoji there.
    kestrel.ResponseHeaderEncodingSelector = _ => Encoding.UTF8;
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapToolchainEndpoints();
app.Run();
