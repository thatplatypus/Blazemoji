using Blazemoji.Desktop.Services;
using Blazemoji.Desktop.Smoke;
using Blazemoji.Services;
using Blazemoji.Services.Projects;
using Blazemoji.Toolchain.Http;
using Hermes.Blazor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace Blazemoji.Desktop;

public static class Program
{
    private const string EnvironmentVariable = "DOTNET_ENVIRONMENT";

    [STAThread]
    public static void Main(string[] args)
    {
        var isDevelopment = string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "Development", StringComparison.OrdinalIgnoreCase);
        var smoke = SmokeSettings.FromEnvironment();

        var builder = HermesBlazorAppBuilder.CreateDefault(args);
        if (smoke.IsEnabled && string.IsNullOrEmpty(builder.Configuration[FileProjectStoreOptions.SectionName + ":" + nameof(FileProjectStoreOptions.Root)]))
        {
            // A smoke run types into the editor, and what is typed is saved. Unless it was told
            // where, it keeps that out of the user's own projects.
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [FileProjectStoreOptions.SectionName + ":" + nameof(FileProjectStoreOptions.Root)] = Path.Combine(Path.GetTempPath(), "blazemoji-smoke-" + Guid.NewGuid().ToString("N")),
            });
        }

        builder.ConfigureWindow(options =>
        {
            options.Title = "Blazemoji";
            options.Width = 1500;
            options.Height = 950;
            options.MinWidth = 1000;
            options.MinHeight = 600;
            options.CenterOnScreen = true;
            // An empty key has Hermes remember the window's size and place under its title.
            options.WindowStateKey = string.Empty;
            options.DevToolsEnabled = isDevelopment;
        });

        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.Logging.SetMinimumLevel(isDevelopment ? LogLevel.Information : LogLevel.Warning);

        // What any host of the editor registers. docs/hosting.md explains each line.
        builder.Services.AddMudServices();
        builder.Services.AddToolchainClient(builder.Configuration);
        builder.Services.AddBlazemojiProjectsOnDisk(builder.Configuration);
        builder.Services.AddBlazemojiEditor(builder.Configuration);

        // What this host supplies because it is a window and not a page in a browser.
        builder.Services.AddSingleton<IExternalLinks, HermesExternalLinks>();
        builder.Services.AddSingleton(smoke);
        builder.Services.AddSingleton<SmokeSession>();
        builder.Services.AddScoped<SmokeInterop>();

        builder.RootComponents.Add<App>("#app");

        var app = builder.Build();
        app.Services.GetRequiredService<SmokeSession>().Start();
        app.Run();
    }
}
