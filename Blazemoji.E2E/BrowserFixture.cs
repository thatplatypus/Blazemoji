namespace Blazemoji.E2E
{
    /// <summary>
    /// One browser for the whole run. The tests need a running Blazemoji whose address is in
    /// BLAZEMOJI_BASE_URL (scripts/e2e.sh starts one), and skip when it is not set.
    /// </summary>
    public sealed class BrowserFixture : IAsyncLifetime
    {
        public const string SkipReason = "Set BLAZEMOJI_BASE_URL to a running Blazemoji, or run scripts/e2e.sh.";

        private IPlaywright? _playwright;
        private IBrowser? _browser;

        public static string? BaseUrl => Environment.GetEnvironmentVariable("BLAZEMOJI_BASE_URL");

        public static string ScreenshotDirectory =>
            Environment.GetEnvironmentVariable("BLAZEMOJI_E2E_SCREENSHOTS")
            ?? Path.Combine(AppContext.BaseDirectory, "screenshots");

        public IBrowser Browser => _browser ?? throw new InvalidOperationException(SkipReason);

        public async ValueTask InitializeAsync()
        {
            if (BaseUrl is null)
                return;

            var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (exitCode != 0)
                throw new InvalidOperationException($"Installing Chromium for Playwright failed with exit code {exitCode}.");

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync();
            Directory.CreateDirectory(ScreenshotDirectory);
        }

        public async ValueTask DisposeAsync()
        {
            if (_browser is not null)
                await _browser.DisposeAsync();

            _playwright?.Dispose();
        }
    }
}
