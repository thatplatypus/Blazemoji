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
        { (int)KeyMod.Shift | (int)KeyCode.Quote, "🔤" },
        { (int)KeyMod.Shift | (int)KeyCode.Digit5, "🚮" },
        { (int)KeyMod.Shift | (int)KeyCode.Digit6, "🔺" },
        { (int)KeyMod.Shift | (int)KeyCode.Digit8, "✖️" },
        { (int)KeyMod.Shift | (int)KeyCode.Digit9, "🤜" },
        { (int)KeyMod.Shift | (int)KeyCode.Digit0, "🤛" },
        { (int)KeyMod.Shift | (int)KeyCode.Equal, "➕" },
        { (int)KeyMod.CtrlCmd | (int)KeyCode.Equal, "+" },
        { (int)KeyMod.CtrlCmd | (int)KeyCode.Minus, "➖" },
        { (int)KeyMod.CtrlCmd | (int)KeyMod.Alt | (int)KeyCode.Slash, "➗" },
        { (int)KeyMod.Shift | (int)KeyCode.Period, "▶️" },
        { (int)KeyMod.Shift | (int)KeyCode.Comma, "◀️" },
    };

        public static Dictionary<int, string> Keybindings { get => _keybindings; }

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