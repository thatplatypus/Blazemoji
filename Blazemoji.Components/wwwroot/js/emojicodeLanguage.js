// What the editor needs from Monaco for Emojicode: its completion, hover and signature help
// providers, its pairs and comments, typing, and its colours.
// Nothing here knows the language. The providers hand the text and the cursor's offset to
// .NET and turn the answer into the shape Monaco wants; the pairs and what a key types come
// from .NET too.
//
// Written in Scripts/emojicodeLanguage.ts and compiled by scripts/build-js.sh to
// wwwroot/js/emojicodeLanguage.js. The compiled file is committed so that building the app
// does not need Node. Edit the .ts file.
// The names of the .NET methods this module calls. They match EmojicodeLanguageInterop.
const COMPLETE = "CompleteAsync";
const HOVER = "HoverAsync";
const SIGNATURE = "SignatureAsync";
const SHOW_PARAMETERS = "editor.action.triggerParameterHints";
function rangeOf(model, start, end) {
    const from = model.getPositionAt(start);
    const to = model.getPositionAt(end);
    return new monaco.Range(from.lineNumber, from.column, to.lineNumber, to.column);
}
function kindOf(kind) {
    const kinds = monaco.languages.CompletionItemKind;
    switch (kind) {
        case "Method": return kinds.Method;
        case "Variable": return kinds.Variable;
        case "Keyword": return kinds.Keyword;
        default: return kinds.Text;
    }
}
const TYPED = "blazemoji.typed";
function editorWithId(editorId) {
    return monaco.editor.getEditors().find((candidate) => candidate.getContainerDomNode()?.id === editorId);
}
// Where the cursor and the scroll were in each file, by the address of its model. Monaco keeps
// these with the editor and not with the model, so showing another model forgets them.
const whereItWasLeft = new Map();
// Shows another file's model in the editor with this element id, and puts the cursor and the
// scroll back where they were when that file was last shown. All in one call, so that
// switching files is one round trip. False when there is no such editor or no such model.
export function showModel(editorId, modelUri) {
    const editor = editorWithId(editorId);
    const model = monaco.editor.getModels().find((candidate) => candidate.uri.toString() === modelUri);
    if (!editor || !model) {
        return false;
    }
    const leaving = editor.getModel();
    if (leaving !== model) {
        if (leaving) {
            whereItWasLeft.set(leaving.uri.toString(), editor.saveViewState());
        }
        editor.setModel(model);
        const left = whereItWasLeft.get(modelUri);
        if (left) {
            editor.restoreViewState(left);
        }
    }
    // A file that has gone takes its place in the list with it.
    const open = new Set(monaco.editor.getModels().map((candidate) => candidate.uri.toString()));
    for (const uri of [...whereItWasLeft.keys()]) {
        if (!open.has(uri)) {
            whereItWasLeft.delete(uri);
        }
    }
    return true;
}
// Names the text and the selection as they are now, so that an edit worked out from them can
// tell whether they are still the same when it arrives.
function stampOf(editor) {
    const model = editor.getModel();
    return `${model.uri}@${model.getVersionId()}@${editor.getSelection()}`;
}
// What is around the cursor of the editor in the element with this id: everything before the
// selection, the selection, and the rest of its last line. .NET works out from these what a
// key should type. Null when there is no such editor or it has nothing open.
export function aroundCursor(editorId) {
    const editor = editorWithId(editorId);
    const model = editor?.getModel();
    const selection = editor?.getSelection();
    if (!model || !selection) {
        return null;
    }
    const end = selection.getEndPosition();
    return {
        before: model.getValueInRange(new monaco.Range(1, 1, selection.startLineNumber, selection.startColumn)),
        selected: model.getValueInRange(selection),
        after: model.getLineContent(end.lineNumber).slice(end.column - 1),
        stamp: stampOf(editor),
    };
}
// Types into the editor in the element with this id and gives it the keyboard. An edit that
// was worked out from text or a selection that has since changed is not applied: what was
// typed goes in as it is, where the cursor is now.
export function type(editorId, typed) {
    const editor = editorWithId(editorId);
    const model = editor?.getModel();
    const selection = editor?.getSelection();
    if (!model || !selection) {
        return;
    }
    const stale = typed.stamp !== null && typed.stamp !== stampOf(editor);
    const edit = stale
        ? { removeBefore: 0, removeAfter: 0, text: typed.plain, selectionStart: typed.plain.length, selectionEnd: typed.plain.length }
        : typed;
    // Monaco writes every line end the way the file already does, which can make the text
    // shorter or longer than it was counted in .NET. The cursor is counted in what goes in.
    const asTheFileEndsLines = (text) => text.replace(/\r\n|\r|\n/g, model.getEOL());
    const start = model.getOffsetAt(selection.getStartPosition()) - edit.removeBefore;
    const end = model.getOffsetAt(selection.getEndPosition()) + edit.removeAfter;
    editor.pushUndoStop();
    editor.executeEdits(TYPED, [{ range: rangeOf(model, start, end), text: asTheFileEndsLines(edit.text), forceMoveMarkers: true }], () => {
        const from = model.getPositionAt(start + asTheFileEndsLines(edit.text.slice(0, edit.selectionStart)).length);
        const to = model.getPositionAt(start + asTheFileEndsLines(edit.text.slice(0, edit.selectionEnd)).length);
        return [new monaco.Selection(from.lineNumber, from.column, to.lineNumber, to.column)];
    });
    editor.pushUndoStop();
    editor.revealPosition(editor.getPosition());
    editor.focus();
}
// The browser's own knowledge of where one written character ends and the next begins.
const graphemes = typeof Intl !== "undefined" && "Segmenter" in Intl
    ? new Intl.Segmenter(undefined, { granularity: "grapheme" })
    : null;
// What marks a character made of several code points as an emoji: a pictograph or a flag
// letter, the selector that asks for the emoji form, a keycap, or the joiner between two emoji.
const EMOJI_PART = /\p{Extended_Pictographic}|\p{Regional_Indicator}|\uFE0F|\u20E3|\u200D/u;
// No emoji is longer than this many UTF-16 units, so this much text before the cursor is
// enough to find where the last one starts.
const LONGEST_EMOJI = 64;
// The range of the emoji that ends at the cursor, when it is one that Backspace would only
// take a part of: an emoji of more than one code point. Null for anything else.
function emojiEndingAtCursor(editor) {
    const selections = editor.getSelections();
    if (!graphemes || !selections || selections.length !== 1 || !selections[0].isEmpty()) {
        return null;
    }
    const position = selections[0].getPosition();
    const before = editor.getModel().getLineContent(position.lineNumber).slice(0, position.column - 1);
    const tail = before.slice(-LONGEST_EMOJI);
    let last = null;
    for (const piece of graphemes.segment(tail)) {
        last = piece;
    }
    if (!last || Array.from(last.segment).length < 2 || !EMOJI_PART.test(last.segment)) {
        return null;
    }
    const start = before.length - tail.length + last.index;
    return new monaco.Range(position.lineNumber, start + 1, position.lineNumber, position.column);
}
// What the text typed next at a position would be inside of, going by the colouring rules
// the editor already has: everything before the position is coloured with one more letter
// after it, and the letter's colour is the answer. That reads the file from its start, so
// it is only asked when Backspace sits between two halves of a pair.
function insideAt(model, position) {
    const before = model.getValueInRange(new monaco.Range(1, 1, position.lineNumber, position.column));
    const lines = monaco.editor.tokenize(before + "x", model.getLanguageId());
    const last = lines[lines.length - 1];
    const kind = last?.[last.length - 1]?.type ?? "";
    return kind.startsWith("string") ? "string" : kind.startsWith("comment") ? "comment" : "code";
}
// True when the text ends with an odd number of the escape mark: the next character is escaped.
function endsEscaping(text, escape) {
    let marks = 0;
    while (text.endsWith(escape.repeat(marks + 1))) {
        marks++;
    }
    return marks % 2 === 1;
}
// How many times a mark stands in the text without the escape mark in front of it.
function unescaped(text, mark, escape) {
    let found = 0;
    for (let at = text.indexOf(mark); at >= 0; at = text.indexOf(mark, at + mark.length)) {
        if (!endsEscaping(text.slice(0, at), escape)) {
            found++;
        }
    }
    return found;
}
// True when the cursor is between the two 🧲 of a value in a string and nothing is written
// there yet. Two 🧲 side by side can also be the end of one value and the start of the next,
// so the ones before the cursor are counted from where the string begins on this line: the
// one just before the cursor opens a value when their number is odd.
function isEmptyInterpolation(before, after, inside, syntax) {
    const [open, close] = syntax.interpolation;
    if (inside !== "string" || !before.endsWith(open) || !after.startsWith(close)) {
        return false;
    }
    const quote = syntax.completed.find(([opens, closes]) => opens === closes)?.[0];
    let begins = 0;
    for (let at = quote ? before.indexOf(quote) : -1; at >= 0; at = before.indexOf(quote, at + quote.length)) {
        if (!endsEscaping(before.slice(0, at), syntax.escape)) {
            begins = at + quote.length;
        }
    }
    return unescaped(before.slice(begins), open, syntax.escape) % 2 === 1;
}
// The range of an opener and its closer with the cursor between them and nothing else.
// Two different halves (🍇🍉) count in code. Two that are the same (🔤🔤) count when the
// first has just opened a string, and not when it ended one and the second begins another.
// The two 🧲 around a value count inside a string. Null for anything else.
function emptyPairAroundCursor(editor, syntax) {
    const selections = editor.getSelections();
    if (!selections || selections.length !== 1 || !selections[0].isEmpty()) {
        return null;
    }
    const position = selections[0].getPosition();
    const model = editor.getModel();
    const line = model.getLineContent(position.lineNumber);
    const before = line.slice(0, position.column - 1);
    const after = line.slice(position.column - 1);
    const pair = [...syntax.completed, syntax.interpolation].find(([open, close]) => before.endsWith(open) && after.startsWith(close));
    if (!pair) {
        return null;
    }
    const [open, close] = pair;
    const inside = insideAt(model, position);
    if (pair === syntax.interpolation) {
        return isEmptyInterpolation(before, after, inside, syntax)
            ? new monaco.Range(position.lineNumber, position.column - open.length, position.lineNumber, position.column + close.length)
            : null;
    }
    const empty = open === close
        ? inside === "string" && !endsEscaping(before.slice(0, before.length - open.length), syntax.escape)
        : inside === "code";
    return empty
        ? new monaco.Range(position.lineNumber, position.column - open.length, position.lineNumber, position.column + close.length)
        : null;
}
// Two things Monaco's Backspace does for other languages and not for this one.
//
// It takes a whole emoji only when the emoji's first character is in a table Monaco carries.
// Much of Emojicode is written with emoji that are not: ↩️ ↪️ ◀️ ▶️ ⬅️ ⁉️ are each a plain
// symbol followed by a selector, and Backspace took the selector and left the symbol; 🤷‍♀️
// lost its second half. Here Backspace takes such an emoji whole.
//
// And between an opener and the closer that came with it, it takes both, but only for
// brackets of one UTF-16 unit. Here it does for 🍇🍉 and the other pairs, and for the 🔤🔤 of
// a string that has just been opened: one 🔤 left behind would turn the rest of the file
// into a string.
//
// Every other case (a plain character, a selection, several cursors, an accent typed after
// its letter) is left to Monaco, by not touching the key at all.
function keepPairsAndEmojiWhole(editor, languageId, syntax) {
    return editor.onKeyDown((pressed) => {
        if (pressed.keyCode !== monaco.KeyCode.Backspace || pressed.ctrlKey || pressed.altKey || pressed.metaKey || pressed.browserEvent?.isComposing) {
            return;
        }
        if (editor.getModel()?.getLanguageId() !== languageId || editor.getOption(monaco.editor.EditorOption.readOnly)) {
            return;
        }
        const taken = emptyPairAroundCursor(editor, syntax) ?? emojiEndingAtCursor(editor);
        if (!taken) {
            return;
        }
        pressed.preventDefault();
        pressed.stopPropagation();
        editor.pushUndoStop();
        editor.executeEdits("blazemoji.backspace", [{ range: taken, text: "", forceMoveMarkers: true }]);
        editor.pushUndoStop();
    });
}
// ➕ ➖ ➗ ✖️ and a few like them are drawn by emoji fonts as one flat dark grey shape, which
// cannot be seen on a dark page. No colour setting reaches an emoji, so each one is marked
// and the style sheet turns the marked ones light when the editor is dark. The symbols that
// are only an emoji with a selector after them are matched with it; without it they are
// plain text and already take the text's colour. 🔜 and 🔚, which begin and end a block
// comment, are of the same kind. Black shapes with a meaning of their own (⚫ ⬛) are not
// here: turned light they would be their white twins.
const FLAT_DARK_GLYPHS = "➕|➖|➗|➰|➿|💱|💲|🔙|🔚|🔛|🔜|🔝|[✖✔〰™©®♠♣]\uFE0F";
// Monaco stops looking after 999 matches unless it is told how many to find.
const EVERY_MATCH = 1000000;
const FLAT_DARK_GLYPH_CLASS = "flat-dark-glyph";
function markFlatDarkGlyphs(editor, languageId) {
    const marks = editor.createDecorationsCollection();
    let waiting = 0;
    const mark = () => {
        waiting = 0;
        const model = editor.getModel();
        marks.set(model?.getLanguageId() === languageId
            ? model.findMatches(FLAT_DARK_GLYPHS, false, true, true, null, false, EVERY_MATCH).map((match) => ({ range: match.range, options: { inlineClassName: FLAT_DARK_GLYPH_CLASS } }))
            : []);
    };
    // Once for however many changes arrive before the next frame is drawn.
    const soon = () => {
        if (!waiting) {
            waiting = requestAnimationFrame(mark);
        }
    };
    mark();
    const listeners = [editor.onDidChangeModelContent(soon), editor.onDidChangeModel(soon)];
    return {
        dispose() {
            cancelAnimationFrame(waiting);
            listeners.forEach(listener => listener.dispose());
            marks.clear();
        },
    };
}
// What the editor does for each Monaco editor on the page.
function attach(editor, languageId, syntax) {
    const hooks = [keepPairsAndEmojiWhole(editor, languageId, syntax), markFlatDarkGlyphs(editor, languageId)];
    return { dispose: () => hooks.forEach(hook => hook.dispose()) };
}
export function register(languageId, dotNet, syntax) {
    // Editors that are already there, and any made later.
    const editorHooks = monaco.editor.getEditors().map((editor) => attach(editor, languageId, syntax));
    const registrations = [
        monaco.editor.onDidCreateEditor((editor) => editorHooks.push(attach(editor, languageId, syntax))),
        // From this Monaco shows which closer belongs to which opener, indents the line after
        // an opener, wraps a selection when an opener is typed by the system's own emoji
        // picker, and can comment lines out. It does not complete pairs: it finds a pair by
        // one UTF-16 unit and an emoji is two, so that is done in .NET (see type above).
        monaco.languages.setLanguageConfiguration(languageId, {
            comments: { lineComment: syntax.lineComment, blockComment: syntax.blockComment },
            brackets: syntax.matched,
            autoClosingPairs: [],
            surroundingPairs: syntax.completed.map(([open, close]) => ({ open, close })),
        }),
        monaco.languages.registerCompletionItemProvider(languageId, {
            triggerCharacters: [".", ":"],
            async provideCompletionItems(model, position) {
                const answers = await dotNet.invokeMethodAsync(COMPLETE, model.getValue(), model.getOffsetAt(position));
                return {
                    // The list depends on every letter typed, so Monaco is told to ask again.
                    incomplete: true,
                    suggestions: answers.map(answer => {
                        const range = rangeOf(model, answer.replaceStart, answer.replaceEnd);
                        return {
                            label: { label: answer.label, description: answer.detail },
                            kind: kindOf(answer.kind),
                            insertText: answer.insert,
                            range,
                            documentation: { value: answer.documentation },
                            sortText: String(answer.order).padStart(5, "0"),
                            // .NET has already chosen what matches. Matching the typed text
                            // against itself stops Monaco filtering the list a second time.
                            filterText: model.getValueInRange(range),
                            // A call that was just started wants its parameters shown.
                            command: answer.kind === "Method" && answer.insert.endsWith(" ")
                                ? { id: SHOW_PARAMETERS, title: "Show parameters" }
                                : undefined,
                        };
                    }),
                };
            },
        }),
        monaco.languages.registerHoverProvider(languageId, {
            async provideHover(model, position) {
                const answer = await dotNet.invokeMethodAsync(HOVER, model.getValue(), model.getOffsetAt(position));
                return answer
                    ? { range: rangeOf(model, answer.start, answer.end), contents: [{ value: answer.markdown }] }
                    : null;
            },
        }),
        monaco.languages.registerSignatureHelpProvider(languageId, {
            signatureHelpTriggerCharacters: [" "],
            signatureHelpRetriggerCharacters: [" "],
            async provideSignatureHelp(model, position) {
                const answer = await dotNet.invokeMethodAsync(SIGNATURE, model.getValue(), model.getOffsetAt(position));
                if (!answer) {
                    return null;
                }
                return {
                    value: {
                        signatures: [{
                                label: answer.label,
                                documentation: { value: answer.documentation },
                                parameters: answer.parameters.map(label => ({ label })),
                            }],
                        activeSignature: 0,
                        activeParameter: answer.activeParameter,
                    },
                    dispose() { },
                };
            },
        }),
    ];
    return {
        dispose() {
            registrations.forEach(registration => registration.dispose());
            editorHooks.forEach(hook => hook.dispose());
        },
    };
}
// A colour as MudBlazor writes it into the page: rgba(34,34,38,1), or #222226 with or without
// two more digits for how solid it is. Null for anything else.
function colourOf(value) {
    const text = value.trim();
    const hex = /^#([0-9a-f]{6})([0-9a-f]{2})?$/i.exec(text);
    if (hex) {
        const rgb = parseInt(hex[1], 16);
        return { r: rgb >> 16, g: (rgb >> 8) & 255, b: rgb & 255, a: hex[2] ? parseInt(hex[2], 16) / 255 : 1 };
    }
    const parts = /^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)$/i.exec(text);
    return parts
        ? { r: Number(parts[1]), g: Number(parts[2]), b: Number(parts[3]), a: parts[4] === undefined ? 1 : Number(parts[4]) }
        : null;
}
function twoDigits(part) {
    return Math.round(Math.min(255, Math.max(0, part))).toString(16).padStart(2, "0");
}
function hexOf(colour) {
    return `#${twoDigits(colour.r)}${twoDigits(colour.g)}${twoDigits(colour.b)}${colour.a < 1 ? twoDigits(colour.a * 255) : ""}`;
}
function faded(colour, by) {
    return { ...colour, a: colour.a * by };
}
// The solid colour that is seen when one colour lies over another.
function over(top, bottom) {
    const mix = (above, below) => above * top.a + below * (1 - top.a);
    return { r: mix(top.r, bottom.r), g: mix(top.g, bottom.g), b: mix(top.b, bottom.b), a: 1 };
}
function isDark(colour) {
    return (0.2126 * colour.r + 0.7152 * colour.g + 0.0722 * colour.b) / 255 < 0.5;
}
// How light a solid colour is, and how far apart two are, as WCAG measures them.
function lightness(colour) {
    const linear = (part) => {
        const share = part / 255;
        return share <= 0.03928 ? share / 12.92 : Math.pow((share + 0.055) / 1.055, 2.4);
    };
    return 0.2126 * linear(colour.r) + 0.7152 * linear(colour.g) + 0.0722 * linear(colour.b);
}
function contrast(one, other) {
    const [more, less] = [lightness(one), lightness(other)].sort((a, b) => b - a);
    return (more + 0.05) / (less + 0.05);
}
// The least contrast text may have with what it is drawn on.
const READABLE = 4.5;
// A colour washed over the surface, for a selection, a match and the like: as strong as
// asked for, or as much weaker as it takes for every kind of text to stay readable on it.
// A selection is drawn behind text of each colour the editor uses, so a wash that suits the
// page's text can still drown a string or a comment.
function wash(colour, strongest, surface, texts) {
    for (let strength = strongest; strength > 0.02; strength -= 0.02) {
        const washed = over(faded(colour, strength), surface);
        if (texts.every(text => contrast(text, washed) >= READABLE)) {
            return faded(colour, strength);
        }
    }
    return faded(colour, 0.02);
}
const LIGHT_THEME = "blazemoji-light";
const DARK_THEME = "blazemoji-dark";
// Gives every editor on the page the page's own colours, read from the palette MudBlazor
// writes into it. The editor then matches whatever theme its host has, light or dark, and
// .NET only has to say when the page's colours have changed. Emoji keep their own colours
// whatever is set here, so there is little to colour: what is written inside a string takes
// the accent, comments are quieter than code, and everything else is the page's text colour.
// A selection or a match is a wash of the page's second colour, never so strong that any of
// those three stops being readable on it.
// A page with no such palette is left with the theme Monaco has.
export function applyTheme() {
    const page = getComputedStyle(document.documentElement);
    const read = (name) => colourOf(page.getPropertyValue(`--mud-palette-${name}`));
    const surface = read("surface");
    const ink = read("text-primary");
    if (!surface || !ink) {
        return;
    }
    const quiet = read("text-secondary") ?? faded(ink, 0.7);
    const accent = read("primary") ?? ink;
    const lines = read("lines-default") ?? faded(ink, 0.14);
    const colours = {};
    const set = (colour, ...names) => {
        if (colour) {
            names.forEach(name => colours[name] = hexOf(colour));
        }
    };
    // The three colours text is written in, as they come out on the page.
    const texts = [over(ink, surface), over(quiet, surface), over(accent, surface)];
    const washed = (colour, strongest) => wash(colour, strongest, surface, texts);
    const marker = read("secondary") ?? accent;
    set(surface, "editor.background", "editorGutter.background", "editorWidget.background", "editorHoverWidget.background", "editorSuggestWidget.background", "minimap.background");
    set(texts[0], "editor.foreground", "editorLineNumber.activeForeground", "editorWidget.foreground", "editorSuggestWidget.foreground", "editorSuggestWidget.selectedForeground");
    set(texts[1], "editorLineNumber.foreground");
    set(accent, "editorCursor.foreground", "editor.findMatchBorder", "editorSuggestWidget.highlightForeground", "editorSuggestWidget.focusHighlightForeground", "editorLink.activeForeground", "focusBorder");
    set(washed(marker, 0.5), "editor.selectionBackground", "editor.findMatchBackground");
    set(washed(marker, 0.28), "editor.inactiveSelectionBackground", "editor.findMatchHighlightBackground");
    set(washed(accent, 0.18), "editorSuggestWidget.selectedBackground");
    // The mark on the two halves of a pair is only ever behind an emoji, so it is not held
    // to what text needs. The style sheet rounds it.
    set(faded(accent, 0.22), "editorBracketMatch.background");
    set(faded(accent, 0.5), "editorBracketMatch.border");
    set(washed(ink, 0.14), "editor.wordHighlightStrongBackground");
    set(washed(ink, 0.1), "editor.selectionHighlightBackground", "editor.wordHighlightBackground");
    set(washed(ink, 0.05), "editor.lineHighlightBackground", "list.hoverBackground");
    set(faded(ink, 0), "editor.lineHighlightBorder", "editorOverviewRuler.border");
    set(lines, "editorIndentGuide.background", "editorIndentGuide.background1", "editorWhitespace.foreground", "editorWidget.border", "editorHoverWidget.border", "editorSuggestWidget.border");
    set(faded(ink, 0.38), "editorIndentGuide.activeBackground", "editorIndentGuide.activeBackground1");
    set(faded(ink, 0.16), "scrollbarSlider.background");
    set(faded(ink, 0.24), "scrollbarSlider.hoverBackground");
    set(faded(ink, 0.32), "scrollbarSlider.activeBackground");
    set(read("error"), "editorError.foreground");
    set(read("warning"), "editorWarning.foreground");
    set(read("info"), "editorInfo.foreground");
    // A token's colour cannot be see-through, so each is the solid colour it comes to on the page.
    const solid = (colour) => hexOf(over(colour, surface)).slice(1);
    const dark = isDark(surface);
    const name = dark ? DARK_THEME : LIGHT_THEME;
    monaco.editor.defineTheme(name, {
        base: dark ? "vs-dark" : "vs",
        inherit: true,
        rules: [
            { token: "", foreground: solid(ink) },
            { token: "comment", foreground: solid(quiet) },
            { token: "string", foreground: solid(accent) },
            { token: "variable", foreground: solid(ink) },
        ],
        colors: colours,
    });
    monaco.editor.setTheme(name);
}
