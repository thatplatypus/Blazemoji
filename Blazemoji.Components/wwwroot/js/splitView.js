// Settles the divider of a SplitView where it was let go, and tells .NET.
//
// The MudSplitPanel inside a SplitView moves its own divider: it sizes both parts in pixels
// while a drag or an arrow key goes on, and does nothing more when it ends. At rest a
// SplitView's divider is a share of the room instead, placed by the style sheet
// (SplitView.razor.css), so that it holds when the window changes size. This is what turns
// the one into the other, in the browser and at once: when the divider is let go it works out
// the share, hands it to the style sheet, and has the panel let go of its pixels. Only then
// is .NET told, so that the share can be kept. Nothing waits on the server, so a second drag
// or a run of arrow presses is never undone by an answer to the first.
//
// Written in Scripts/splitView.ts and compiled by scripts/build-js.sh to wwwroot/js/splitView.js.
// The compiled file is committed so that building the app does not need Node. Edit the .ts file.
const DIVIDER_MOVED = "DividerMovedAsync";
const DIVIDER_RESET = "DividerResetAsync";
// The classes SplitView.razor gives the panel's own elements.
const DIVIDER = ":scope > * > .split-view-divider";
const FIRST = ":scope > * > .split-view-first";
// Where the style sheet looks for the share before it looks at what .NET rendered. It is set
// on the panel's own element, which .NET never writes a style to, so nothing .NET renders
// later can move a divider that has just been let go.
const LIVE_SHARE = "--split-live";
const MOVES_THE_DIVIDER = new Set(["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"]);
/** Less than this many pixels is a press on the divider, not a move of it. */
const A_MOVE = 0.5;
/** Two taps of a finger within this many milliseconds are a double tap. */
const DOUBLE_TAP = 300;
const watched = new WeakMap();
/**
 * Has the panel drop the pixel sizes its last drag left on both parts. This is MudBlazor's
 * own reset, the one its ResetDividerPositionAsync calls. In a version of MudBlazor without
 * it the parts keep those sizes, which is where the divider was let go: nothing moves, and
 * the divider only stops following the window.
 */
function releasePixels(panels) {
    const reset = window.mudSplitPanel_resetDividerPosition;
    try {
        reset?.(panels.id);
    }
    catch {
        // The panel has not been built yet, or has gone: there are no pixel sizes to drop.
    }
}
/**
 * Starts looking after the divider directly inside `root`. A split view inside one of the
 * parts has a root and a divider of its own.
 *
 * @param defaultShare Where a double-click or a double tap puts the divider.
 * @param label What the divider is called for someone who cannot see it, in place of the panel's own words.
 */
export function attach(root, view, defaultShare, label) {
    detach(root);
    const divider = root.querySelector(DIVIDER);
    const first = root.querySelector(FIRST);
    const panels = divider?.parentElement;
    if (!divider || !first || !panels) {
        return;
    }
    if (label) {
        divider.setAttribute("aria-label", label);
    }
    const stacked = () => divider.getAttribute("aria-orientation") === "horizontal";
    const along = (box) => (stacked() ? box.height : box.width);
    const taken = () => along(first.getBoundingClientRect());
    // The first part's share as the style sheet counts it: of the room and one divider more,
    // what the first part and the divider take. Counted this way, the same number puts the
    // divider back on the same pixel.
    const share = () => {
        const gap = along(divider.getBoundingClientRect());
        return (taken() + gap) / (along(panels.getBoundingClientRect()) + gap);
    };
    const tell = (method, ...args) => {
        // The page may be on its way out, and then there is nobody left to tell.
        view.invokeMethodAsync(method, ...args).catch(() => undefined);
    };
    // What someone who cannot see the divider is read: the first part's size in hundredths
    // of the room. The panel writes it while it drags, and writes 50 whenever it lets go of
    // its pixel sizes, wherever the divider then rests. So it is put right each time the
    // panel has written it.
    const describe = () => {
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
    let lastTap = 0;
    let lastLetGoMoved = false;
    /** Puts the divider at a share, here and now, as a share and no longer as pixels. */
    const settle = (to) => {
        panels.style.setProperty(LIVE_SHARE, String(to));
        releasePixels(panels);
        describe();
        before = taken();
    };
    /** @returns True when the divider is somewhere new, and has been settled there. */
    const settleIfMoved = () => {
        if (Math.abs(taken() - before) < A_MOVE) {
            return false;
        }
        const to = share();
        if (!Number.isFinite(to)) {
            return false;
        }
        settle(to);
        tell(DIVIDER_MOVED, to);
        return true;
    };
    const reset = () => {
        settle(defaultShare);
        tell(DIVIDER_RESET);
    };
    const stopWaiting = () => {
        document.removeEventListener("mouseup", letGo);
        document.removeEventListener("touchend", letGo);
    };
    const letGo = (event) => {
        stopWaiting();
        const now = performance.now();
        lastLetGoMoved = settleIfMoved();
        if (lastLetGoMoved) {
            lastTap = 0;
        }
        else if (event.type === "touchend") {
            // The panel keeps a touch from becoming a double-click, so a double tap is counted here.
            if (now - lastTap < DOUBLE_TAP) {
                lastTap = 0;
                reset();
            }
            else {
                lastTap = now;
            }
        }
    };
    const pressed = () => {
        before = taken();
        document.addEventListener("mouseup", letGo);
        document.addEventListener("touchend", letGo);
    };
    const keyDown = (event) => {
        if (MOVES_THE_DIVIDER.has(event.key) && !event.repeat) {
            before = taken();
        }
    };
    const keyUp = (event) => {
        if (MOVES_THE_DIVIDER.has(event.key)) {
            settleIfMoved();
        }
    };
    // A key still held when the focus moves on never comes up here.
    const focusLeft = () => {
        settleIfMoved();
    };
    const doubleClicked = () => {
        // A press straight after a click, held and dragged, ends as a double-click too. The
        // letting go that came just before this says which it was.
        if (!lastLetGoMoved) {
            reset();
        }
    };
    divider.addEventListener("mousedown", pressed);
    divider.addEventListener("touchstart", pressed, { passive: true });
    divider.addEventListener("keydown", keyDown, true);
    divider.addEventListener("keyup", keyUp);
    divider.addEventListener("blur", focusLeft);
    divider.addEventListener("dblclick", doubleClicked);
    divider.addEventListener("focus", describe);
    watched.set(root, () => {
        stopWaiting();
        whenThePanelDescribesIt.disconnect();
        divider.removeEventListener("mousedown", pressed);
        divider.removeEventListener("touchstart", pressed);
        divider.removeEventListener("keydown", keyDown, true);
        divider.removeEventListener("keyup", keyUp);
        divider.removeEventListener("blur", focusLeft);
        divider.removeEventListener("dblclick", doubleClicked);
        divider.removeEventListener("focus", describe);
    });
}
/**
 * Hands the divider inside `root` back to the share .NET rendered. For when the share has
 * changed for a reason other than this divider being moved.
 */
export function release(root) {
    const panels = root.querySelector(DIVIDER)?.parentElement;
    if (!panels) {
        return;
    }
    panels.style.removeProperty(LIVE_SHARE);
    releasePixels(panels);
}
/** Stops looking after the divider inside `root`. Asking for one that is not looked after does nothing. */
export function detach(root) {
    watched.get(root)?.();
    watched.delete(root);
}
