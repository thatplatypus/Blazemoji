using Blazemoji.Services;
using Hermes;

namespace Blazemoji.Desktop.Services;

/// <summary>Opens a link in the system's browser. In the app's own window it would take the editor away.</summary>
public sealed class HermesExternalLinks : IExternalLinks
{
    public Task OpenAsync(string url)
    {
        HermesApplication.OpenUrl(url);
        return Task.CompletedTask;
    }
}
