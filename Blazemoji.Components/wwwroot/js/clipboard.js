// Copies text to the system clipboard for the Copy buttons.
//
// Written in Scripts/clipboard.ts and compiled by scripts/build-js.sh to wwwroot/js/clipboard.js.
// The compiled file is committed so that building the app does not need Node. Edit the .ts file.
export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    }
    catch {
        // The browser refused: the page is not focused, or it is not served securely.
        return false;
    }
}
