// Copies text to the system clipboard for the Copy buttons.
//
// A browser lets a page write to the clipboard only while a press is being handled. In a
// Blazor Server app a press goes to the server and the answer comes back later, and Safari
// no longer counts that as part of the press: it refuses the write. So the copying is done
// here, as the press arrives, from text the button already carries in its data-copy
// attribute, and .NET only asks afterwards how it went. Loading this script is what starts
// it, for every Copy button on the page.
//
// Written in Scripts/clipboard.ts and compiled by scripts/build-js.sh to wwwroot/js/clipboard.js.
// The compiled file is committed so that building the app does not need Node. Edit the .ts file.
const CARRIES_TEXT = "[data-copy]";
const TEXT = "data-copy";
let lastCopy = Promise.resolve(false);
function copyFrom(pressed) {
    const button = pressed.target?.closest?.(CARRIES_TEXT);
    if (!button) {
        return;
    }
    const text = button.getAttribute(TEXT);
    try {
        // Refused when the page is not served securely, among other things.
        lastCopy = text ? navigator.clipboard.writeText(text).then(() => true, () => false) : Promise.resolve(false);
    }
    catch {
        lastCopy = Promise.resolve(false);
    }
}
// Before anything on the page can stop the press from getting here.
document.addEventListener("click", copyFrom, true);
// Whether the copy started by the last press on a Copy button went through.
export function lastCopySucceeded() {
    return lastCopy;
}
