using System.ComponentModel;
using Blazemoji.Services;
using Hermes;
using Microsoft.Extensions.Logging;

namespace Blazemoji.Desktop.Services;

/// <summary>Opens a link in the system's browser. In the app's own window it would take the editor away.</summary>
public sealed class HermesExternalLinks(ILogger<HermesExternalLinks> logger) : IExternalLinks
{
    public Task OpenAsync(string url)
    {
        try
        {
            HermesApplication.OpenUrl(url);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException or IOException)
        {
            // A system with nothing set to open links. The app carries on without the page.
            logger.LogWarning(exception, "The link {Url} could not be opened", url);
        }

        return Task.CompletedTask;
    }
}
