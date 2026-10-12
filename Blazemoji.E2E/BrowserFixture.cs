namespace Blazemoji.E2E
{
    /// <summary>
    /// One browser for the whole run. The tests need a running Blazemoji whose address is in
    /// BLAZEMOJI_BASE_URL (scripts/e2e.sh starts one), and skip when it is not set.
    /// </summary>
    public sealed class BrowserFixture : IAsyncLifetime
    {
        public const string SkipReason = "Set BLAZEMOJI_BASE_URL to a running Blazemoji, or run scripts/e2e.sh.";

        public const string NoPackages = "GitHub or Packmoji's registry could not be reached, and this test fetches Grapevine's release.";

        // What a test that fetches Grapevine needs from outside: the release itself, and the
        // registry the website finds packages in.
        private static readonly string[] WhatFetchingNeeds =
        [
            "https://github.com/thatplatypus/grapevine/releases/download/grapevine-v0.2.0/grapevine-0.2.0.pmj.tar.gz",
            "https://raw.githubusercontent.com/thatplatypus/Packmoji/registry/v1/packages/thatplatypus/grapevine.json",
        ];

        private IPlaywright? _playwright;
        private IBrowser? _browser;

        public static string? BaseUrl => Environment.GetEnvironmentVariable("BLAZEMOJI_BASE_URL");

        public static string ScreenshotDirectory =>
            Environment.GetEnvironmentVariable("BLAZEMOJI_E2E_SCREENSHOTS")
            ?? Path.Combine(AppContext.BaseDirectory, "screenshots");

        public IBrowser Browser => _browser ?? throw new InvalidOperationException(SkipReason);

        /// <summary>
        /// Whether a test that fetches Grapevine's release can run here. It is asked from the
        /// machine the tests run on, which is not the web app's own: a place that lets one
        /// through and not the other fails the test where it was meant to be skipped.
        /// </summary>
        public static async Task<bool> PackagesCanBeFetchedAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                foreach (var address in WhatFetchingNeeds)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Head, address);
                    using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
                    if (!response.IsSuccessStatusCode)
                        return false;
                }

                return true;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                return false;
            }
        }

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
