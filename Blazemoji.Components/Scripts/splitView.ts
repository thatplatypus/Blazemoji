// Watches the divider of a SplitView and tells .NET when it has been let go.
//
// The MudSplitPanel inside a SplitView moves its own divider: it sizes both parts in pixels
// while a drag or an arrow key goes on, and reports nothing when it ends. The server only
// needs to hear about the end, so this listens for it and sends where the divider is as a
// share of the room. A double-click asks for the default instead. Nothing here moves
// anything: placing the divider at rest is the style sheet's work (SplitView.razor.css).
//
// Written in Scripts/splitView.ts and compiled by scripts/build-js.sh to wwwroot/js/splitView.js.
// The compiled file is committed so that building the app does not need Node. Edit the .ts file.

const DIVIDER_MOVED = "DividerMovedAsync";
const DIVIDER_RESET = "DividerResetAsync";

// The classes SplitView.razor gives the panel's own elements.
const DIVIDER = ":scope > * > .split-view-divider";
const FIRST = ":scope > * > .split-view-first";

const MOVES_THE_DIVIDER = new Set(["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"]);

/** Less than this many pixels is a press on the divider, not a move of it. */
const A_MOVE = 0.5;

interface DotNetView {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

const watched = new WeakMap<HTMLElement, () => void>();

/**
 * Starts telling `view` about the divider directly inside `root`. A split view inside one of
 * the parts has a root and a divider of its own.
 */
export function attach(root: HTMLElement, view: DotNetView): void {
    detach(root);

    const divider = root.querySelector<HTMLElement>(DIVIDER);
    const first = root.querySelector<HTMLElement>(FIRST);
    const panels = divider?.parentElement;
    if (!divider || !first || !panels) {
        return;
    }

    const stacked = (): boolean => divider.getAttribute("aria-orientation") === "horizontal";
    const along = (box: DOMRect): number => (stacked() ? box.height : box.width);
    const taken = (): number => along(first.getBoundingClientRect());

    // The first part's share as the style sheet counts it: of the room and one divider more,
    // what the first part and the divider take. Counted this way, the same number puts the
    // divider back on the same pixel.
    const share = (): number => {
        const gap = along(divider.getBoundingClientRect());
        return (taken() + gap) / (along(panels.getBoundingClientRect()) + gap);
    };

    const tell = (method: string, ...args: unknown[]): void => {
        // The page may be on its way out, and then there is nobody left to tell.
        view.invokeMethodAsync(method, ...args).catch(() => undefined);
    };

    // What someone who cannot see the divider is read: the first part's size in hundredths
    // of the room. The panel writes it while it drags, and writes 50 whenever it lets go of
    // its pixel sizes, wherever the divider then rests. So it is put right each time the
    // panel has written it.
    const describe = (): void => {
        const room = along(panels.getBoundingClientRect());
        const now = room > 0 ? ((taken() / room) * 100).toFixed(2) : null;
        if (now !== null && divider.getAttribute("aria-valuenow") !== now) {
            divider.setAttribute("aria-valuenow", now);
        }
    };
    const whenThePanelDescribesIt = new MutationObserver(describe);
    whenThePanelDescribesIt.observe(divider, { attributes: true, attributeFilter: ["aria-valuenow"] });
    describe();

    let before = 0;
    const stopWaiting = (): void => {
        document.removeEventListener("mouseup", letGo);
        document.removeEventListener("touchend", letGo);
    };
    const letGo = (): void => {
        stopWaiting();
        if (Math.abs(taken() - before) >= A_MOVE) {
            tell(DIVIDER_MOVED, share());
        }
    };
    const pressed = (): void => {
        before = taken();
        document.addEventListener("mouseup", letGo);
        document.addEventListener("touchend", letGo);
    };
    const keyDown = (event: KeyboardEvent): void => {
        if (MOVES_THE_DIVIDER.has(event.key) && !event.repeat) {
            before = taken();
        }
    };
    const keyUp = (event: KeyboardEvent): void => {
        if (MOVES_THE_DIVIDER.has(event.key) && Math.abs(taken() - before) >= A_MOVE) {
            tell(DIVIDER_MOVED, share());
        }
    };
    const doubleClicked = (): void => tell(DIVIDER_RESET);

    divider.addEventListener("mousedown", pressed);
    divider.addEventListener("touchstart", pressed, { passive: true });
    divider.addEventListener("keydown", keyDown, true);
    divider.addEventListener("keyup", keyUp);
    divider.addEventListener("dblclick", doubleClicked);
    divider.addEventListener("focus", describe);

    watched.set(root, () => {
        stopWaiting();
        whenThePanelDescribesIt.disconnect();
        divider.removeEventListener("mousedown", pressed);
        divider.removeEventListener("touchstart", pressed);
        divider.removeEventListener("keydown", keyDown, true);
        divider.removeEventListener("keyup", keyUp);
        divider.removeEventListener("dblclick", doubleClicked);
        divider.removeEventListener("focus", describe);
    });
}

/** Stops watching the divider inside `root`. Asking for one that is not watched does nothing. */
export function detach(root: HTMLElement): void {
    watched.get(root)?.();
    watched.delete(root);
}
