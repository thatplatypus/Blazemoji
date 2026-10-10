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
            page.PageError += (_, error) =>
            {
                // When the editor is given another file to show, Monaco abandons whatever it was
                // still working out for the previous one and reports that as a rejected promise.
                if (!error.StartsWith("Canceled: Canceled", StringComparison.Ordinal))
                    _consoleErrors.Add(error);
            };
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

        public ILocator ThemeToggle => Page.GetByTestId("dark-mode-toggle");

        public ILocator KeyCommandsButton => Page.GetByTestId("key-commands");

        public ILocator SaveButton => Page.GetByTestId("save-to-library");

        public ILocator SettingsButton => Page.GetByTestId("settings-button");

        /// <summary>The row of one setting in the open Settings dialog, by its property's name.</summary>
        public ILocator Setting(string name) => Page.Locator($"[data-testid=setting][data-setting={name}]");

        public async Task OpenSettingsAsync()
        {
            await SettingsButton.ClickAsync();
            await Page.GetByTestId("setting").First.WaitForAsync();
        }

        public async Task CloseSettingsAsync()
        {
            await Dialog.Locator(".mud-button-close").ClickAsync();
            await Dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        }

        /// <summary>The size the editor's text is drawn at, as the browser has it.</summary>
        public Task<string> FontSizeAsync() =>
            Page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.monaco-editor .view-lines')).fontSize");

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

        /// <summary>Replaces the text of the file the editor is showing.</summary>
        public Task SetCodeAsync(string code) =>
            Page.EvaluateAsync("code => monaco.editor.getEditors()[0].getModel().setValue(code)", code);

        public Task<string> GetCodeAsync() =>
            Page.EvaluateAsync<string>("() => monaco.editor.getEditors()[0].getModel().getValue()");

        public ILocator OpenFileCaption => Page.GetByTestId("open-file");

        public ILocator Files => Page.GetByTestId("file");

        public ILocator File(string path) => Page.Locator($"[data-testid=file][data-path=\"{path}\"]");

        public ILocator EntryFlag => Page.GetByTestId("entry-flag");

        public async Task<IReadOnlyList<string>> FilePathsAsync() =>
            await Files.EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.path)");

        /// <summary>
        /// The file list is drawn by its own component, a moment after the rest of the page
        /// has caught up with a change, so the list is waited for and not read once.
        /// </summary>
        public Task WaitForFilesAsync(params string[] paths) =>
            Page.WaitForFunctionAsync(
                "wanted => JSON.stringify([...document.querySelectorAll('[data-testid=file]')].map(row => row.dataset.path)) === JSON.stringify(wanted)",
                paths);

        /// <summary>Waits for the page to say which file is open.</summary>
        public Task WaitForOpenFileAsync(string project, string path) =>
            Page.Locator("[data-testid=open-file]", new PageLocatorOptions { HasTextString = $"{project} / {path}" }).WaitForAsync();

        /// <summary>Makes a project from a template and waits for its entry file to be showing.</summary>
        public async Task CreateProjectAsync(string name, string templateId, string entry = "main.🍇")
        {
            await OpenTabAsync("Files");
            await ShowsAnotherFileAsync(
                async () =>
                {
                    await Page.GetByTestId("project-menu").GetByRole(AriaRole.Button).ClickAsync();
                    await Page.GetByTestId("new-project").ClickAsync();
                    await Page.GetByTestId("new-project-name").FillAsync(name);
                    await Page.GetByTestId("template-" + templateId).ClickAsync();
                    await Page.GetByTestId("new-project-create").ClickAsync();
                    await Dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
                },
                name,
                entry);
        }

        public async Task<bool> HasTemplateAsync(string templateId)
        {
            await OpenTabAsync("Files");
            await Page.GetByTestId("project-menu").GetByRole(AriaRole.Button).ClickAsync();
            await Page.GetByTestId("new-project").ClickAsync();
            await Page.GetByTestId("new-project-name").WaitForAsync();
            var offered = await Page.GetByTestId("template-" + templateId).CountAsync() > 0;
            await Dialog.GetByText("Cancel").ClickAsync();
            await Dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
            return offered;
        }

        public async Task OpenFileAsync(string project, string path)
        {
            await OpenTabAsync("Files");
            if ((await OpenFileCaption.InnerTextAsync()).Trim() == $"{project} / {path}")
                return;

            await ShowsAnotherFileAsync(() => File(path).ClickAsync(), project, path);
        }

        /// <summary>
        /// Does something that makes the editor show a different file, and waits until it does.
        /// The caption changes when the page knows which file is open; the editor is told one
        /// round trip later, so its model changing is what says the file is really showing.
        /// </summary>
        public async Task ShowsAnotherFileAsync(Func<Task> action, string project, string path)
        {
            var shown = await ShownModelAsync();
            await action();
            await WaitForOpenFileAsync(project, path);
            await Page.WaitForFunctionAsync("before => monaco.editor.getEditors()[0].getModel().uri.toString() !== before", shown);
        }

        private Task<string> ShownModelAsync() =>
            Page.EvaluateAsync<string>("() => monaco.editor.getEditors()[0].getModel().uri.toString()");

        public async Task ChooseFileActionAsync(string path, string action)
        {
            await File(path).GetByTestId("file-menu").GetByRole(AriaRole.Button).ClickAsync();
            await Page.GetByTestId(action).ClickAsync();
        }

        public async Task AnswerPromptAsync(string text)
        {
            await Page.GetByTestId("prompt-input").FillAsync(text);
            await Page.GetByTestId("prompt-confirm").ClickAsync();
        }

        /// <summary>
        /// Sends a request from the Requests tab and waits for its answer to be shown.
        /// </summary>
        public async Task SendRequestAsync(string method, string path, string body = "")
        {
            await OpenTabAsync("Requests");
            // The select puts its test id on a hidden input as well as on the part people click.
            await Page.Locator("div[data-testid=request-method]").ClickAsync();
            await Page.Locator(".mud-popover-open .mud-list-item", new PageLocatorOptions { HasTextString = method }).First.ClickAsync();
            await Page.GetByTestId("request-path").FillAsync(path);
            await Page.GetByTestId("request-body").FillAsync(body);
            await Page.GetByTestId("request-send").ClickAsync();
            await Page.Locator("[data-testid=response-request]", new PageLocatorOptions { HasTextString = $"{method} {path}" }).WaitForAsync();
        }

        public ILocator Suggestions => Page.Locator(".suggest-widget.visible .monaco-list-row");

        public ILocator SuggestionDetails => Page.Locator(".suggest-details");

        public ILocator ParameterHints => Page.Locator(".parameter-hints-widget");

        public ILocator ActiveParameter => Page.Locator(".parameter-hints-widget .parameter.active");

        /// <summary>
        /// Puts the cursor on a new, empty line straight after the first line that contains
        /// <paramref name="text"/>, with the keyboard in the editor.
        /// </summary>
        public async Task StartLineAfterAsync(string text)
        {
            await Page.EvaluateAsync(
                """
                text => {
                    const editor = monaco.editor.getEditors()[0];
                    const line = editor.getModel().getLinesContent().findIndex(content => content.includes(text)) + 1;
                    editor.setPosition({ lineNumber: line, column: editor.getModel().getLineMaxColumn(line) });
                    editor.focus();
                }
                """,
                text);
            await Page.Keyboard.PressAsync("Enter");
        }

        /// <summary>Types as a person would, one key at a time, which is what makes suggestions appear.</summary>
        public Task TypeAsync(string text) => Page.Keyboard.TypeAsync(text, new KeyboardTypeOptions { Delay = 40 });

        public Task<string> LineAtCursorAsync() =>
            Page.EvaluateAsync<string>("() => { const editor = monaco.editor.getEditors()[0]; return editor.getModel().getLineContent(editor.getPosition().lineNumber); }");

        /// <summary>What each visible suggestion says: its label, what it belongs to, and its kind.</summary>
        public async Task<IReadOnlyList<string>> SuggestionTextsAsync()
        {
            await Suggestions.First.WaitForAsync();
            return await Suggestions.EvaluateAllAsync<string[]>("rows => rows.map(row => row.getAttribute('aria-label') ?? '')");
        }

        /// <summary>Opens the documentation beside the suggestion list, as Ctrl+Space does.</summary>
        public Task ShowSuggestionDetailsAsync() =>
            Page.EvaluateAsync("() => monaco.editor.getEditors()[0].trigger('test', 'toggleSuggestionDetails', {})");

        public ILocator ResponseStatus => Page.GetByTestId("response-status");

        public ILocator ResponseBody => Page.GetByTestId("response-body");

        public ILocator ResponseProblem => Page.GetByTestId("response-problem");

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
            var tab = Page.Locator("[role=tab]", new PageLocatorOptions { HasTextString = name });

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
