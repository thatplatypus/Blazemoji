using Blazemoji.Emojicode.Intelligence;
using BlazorMonaco;

namespace Blazemoji.Emojicode
{
    public static class EmojicodeKeybindings
    {
        private static Dictionary<int, string> _keybindings = new()
    {
        { (int)KeyMod.Shift | (int)KeyCode.BracketLeft, "🍇" },
        { (int)KeyMod.Shift | (int)KeyCode.BracketRight, "🍉" },
        { (int)KeyMod.CtrlCmd | (int)KeyCode.Slash, "💭" },
        { (int)KeyMod.CtrlCmd | (int)KeyMod.Shift | (int)KeyCode.Slash, "💭🔜\r\n🔚💭" },
        { (int)KeyMod.CtrlCmd | (int)KeyCode.KeyP, "😀" },
        { (int)KeyMod.Shift | (int)KeyCode.Digit1, "❗" },
        { (int)KeyMod.CtrlCmd | (int)KeyCode.Digit1, "!" },
        { (int)KeyMod.Shift | (int)KeyCode.Digit2, "🧲" },
        { (int)KeyMod.Shift | (int)KeyCode.Quote, "🔤" },
        { (int)KeyMod.Shift | (int)KeyCode.Digit5, "🚮" },
        { (int)KeyMod.Shift | (int)KeyCode.Digit6, "🔺" },
        { (int)KeyMod.Shift | (int)KeyCode.Digit8, "✖️" },
        { (int)KeyMod.Shift | (int)KeyCode.Digit9, "🤜" },
        { (int)KeyMod.Shift | (int)KeyCode.Digit0, "🤛" },
        { (int)KeyCode.Equal, "➡️" },
        { (int)KeyMod.Shift | (int)KeyCode.Equal, "➕" },
        { (int)KeyMod.CtrlCmd | (int)KeyCode.Equal, "+" },
        { (int)KeyMod.CtrlCmd | (int)KeyCode.Minus, "➖" },
        { (int)KeyMod.CtrlCmd | (int)KeyMod.Alt | (int)KeyCode.Slash, "➗" },
        { (int)KeyMod.Shift | (int)KeyCode.Period, "▶️" },
        { (int)KeyMod.Shift | (int)KeyCode.Comma, "◀️" },
    };

        public static Dictionary<int, string> Keybindings { get => _keybindings; }

        // What is printed on each key that is typed alone or with Shift alone, on a US keyboard,
        // which is the layout the table above is written for. Inside a string or a comment that
        // is what the key should type: "Hello World!" needs its own exclamation mark, not the ❗
        // that ends a call.
        private static readonly Dictionary<int, string> _printedOnTheKey = new()
        {
            { (int)KeyMod.Shift | (int)KeyCode.BracketLeft, "{" },
            { (int)KeyMod.Shift | (int)KeyCode.BracketRight, "}" },
            { (int)KeyMod.Shift | (int)KeyCode.Digit1, "!" },
            { (int)KeyMod.Shift | (int)KeyCode.Digit2, "@" },
            { (int)KeyMod.Shift | (int)KeyCode.Quote, "\"" },
            { (int)KeyMod.Shift | (int)KeyCode.Digit5, "%" },
            { (int)KeyMod.Shift | (int)KeyCode.Digit6, "^" },
            { (int)KeyMod.Shift | (int)KeyCode.Digit8, "*" },
            { (int)KeyMod.Shift | (int)KeyCode.Digit9, "(" },
            { (int)KeyMod.Shift | (int)KeyCode.Digit0, ")" },
            { (int)KeyCode.Equal, "=" },
            { (int)KeyMod.Shift | (int)KeyCode.Equal, "+" },
            { (int)KeyMod.Shift | (int)KeyCode.Period, ">" },
            { (int)KeyMod.Shift | (int)KeyCode.Comma, "<" },
        };

        private const int ClosesAString = (int)KeyMod.Shift | (int)KeyCode.Quote;
        private const int PutsAValueInAString = (int)KeyMod.Shift | (int)KeyCode.Digit2;

        /// <summary>True for a key whose text depends on where the cursor is. See <see cref="TextFor"/>.</summary>
        public static bool CanDependOnContext(int keybinding) => _printedOnTheKey.ContainsKey(keybinding);

        /// <summary>
        /// What a key types where the cursor is: its emoji in code, and what is printed on it
        /// inside a string or a comment. Between two 🧲 in a string, where a value is written,
        /// it is code again. Two keys differ. The quote key still types 🔤 inside a string,
        /// since that is how the string is closed. And the at key is the other way about: its
        /// 🧲 means something only inside a string, so that is the one place it types it.
        /// </summary>
        public static string TextFor(int keybinding, TextContext context)
        {
            var isCode = context is TextContext.Code or TextContext.Interpolation;
            var typesItsEmoji = keybinding switch
            {
                PutsAValueInAString => context is TextContext.String or TextContext.Interpolation,
                ClosesAString => isCode || context == TextContext.String,
                _ => isCode,
            };

            return !typesItsEmoji && _printedOnTheKey.TryGetValue(keybinding, out var printed) ? printed : _keybindings[keybinding];
        }

        public static (KeyMod[], KeyCode) GetKeybindingComponents(int key)
        {
            List<KeyMod> keyMods = [];
            int remainingKey = key;

            foreach (KeyMod mod in Enum.GetValues(typeof(KeyMod)))
            {
                if ((remainingKey & (int)mod) != 0)
                {
                    keyMods.Add(mod);
                    remainingKey -= (int)mod;
                }
            }

            KeyCode keyCode = (KeyCode)remainingKey;

            return (keyMods.OrderByDescending(x => x).ToArray(), keyCode);
        }
    }
}