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

export function register(languageId: string, dotNet: DotNetReference): Disposable {
    const registrations: Disposable[] = [
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
        },
    };
}
