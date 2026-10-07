using Blazemoji.Services.Library;
using Blazemoji.Services.Projects;
using Blazemoji.Toolchain.Http;
using Blazored.LocalStorage;
using Hermes.Blazor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace Blazemoji.Desktop;

// SPIKE. The same registrations as the web host's Program.cs, in a Hermes window.
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // The web host's LibraryService reads its samples from a path relative to the
        // working directory, which for a desktop app is wherever it was started from.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        var builder = HermesBlazorAppBuilder.CreateDefault(args);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ToolchainClient:BaseUrl"] = Environment.GetEnvironmentVariable("BLAZEMOJI_TOOLCHAIN") ?? "http://127.0.0.1:5290",
        });

        builder.ConfigureWindow(options =>
        {
            options.Title = "Blazemoji (desktop spike)";
            options.Width = 1500;
            options.Height = 950;
            options.DevToolsEnabled = true;
        });

        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services.AddMudServices();
        builder.Services.AddToolchainClient(builder.Configuration);
        builder.Services.AddBlazemojiEditor(builder.Configuration);

        builder.Services.AddBlazoredLocalStorage();
        builder.Services.AddScoped<IProjectStore, LocalStorageProjectStore>();
        builder.Services.AddTransient<ILibraryService, LibraryService>();

        builder.RootComponents.Add<App>("#app");

        var app = builder.Build();
        app.Run();
    }
}
