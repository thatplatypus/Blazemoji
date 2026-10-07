// Registers Monaco's completion, hover and signature help providers for Emojicode.
// The providers hold no knowledge of the language: each one hands the text and the cursor's
// offset to .NET and turns the answer into the shape Monaco wants.
//
// Written in Scripts/emojicodeLanguage.ts and compiled by scripts/build-js.sh to
// wwwroot/js/emojicodeLanguage.js. The compiled file is committed so that building the app
// does not need Node. Edit the .ts file.

declare const monaco: any;

interface DotNetReference {
    invokeMethodAsync<T>(method: string, ...args: unknown[]): Promise<T>;
}

interface CompletionAnswer {
    label: string;
    kind: string;
    insert: string;
    replaceStart: number;
    replaceEnd: number;
    detail: string;
    documentation: string;
    order: number;
}

interface HoverAnswer {
    start: number;
    end: number;
    markdown: string;
}

interface SignatureAnswer {
    label: string;
    documentation: string;
    parameters: string[];
    activeParameter: number;
}

interface Disposable {
    dispose(): void;
}

// The names of the .NET methods this module calls. They match EmojicodeLanguageInterop.
const COMPLETE = "CompleteAsync";
const HOVER = "HoverAsync";
const SIGNATURE = "SignatureAsync";

const SHOW_PARAMETERS = "editor.action.triggerParameterHints";

function rangeOf(model: any, start: number, end: number): any {
    const from = model.getPositionAt(start);
    const to = model.getPositionAt(end);
    return new monaco.Range(from.lineNumber, from.column, to.lineNumber, to.column);
}

function kindOf(kind: string): number {
    const kinds = monaco.languages.CompletionItemKind;
    switch (kind) {
        case "Method": return kinds.Method;
        case "Variable": return kinds.Variable;
        case "Keyword": return kinds.Keyword;
        default: return kinds.Text;
    }
}

// Everything from the start of the file to the cursor of the editor in the element with this
// id. What a key should type depends on whether that text ends inside a string or a comment,
// and .NET is where that is worked out.
export function textBeforeCursor(editorId: string): string {
    const editor = monaco.editor.getEditors().find((candidate: any) => candidate.getContainerDomNode()?.id === editorId);
    const model = editor?.getModel();
    const position = editor?.getPosition();
    if (!model || !position) {
        return "";
    }

    return model.getValueInRange(new monaco.Range(1, 1, position.lineNumber, position.column));
}

// The browser's own knowledge of where one written character ends and the next begins.
const graphemes: any = typeof Intl !== "undefined" && "Segmenter" in Intl
    ? new (Intl as any).Segmenter(undefined, { granularity: "grapheme" })
    : null;

// What marks a character made of several code points as an emoji: a pictograph or a flag
// letter, the selector that asks for the emoji form, a keycap, or the joiner between two emoji.
const EMOJI_PART = /\p{Extended_Pictographic}|\p{Regional_Indicator}|\uFE0F|\u20E3|\u200D/u;

// No emoji is longer than this many UTF-16 units, so this much text before the cursor is
// enough to find where the last one starts.
const LONGEST_EMOJI = 64;

// The range of the emoji that ends at the cursor, when it is one that Backspace would only
// take a part of: an emoji of more than one code point. Null for anything else.
function emojiEndingAtCursor(editor: any): any | null {
    const selections = editor.getSelections();
    if (!graphemes || !selections || selections.length !== 1 || !selections[0].isEmpty()) {
        return null;
    }

    const position = selections[0].getPosition();
    const before: string = editor.getModel().getLineContent(position.lineNumber).slice(0, position.column - 1);
    const tail = before.slice(-LONGEST_EMOJI);
    let last: { segment: string; index: number } | null = null;
    for (const piece of graphemes.segment(tail)) {
        last = piece;
    }

    if (!last || Array.from(last.segment).length < 2 || !EMOJI_PART.test(last.segment)) {
        return null;
    }

    const start = before.length - tail.length + last.index;
    return new monaco.Range(position.lineNumber, start + 1, position.lineNumber, position.column);
}

// Monaco's Backspace takes a whole emoji only when the emoji's first character is in a table
// Monaco carries. Much of Emojicode is written with emoji that are not: ↩️ ↪️ ◀️ ▶️ ⬅️ ⁉️ are
// each a plain symbol followed by a selector, and Backspace took the selector and left the
// symbol; 🤷‍♀️ lost its second half. Here Backspace takes such an emoji whole. Every other
// case (a plain character, a selection, several cursors, an accent typed after its letter)
// is left to Monaco, by not touching the key at all.
function keepEmojiWhole(editor: any, languageId: string): Disposable {
    return editor.onKeyDown((pressed: any) => {
        if (pressed.keyCode !== monaco.KeyCode.Backspace || pressed.ctrlKey || pressed.altKey || pressed.metaKey || pressed.browserEvent?.isComposing) {
            return;
        }

        if (editor.getModel()?.getLanguageId() !== languageId || editor.getOption(monaco.editor.EditorOption.readOnly)) {
            return;
        }

        const emoji = emojiEndingAtCursor(editor);
        if (!emoji) {
            return;
        }

        pressed.preventDefault();
        pressed.stopPropagation();
        editor.pushUndoStop();
        editor.executeEdits("blazemoji.backspace", [{ range: emoji, text: "", forceMoveMarkers: true }]);
        editor.pushUndoStop();
    });
}

export function register(languageId: string, dotNet: DotNetReference): Disposable {
    // Editors that are already there, and any made later.
    const editorHooks: Disposable[] = monaco.editor.getEditors().map((editor: any) => keepEmojiWhole(editor, languageId));

    const registrations: Disposable[] = [
        monaco.editor.onDidCreateEditor((editor: any) => editorHooks.push(keepEmojiWhole(editor, languageId))),
        monaco.languages.registerCompletionItemProvider(languageId, {
            triggerCharacters: [".", ":"],
            async provideCompletionItems(model: any, position: any) {
                const answers = await dotNet.invokeMethodAsync<CompletionAnswer[]>(COMPLETE, model.getValue(), model.getOffsetAt(position));
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
            async provideHover(model: any, position: any) {
                const answer = await dotNet.invokeMethodAsync<HoverAnswer | null>(HOVER, model.getValue(), model.getOffsetAt(position));
                return answer
                    ? { range: rangeOf(model, answer.start, answer.end), contents: [{ value: answer.markdown }] }
                    : null;
            },
        }),
        monaco.languages.registerSignatureHelpProvider(languageId, {
            signatureHelpTriggerCharacters: [" "],
            signatureHelpRetriggerCharacters: [" "],
            async provideSignatureHelp(model: any, position: any) {
                const answer = await dotNet.invokeMethodAsync<SignatureAnswer | null>(SIGNATURE, model.getValue(), model.getOffsetAt(position));
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
