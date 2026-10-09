// The desktop app looking at itself from inside its own window, for a smoke test: nothing
// outside the window can see into it. Each check drives the page as a person would, as far
// as script can, and says in a sentence what went wrong when it fails.
const sleep = (ms) => new Promise(resolve => setTimeout(resolve, ms));
async function until(look, ms) {
    const end = Date.now() + ms;
    while (Date.now() < end) {
        try {
            const seen = look();
            if (seen)
                return seen;
        }
        catch {
            // Not there yet.
        }
        await sleep(100);
    }
    return null;
}
function must(condition, otherwise) {
    if (!condition)
        throw new Error(otherwise);
}
const editor = () => monaco.editor.getEditors()[0];
const text = () => editor().getModel().getValue();
const numbers = (colour) => (colour.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number).join(",");
const editorGround = () => numbers(getComputedStyle(document.querySelector(".monaco-editor .monaco-editor-background")).backgroundColor);
const pageSurface = () => numbers(getComputedStyle(document.documentElement).getPropertyValue("--mud-palette-surface"));
const testId = (id) => document.querySelector(`[data-testid=${id}]`);
function put(value, line, column) {
    editor().getModel().setValue(value);
    editor().setPosition({ lineNumber: line, column });
    editor().focus();
}
const windowSize = () => `The window is ${window.innerWidth} by ${window.innerHeight}.`;
// In a wide window the Toolbox has a tab of its own. In a narrow one, which is what a build
// machine's small screen gives, the strip shows only the open tab and a menu of them all.
async function openTheToolbox() {
    const named = (candidates) => [...candidates].find(candidate => candidate.textContent?.includes("Toolbox"));
    const tab = named(document.querySelectorAll("[role=tab]"));
    if (tab) {
        tab.click();
        return;
    }
    const menu = document.querySelector("[data-testid=more-tabs] button");
    must(menu, `There is no Toolbox tab, and no menu of tabs to find it in. ${windowSize()}`);
    menu.click();
    const item = await until(() => named(document.querySelectorAll(".mud-popover-open .mud-menu-item")), 5000);
    must(item, `The menu of tabs has no Toolbox in it. ${windowSize()}`);
    item.click();
}
const checks = [
    ["editor-shows-the-project", async () => {
            must(await until(() => typeof monaco !== "undefined" && editor(), 30000), "Monaco did not load, or no editor was made.");
            must(await until(() => text().trim().length > 0, 15000), "The editor stayed empty: no project file reached it.");
            must(testId("open-file")?.textContent?.trim(), "The name of the open file is not shown.");
        }],
    ["style-sheets-and-font", async () => {
            const sheets = [...document.styleSheets].filter(sheet => sheet.href);
            const empty = sheets.filter(sheet => { try {
                return sheet.cssRules.length === 0;
            }
            catch {
                return true;
            } }).map(sheet => sheet.href.split("/").pop());
            must(sheets.length >= 4, `Only ${sheets.length} style sheets are linked.`);
            must(empty.length === 0, `Style sheets with nothing in them: ${empty.join(", ")}.`);
            must((await document.fonts.load("16px Nunito")).length > 0, "The bundled font did not load.");
        }],
    ["editor-has-the-page-colours", async () => {
            must(await until(() => editorGround() === pageSurface(), 5000), `The editor is ${editorGround()} on a page that is ${pageSurface()}.`);
        }],
    ["a-key-types-a-pair", async () => {
            put("🏁 ", 1, 4);
            const target = editor().getDomNode().querySelector("textarea.inputarea") ?? document.activeElement;
            target.dispatchEvent(new KeyboardEvent("keydown", { key: "{", code: "BracketLeft", keyCode: 219, which: 219, shiftKey: true, bubbles: true, cancelable: true }));
            must(await until(() => text() === "🏁 🍇🍉", 5000), `Shift and [ left ${JSON.stringify(text())}.`);
        }],
    ["toolbox-types-an-emoji", async () => {
            put("🏁 ", 1, 4);
            await openTheToolbox();
            const tile = await until(() => [...document.querySelectorAll(".emoji-tile-insert")].find(button => button.textContent?.includes("🍇")), 10000);
            must(tile, `The toolbox is open and has no 🍇 to press. ${windowSize()}`);
            tile.click();
            must(await until(() => text() === "🏁 🍇🍉", 5000), `Pressing 🍇 in the toolbox left ${JSON.stringify(text())}.`);
        }],
    ["completion-answers", async () => {
            put("🏁 🍇\n  \n🍉", 2, 3);
            editor().trigger("smoke", "editor.action.triggerSuggest", {});
            const rows = await until(() => document.querySelectorAll(".suggest-widget.visible .monaco-list-row").length, 10000);
            editor().trigger("smoke", "hideSuggestWidget", {});
            must(rows, "Asking for suggestions showed none.");
        }],
    ["dark-mode-reaches-the-editor", async () => {
            const toggle = testId("dark-mode-toggle");
            must(toggle, "There is no dark mode button.");
            const wasDark = Boolean(document.querySelector(".monaco-editor.vs-dark"));
            toggle.click();
            must(await until(() => Boolean(document.querySelector(".monaco-editor.vs-dark")) !== wasDark && editorGround() === pageSurface(), 5000), `After the button the editor is ${editorGround()} on a page that is ${pageSurface()}.`);
            toggle.click();
            must(await until(() => Boolean(document.querySelector(".monaco-editor.vs-dark")) === wasDark, 5000), "The editor did not go back.");
        }],
    ["a-dragged-divider-is-heard", async () => {
            // The mouse is stood in for, as the keys are above: nothing outside the window can
            // reach into it. The panel moves the divider; the app has to hear where it was let
            // go and settle the sidebar there, which is its own script calling back into .NET.
            const columns = testId("workspace-columns");
            const divider = columns?.querySelector(":scope > * > .split-view-divider");
            const sidebar = columns?.querySelector(":scope > * > .split-view-first");
            must(columns && divider && sidebar, "The workspace has no divider beside its sidebar.");
            const share = () => columns.style.getPropertyValue("--split-share").trim();
            const before = { share: share(), width: sidebar.getBoundingClientRect().width };
            const at = divider.getBoundingClientRect();
            const mouse = (x) => ({ clientX: x, clientY: at.top + at.height / 2, bubbles: true, cancelable: true });
            divider.dispatchEvent(new MouseEvent("mousedown", mouse(at.left + 2)));
            document.dispatchEvent(new MouseEvent("mousemove", mouse(at.left + 42)));
            document.dispatchEvent(new MouseEvent("mousemove", mouse(at.left + 82)));
            document.dispatchEvent(new MouseEvent("mouseup", mouse(at.left + 82)));
            const settled = () => share() !== before.share && sidebar.style.width === "100%";
            must(await until(settled, 10000), `The app did not take up where the divider was let go: the share is ${share()} and the sidebar is held at ${sidebar.style.width || "nothing"}. ${windowSize()}`);
            const moved = sidebar.getBoundingClientRect().width - before.width;
            must(Math.abs(moved - 80) < 3, `The sidebar is ${moved.toFixed(1)} pixels wider after a drag of 80. ${windowSize()}`);
        }],
];
const compiles = ["compiles-and-runs", async () => {
        put("🏁 🍇\n  😀 🔤Hello from a desktop window🔤❗️\n🍉\n", 1, 1);
        const status = () => testId("run-status")?.textContent?.trim() ?? "";
        const button = () => testId("run-button");
        must(await until(() => button() && !button().disabled, 20000), "The Run button never became ready.");
        await sleep(1500);
        button().click();
        must(await until(() => /Exited/i.test(status()), 90000), `The run did not finish. The status reads "${status()}".`);
        const output = [...document.querySelectorAll("[data-testid=output-line]")].map(line => line.textContent ?? "");
        must(output.some(line => line.includes("Hello from a desktop window")), `The output was ${JSON.stringify(output.slice(0, 5))}, and the status "${status()}".`);
    }];
/** Runs every check in turn. One that fails does not stop the ones after it. */
export async function run(alsoCompile) {
    const outcomes = [];
    for (const [name, check] of alsoCompile ? [...checks, compiles] : checks) {
        const started = performance.now();
        let error = null;
        try {
            await check();
        }
        catch (thrown) {
            error = thrown instanceof Error ? thrown.message : String(thrown);
            if (document.hidden) {
                // A web view whose window is covered, minimized or behind a locked screen
                // stops drawing and slows its timers, and several checks wait for something
                // to be drawn.
                error += " The window was not in view at the time, and a page that is not in view does not draw: run it again with the window showing.";
            }
        }
        outcomes.push({ name, passed: error === null, durationMs: Math.round(performance.now() - started), error });
    }
    return outcomes;
}
