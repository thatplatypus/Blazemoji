using Blazemoji.Toolchain.Service;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddToolchainService(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapToolchainEndpoints();
app.Run();
