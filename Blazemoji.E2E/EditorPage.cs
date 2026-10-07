using System.Text.Json;

namespace Blazemoji.E2E
{
    public sealed record EditorMarker(int StartLineNumber, int StartColumn, int EndLineNumber, int EndColumn, string Message, int Severity);

    public sealed record CursorPosition(int LineNumber, int Column);

    /// <summary>
    /// What a person can see and do on the Blazemoji page, in the page's own terms.
    /// </summary>
    public sealed class EditorPage : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly IBrowserContext _context;
        private readonly List<string> _consoleErrors = [];

        private EditorPage(IBrowserContext context, IPage page)
        {
            _context = context;
            Page = page;
            page.Console += (_, message) =>
            {
                if (message.Type == "error")
                    _consoleErrors.Add(message.Text);
            };
            page.PageError += (_, error) => _consoleErrors.Add(error);
        }

        public IPage Page { get; }

        public IReadOnlyList<string> ConsoleErrors => _consoleErrors;

        public ILocator RunButton => Page.GetByTestId("run-button");

        public ILocator StopButton => Page.GetByTestId("stop-button");

        public ILocator RunStatus => Page.GetByTestId("run-status");

        public ILocator OutputLines => Page.GetByTestId("output-line");

        public ILocator DroppedLinesNote => Page.GetByTestId("output-dropped");

        public ILocator Problems => Page.GetByTestId("problem");

        public ILocator EditorText => Page.Locator(".monaco-editor .view-lines");

        public ILocator Dialog => Page.Locator(".mud-dialog");

        public ILocator ThemeToggle => Page.Locator("header button.mud-icon-button").First;

        /// <summary>
        /// The icon buttons in the editor toolbar, leaving out the emoji picker's own button:
        /// key commands first, then save.
        /// </summary>
        private ILocator ToolbarIconButtons =>
            Page.Locator(".mud-toolbar", new PageLocatorOptions { HasTextString = "Emojicode Editor" })
                .Locator("button.mud-icon-button:not([aria-label=\"Open Emoji Picker\"])");

        public ILocator KeyCommandsButton => ToolbarIconButtons.Nth(0);

        public ILocator SaveButton => ToolbarIconButtons.Nth(1);

        /// <param name="beforeNavigation">Runs on the new page before it loads, for example to slow a request down.</param>
        public static async Task<EditorPage> OpenAsync(
            BrowserFixture fixture,
            ColorScheme colorScheme = ColorScheme.Light,
            Func<IPage, Task>? beforeNavigation = null,
            float readyTimeoutMilliseconds = 60_000)
        {
            var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1600, Height = 900 },
                ColorScheme = colorScheme,
            });
            context.SetDefaultTimeout(60_000);

            var page = await context.NewPageAsync();
            var editorPage = new EditorPage(context, page);
            if (beforeNavigation is not null)
                await beforeNavigation(page);

            await page.GotoAsync(BrowserFixture.BaseUrl!);
            try
            {
                await editorPage.EditorText.GetByText("Hello World!").WaitForAsync(new LocatorWaitForOptions { Timeout = readyTimeoutMilliseconds });
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }

            return editorPage;
        }

        public Task SetCodeAsync(string code) =>
            Page.EvaluateAsync("code => monaco.editor.getModels()[0].setValue(code)", code);

        public Task<string> GetCodeAsync() =>
            Page.EvaluateAsync<string>("() => monaco.editor.getModels()[0].getValue()");

        /// <summary>
        /// Markers reach the editor one round trip after the problems list is drawn, so they are waited for.
        /// </summary>
        public Task WaitForMarkerCountAsync(int count) =>
            Page.WaitForFunctionAsync("count => monaco.editor.getModelMarkers({}).length === count", count);

        public async Task<IReadOnlyList<EditorMarker>> MarkersAsync()
        {
            var json = await Page.EvaluateAsync<string>("() => JSON.stringify(monaco.editor.getModelMarkers({}))");
            return JsonSerializer.Deserialize<List<EditorMarker>>(json, Json) ?? [];
        }

        public async Task<CursorPosition> CursorAsync()
        {
            var json = await Page.EvaluateAsync<string>("() => JSON.stringify(monaco.editor.getEditors()[0].getPosition())");
            return JsonSerializer.Deserialize<CursorPosition>(json, Json)!;
        }

        /// <summary>
        /// A click travels to the server and back before the editor moves, so the cursor is waited for.
        /// </summary>
        public Task WaitForCursorAsync(int line, int column) =>
            Page.WaitForFunctionAsync(
                "([line, column]) => { const position = monaco.editor.getEditors()[0].getPosition(); return position.lineNumber === line && position.column === column; }",
                new[] { line, column });

        public async Task OpenTabAsync(string name)
        {
            var tab = Page.Locator(".mud-tab", new PageLocatorOptions { HasTextString = name });

            // A Library row's code preview can cover the tabs, and the tab is often open already.
            if (await tab.GetAttributeAsync("aria-selected") != "true")
                await tab.ClickAsync();
        }

        public async Task LoadSampleAsync(string name)
        {
            await OpenTabAsync("Library");
            await Page.Locator(".mud-treeview-item", new PageLocatorOptions { HasTextString = name }).Last.ClickAsync();
        }

        public async Task RunAsync(string code)
        {
            await SetCodeAsync(code);
            await RunButton.ClickAsync();
        }

        public Task WaitForStatusAsync(string text) => RunStatus.GetByText(text).WaitForAsync();

        public async Task ScreenshotAsync(string name)
        {
            // Popovers and dialogs fade in; wait so the picture shows the settled state.
            await Page.WaitForTimeoutAsync(400);
            await Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(BrowserFixture.ScreenshotDirectory, name + ".png") });
        }

        public async ValueTask DisposeAsync() => await _context.DisposeAsync();
    }
}
