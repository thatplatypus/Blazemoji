// SPIKE. Drives the page as a person would, as far as script can, and reports what happened.
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

async function until(check, ms = 15000) {
    const end = Date.now() + ms;
    while (Date.now() < end) {
        try { const value = check(); if (value) return value; } catch { }
        await sleep(100);
    }
    return null;
}

const editor = () => monaco.editor.getEditors()[0];
const text = () => editor().getModel().getValue();
const numbers = colour => (colour.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number).join(',');

function put(value, line, column) {
    editor().getModel().setValue(value);
    editor().setPosition({ lineNumber: line, column });
    editor().focus();
}

export async function probe() {
    const out = { userAgent: navigator.userAgent, steps: {} };
    const step = async (name, run) => { try { out.steps[name] = await run(); } catch (error) { out.steps[name] = 'THREW ' + error; } };

    const launches = Number(localStorage.getItem('spike.launches') ?? '0') + 1;
    localStorage.setItem('spike.launches', String(launches));
    out.launches = launches;
    out.storedKeysAtStart = Object.keys(localStorage).filter(key => key.startsWith('blazemoji.')).length;

    await step('1 monaco loads and the editor shows the template', async () => {
        const ready = await until(() => typeof monaco !== 'undefined' && text().includes('Hello World'), 30000);
        return ready
            ? 'yes: ' + text().split('\n').filter(line => line.trim()).slice(-3).join(' | ')
            : 'NO. monaco=' + (typeof monaco) + ' editors=' + (typeof monaco === 'undefined' ? '-' : monaco.editor.getEditors().length) + ' page says: ' + document.body.innerText.slice(0, 300);
    });
    if (typeof monaco === 'undefined' || !editor()) {
        out.problems = window.__problems;
        return JSON.stringify(out);
    }

    await step('2 style sheets and the bundled font', async () => ({
        sheets: [...document.styleSheets].map(sheet => { let rules = -1; try { rules = sheet.cssRules.length; } catch { } return (sheet.href ?? 'inline').split('/').pop() + ':' + rules; }).filter(name => !name.startsWith('inline')),
        nunitoFaces: (await document.fonts.load('16px Nunito')).length,
        bodyFont: getComputedStyle(document.body).fontFamily.slice(0, 40),
    }));

    await step('3 the editor has the page colours (light)', async () => {
        const palette = getComputedStyle(document.documentElement);
        await until(() => numbers(getComputedStyle(document.querySelector('.monaco-editor .monaco-editor-background')).backgroundColor) === numbers(palette.getPropertyValue('--mud-palette-surface')), 5000);
        return { editor: numbers(getComputedStyle(document.querySelector('.monaco-editor .monaco-editor-background')).backgroundColor), surface: numbers(palette.getPropertyValue('--mud-palette-surface')) };
    });

    await step('4 a key for an emoji (Shift+[ as a synthetic key)', async () => {
        put('🏁 ', 1, 4);
        const target = editor().getDomNode().querySelector('textarea.inputarea') ?? document.activeElement;
        target.dispatchEvent(new KeyboardEvent('keydown', { key: '{', code: 'BracketLeft', keyCode: 219, which: 219, shiftKey: true, bubbles: true, cancelable: true }));
        const paired = await until(() => text() === '🏁 🍇🍉', 4000);
        return paired ? 'typed 🍇🍉 with the cursor at ' + editor().getPosition() : 'left ' + JSON.stringify(text()) + ' (a synthetic key may simply not be accepted)';
    });

    await step('5 an emoji from the toolbox (through .NET and back)', async () => {
        put('🏁 ', 1, 4);
        const tab = [...document.querySelectorAll('[role=tab]')].find(candidate => candidate.textContent.includes('Toolbox'));
        tab?.click();
        const tile = await until(() => [...document.querySelectorAll('.emoji-tile-insert')].find(button => button.textContent.includes('🍇')), 8000);
        if (!tile) return 'no 🍇 tile found; tabs: ' + [...document.querySelectorAll('[role=tab]')].map(candidate => candidate.textContent.trim()).join(',');
        const started = performance.now();
        tile.click();
        const paired = await until(() => text() === '🏁 🍇🍉', 4000);
        return paired ? `typed 🍇🍉 in ${Math.round(performance.now() - started)} ms, cursor at ${editor().getPosition()}` : 'left ' + JSON.stringify(text());
    });

    await step('6 completion asks .NET', async () => {
        put('🏁 🍇\n  \n🍉', 2, 3);
        editor().trigger('spike', 'editor.action.triggerSuggest', {});
        const rows = await until(() => document.querySelectorAll('.suggest-widget.visible .monaco-list-row').length, 8000);
        editor().trigger('spike', 'hideSuggestWidget', {});
        return rows ? rows + ' suggestions shown' : 'no suggestions';
    });

    await step('7 compile and run through the toolchain service', async () => {
        put('🏁 🍇\n  😀 🔤Hello from a desktop window🔤❗️\n🍉\n', 1, 1);
        const status = () => document.querySelector('[data-testid=run-status]')?.textContent?.trim() ?? 'no status element';
        const button = () => document.querySelector('[data-testid=run-button]');
        const seen = [status()];
        const note = () => { if (seen[seen.length - 1] !== status()) seen.push(status()); };
        const enabled = await until(() => { note(); return button() && !button().disabled; }, 20000);
        await sleep(1500);
        const before = { exists: Boolean(button()), disabled: button()?.disabled ?? null, tag: button()?.tagName ?? null };
        button()?.click();
        const done = await until(() => { note(); return /Exited/i.test(status()); }, 60000);
        return { button: before, enabledInTime: Boolean(enabled), markers: monaco.editor.getModelMarkers({}).length, snackbars: [...document.querySelectorAll('.mud-snackbar')].map(bar => bar.textContent.trim().slice(0, 120)), statuses: seen, output: [...document.querySelectorAll('[data-testid=output-line]')].map(line => line.textContent).slice(0, 5), finished: Boolean(done) };
    });

    await step('8 the project is kept in the web view\'s local storage', async () => {
        await sleep(1500);
        return Object.keys(localStorage).filter(key => key.startsWith('blazemoji.')).length + ' keys';
    });

    await step('9 dark mode reaches the editor', async () => {
        document.querySelector('[data-testid=spike-dark]')?.click();
        const dark = await until(() => document.querySelector('.monaco-editor.vs-dark'), 5000);
        const palette = getComputedStyle(document.documentElement);
        return { darkClass: Boolean(dark), editor: numbers(getComputedStyle(document.querySelector('.monaco-editor .monaco-editor-background')).backgroundColor), surface: numbers(palette.getPropertyValue('--mud-palette-surface')) };
    });

    await step('10 the clipboard (no click behind it, so a refusal here is not the last word)', async () => {
        try {
            await navigator.clipboard.writeText('from the spike');
            return 'writeText accepted; readText: ' + await navigator.clipboard.readText().catch(error => 'refused (' + error.name + ')');
        } catch (error) {
            return 'writeText refused: ' + error.name + ' ' + error.message;
        }
    });

    out.problems = window.__problems;
    return JSON.stringify(out);
}
