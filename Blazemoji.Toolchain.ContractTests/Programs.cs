namespace Blazemoji.Toolchain.ContractTests
{
    /// <summary>
    /// Small Emojicode programs with a known behaviour. This project keeps its own copy so
    /// that it can be lifted out and pointed at any implementation of the contract.
    /// </summary>
    internal static class Programs
    {
        public const string Hello = "🏁 🍇\n  😀 🔤Hello World!🔤❗️\n🍉\n";

        /// <summary>The compiler reports <c>nope</c> at line 2, character 5.</summary>
        public const string UndefinedVariable = "🏁 🍇\n  😀 nope❗️\n🍉\n";

        /// <summary>
        /// A list of closures makes the compiler print its "Run-time type information" warning
        /// while still producing a working binary.
        /// </summary>
        public const string RttiWarning =
            "🏁 🍇\n" +
            "  🆕🍨🐚🍇🔢➡️🔢🍉🍆❗️ ➡️ 🖍🆕 fs\n" +
            "  🐻 fs 🍇🎍🥡 a 🔢 ➡️ 🔢\n" +
            "    ↩️ a ➕ 1\n" +
            "  🍉❗️\n" +
            "  😀 🔤stored🔤❗️\n" +
            "🍉\n";

        public const string SlowTwoLines =
            "🏁 🍇\n  😀 🔤one🔤❗️\n  ⏲🐇🧵 1500000❗️\n  😀 🔤two🔤❗️\n🍉\n";

        public const string Forever =
            "🏁 🍇\n  🔁 👍 🍇\n    ⏲🐇🧵 100000❗️\n  🍉\n🍉\n";

        public const string PrintsEnvironment =
            "🏁 🍇\n  ↪️ 🌳🐇💻 🔤BLAZEMOJI_TEST🔤❗️ ➡️ value 🍇\n    😀 value❗️\n  🍉\n🍉\n";

        public static string ExitsWith(int code) => $"🏁 🍇\n  😀 🔤before exit🔤❗️\n  🚪🐇💻 {code}❗️\n🍉\n";
    }
}
