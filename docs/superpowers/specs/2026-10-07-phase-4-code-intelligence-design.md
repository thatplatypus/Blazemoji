# Phase 4: Code intelligence without a language server

- **Date:** 2026-10-07
- **Branch:** `phase-4-code-intelligence`, from `phase-3-projects`
- **Status:** written and self-approved during the overnight run Tom authorised on 2026-10-06; awaiting his review after the fact. The brief asks for the approach to the method-before-receiver problem to be proposed before building. Tom was asleep, so the proposal below was chosen and built; it is the first thing to look at.

## Context

Emojicode is hard to write from memory: every method, type and keyword is an emoji, and Grapevine adds about a hundred more. The editor so far only colours comments and strings. This phase makes it help: suggestions, descriptions, parameter lists, and compiler errors while typing. There is no language server; Monaco's own provider hooks are fed with data the app already has.

## Goal

Done when, in a project made from the Grapevine Todo template:

1. completion offers 🍷's routing methods (📥 📮 ✏️ 🗑 🧱) with their documentation;
2. signature help shows a method's parameters once its receiver is typed;
3. a type error shows up in the editor within about a second of being typed.

A browser test checks each.

## Facts checked before writing this

| Fact | Consequence |
| --- | --- |
| The compiler's documentation report (served per package since Phase 2) lists every type with its methods, type methods and initializers, their parameters with names and types, return types and doc comments. | It is the whole data source for types and methods. Nothing has to be parsed out of interface files. |
| BlazorMonaco 3.5 lets C# register completion and hover providers. It has nothing for signature help. | Signature help needs one small JavaScript module, written in TypeScript with the compiled file committed, wrapped in a typed C# class. |
| GEmojiSharp 4.1.0 (MIT) carries the GitHub emoji list: every emoji with aliases, tags and a description. | "Emoji by name" needs no data file of our own. |
| A call is written method first: `📥 app 🔤/x🔤 handler❗️`. An initializer is `🆕🍷❗️`, a type method `🏗🐇🍷❗️`. | When the method emoji is being typed, the receiver is not there yet. See the proposal. |
| Compiling for diagnostics needs no link step. Linking is about half the time of a small build. | A check-only compile is added to the contract. |

## The method-before-receiver problem: proposal

Three things that work together. The first is the one to learn; the other two catch the cases it does not.

**A. Receiver first, then a dot.** Type the receiver and a dot, `app.`, and the list shows the methods of `app`'s type with their documentation. Accepting one rewrites `app.` into `📥 app ` and opens the parameter list. A dot means nothing in Emojicode, so it is free to use as a trigger, and it is what every other language has taught people to type.

**B. Name search anywhere.** Typing a word, or a colon and a word (`:inbox`), searches three things at once: methods of the types that are in scope near the cursor (ranked first, each labelled with its type), keywords from the catalog, and every emoji by name. So `inbox` finds 📥 as "📥 of 🍷: registers a GET route" before it finds 📥 the plain emoji.

**C. Signature help once the receiver is there.** After `📥 app ` the parameters appear, `path 🔡, handler 🍇📨➡️📬🍉`, with the current one marked. This works however the method emoji got there.

What makes A and C possible is knowing the type of `app`. The editor works that out from the text, deliberately simply:

- a variable assigned from an initializer (`🆕🍷❗️ ➡️ app`) has that type;
- a variable assigned from a literal has the literal's type;
- a variable assigned from a call whose receiver's type is known has the call's documented return type (`🗂 app 🔤/admin🔤❗️ ➡️ admin`);
- a parameter declared with a type (`r 📨` in a method or closure header) has that type;
- a declared instance variable (`🖍🆕 store 🏪`) has that type.

When the type cannot be worked out, A offers nothing and says so, and B still works. No attempt is made to check calls: the compiler does that, and its answer arrives within a second (see "Diagnostics while typing").

Alternatives considered and not chosen: suggesting every method of every type on every emoji typed (hundreds of entries, most wrong); a full type checker in the browser (a second compiler to maintain); a structured "call builder" dialog (takes the hands off the keyboard).

## Design

### The contract: one addition

`POST /compile` accepts `"check": true`. The service compiles and reports diagnostics as usual, does not link, and keeps no build; the answer has no `buildId`. `CompileRequest` gains `CheckOnly`.

### Data, in plain C#

All of this lives in `Blazemoji/Emojicode/Intelligence/`, has no Monaco or Blazor types in it, and is tested with plain xUnit.

| Piece | What it does |
| --- | --- |
| `PackageDocumentation` and its reader | The compiler's report as a model: package, types, methods, parameters, return types, documentation. |
| `IPackageLibrary` | Gives the documentation for a package by name, fetched once from the toolchain service and kept. |
| `EmojiNames` | Emoji by name, alias or tag, from GEmojiSharp. |
| `KeywordCatalog` | The existing `EmojicodeKeyword` classes: emoji, description, the equivalent keyword in other languages, example. |
| `SourceReader` | Splits Emojicode text into tokens well enough to find imports (`📦 name 🏠`), variable assignments and declarations, and the call the cursor is in. Comments and strings are skipped. |
| `ICodeIntelligence` | `CompleteAsync`, `HoverAsync`, `SignatureAsync`, each taking the project's files, the open file's path and a position, each returning plain records. |

### Providers

The editor registers three Monaco providers for the language, once. Each asks `ICodeIntelligence` and maps the answer to Monaco's shapes:

- **Completion:** triggered by letters, `:` and `.`. An entry inserts an emoji (or a snippet for keywords with an obvious shape), replaces what was typed to find it, shows what it belongs to as its detail, and carries documentation in Markdown, including the example for keywords.
- **Hover:** over a keyword, its description and example; over a type, its documentation; over a method in a call whose receiver's type is known, its signature and documentation; over any other emoji, the types that have a method by that name, and the emoji's name.
- **Signature help:** triggered by a space. Finds the call the cursor is in, and if the receiver's type and the method are known, shows the parameters and marks the one being typed.

### Diagnostics while typing

Half a second after typing pauses, the page compiles the project with `check: true` and replaces the problems and markers. It does not start while a run is in progress or being started, a newer edit discards an older answer, and Run still compiles for itself. `RunState` owns this as `CheckAsync`, since it already owns diagnostics.

## Testing

- **Plain C#:** the documentation reader against the committed `s` report (and Grapevine's when built); the source reader on real Grapevine code; completion, hover and signature results for the cases in the proposal, with exact expectations; type inference case by case; emoji search.
- **Toolchain and service:** check-only compiles report diagnostics, produce no build and no program file; contract test for `check: true`.
- **State:** `CheckAsync` publishes diagnostics, drops stale answers, stands aside for a run.
- **Browser:** the three "done when" checks, plus emoji by name.

## Out of scope

- Completion of the project's own classes and methods (only packages have documentation reports).
- Go to definition, rename, formatting, code actions.
- Generic type arguments in inference: `🍨🐚🔡🍆` is treated as `🍨`.
- Checking calls in the browser.

## Risks

| Risk | Handling |
| --- | --- |
| Each completion is a round trip to the server (Blazor Server). | Fine on one machine. The logic is plain C#, so a WebAssembly or desktop host can run it in the browser process later. |
| The simple type inference guesses wrong. | It only ever narrows suggestions; name search and the compiler are still there. Wrong guesses are testable case by case. |
| Check-only compiles pile up on a slow machine. | One at a time per page, newest wins, and the service already caps concurrent compiles. |

## Decisions taken on Tom's behalf

1. The proposal above (A, B and C together), without waiting for a review of it.
2. A dot as the receiver-first trigger.
3. GEmojiSharp for emoji names.
4. TypeScript source with the compiled JavaScript committed, so that building the app still needs no Node.
5. A `check` flag on `/compile` in place of a separate endpoint.
