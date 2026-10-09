using Blazemoji.Desktop.Services;
using Blazemoji.Desktop.Smoke;
using Blazemoji.Services;
using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
using Blazemoji.Shared.Models.Settings;
using Blazemoji.Shared.State;
using Blazemoji.Toolchain.Http;
using Hermes.Blazor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using Velopack;

namespace Blazemoji.Desktop;

public static class Program
{
    private const string EnvironmentVariable = "DOTNET_ENVIRONMENT";

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // An installed copy is started by its installer and updater with errands of their
            // own, which this runs and then exits. It has to come before anything else.
            VelopackApp.Build().Run();
        }
        catch (Exception exception)
        {
            // Expected wherever the app is not an installed copy: dotnet run, a plain publish.
            // Nothing has been built yet to log with, so standard error is all there is.
            Console.Error.WriteLine($"Not started as an installed copy ({exception.GetType().Name}).");
        }

        var isDevelopment = string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "Development", StringComparison.OrdinalIgnoreCase);
        var smoke = SmokeSettings.FromEnvironment();

        var builder = HermesBlazorAppBuilder.CreateDefault(args);
        if (smoke.IsEnabled)
        {
            // A smoke run types into the editor, and what is typed is saved. It is added last
            // so that it wins over wherever the app is otherwise set to keep projects: a smoke
            // run never writes into those.
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [FileProjectStoreOptions.SectionName + ":" + nameof(FileProjectStoreOptions.Root)] = smoke.ProjectsRoot,
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
            options.IconPath = WindowIcon.Path;
        });

        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.Logging.SetMinimumLevel(isDevelopment ? LogLevel.Information : LogLevel.Warning);

        // What any host of the editor registers. docs/hosting.md explains each line.
        builder.Services.AddMudServices();
        builder.Services.AddToolchainClient(builder.Configuration);
        builder.Services.AddBlazemojiProjectsOnDisk(builder.Configuration);

        // One user and one window, so one of each for the life of the app. Registered before
        // the editor's own call, which would make them one per scope as a server needs.
        builder.Services.AddSingleton<RunState>();
        builder.Services.AddSingleton<ProjectState>();
        builder.Services.AddSingleton<RequestState>();
        builder.Services.AddSingleton<LayoutState>();
        builder.Services.AddSingleton<SettingsState>();
        builder.Services.Configure<SettingsOptions>(settings => settings.Host = SettingHosts.Desktop);
        builder.Services.AddSingleton<LocalStorageFiles>();
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
