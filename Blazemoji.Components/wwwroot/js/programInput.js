// Sends the line typed for a running program when Enter is pressed, and empties the box.
//
// Both happen here, in the browser, as the key goes down. If the server emptied the box, it
// would do so a round trip later, and take with it whatever had been typed in the meantime:
// "yes", Enter, "no", Enter on a slow line reached the program as "yes" and "yesno". So the
// server is never told what is in the box, only handed each line as it is sent.
//
// Written in Scripts/programInput.ts and compiled by scripts/build-js.sh to
// wwwroot/js/programInput.js. The compiled file is committed so that building the app does
// not need Node. Edit the .ts file.
const SEND_LINE = "SendLineAsync";
const watched = new WeakMap();
/**
 * Starts sending lines from the text box inside `line`. The box itself may be drawn again
 * by the server; what is listened to is the element around it.
 */
export function attach(line, view) {
    detach(line);
    const keyDown = (event) => {
        // Enter that ends a word being composed, as with an input method, is not the end of the line.
        if (event.key !== "Enter" || event.isComposing) {
            return;
        }
        const box = event.target;
        if (!(box instanceof HTMLInputElement) || box.disabled) {
            return;
        }
        event.preventDefault();
        const text = box.value;
        box.value = "";
        // The page may be on its way out, and then there is nobody left to send to.
        view.invokeMethodAsync(SEND_LINE, text).catch(() => undefined);
    };
    line.addEventListener("keydown", keyDown);
    watched.set(line, () => line.removeEventListener("keydown", keyDown));
}
/** Stops sending lines from the box inside `line`. Asking for one that is not listened to does nothing. */
export function detach(line) {
    watched.get(line)?.();
    watched.delete(line);
}
