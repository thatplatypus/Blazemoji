namespace Blazemoji.Test.Toolchain
{
    /// <summary>
    /// Small Emojicode programs with a known behaviour.
    /// </summary>
    internal static class Programs
    {
        public const string Hello = "🏁 🍇\n  😀 🔤Hello World!🔤❗️\n🍉\n";

        /// <summary>The compiler reports <c>nope</c> at line 2, character 5.</summary>
        public const string UndefinedVariable = "🏁 🍇\n  😀 nope❗️\n🍉\n";

        /// <summary>
        /// A list of closures makes the compiler print its "Run-time type information" warning
        /// while still producing a working binary, which is what every Grapevine build does.
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

        /// <summary>20,000 lines of 64 characters: 1.3 MB.</summary>
        public const string OneMegabyte =
            "🏁 🍇\n  🔂 i 🆕⏩ 0 20000❗️ 🍇\n" +
            "    😀 🔤0123456789012345678901234567890123456789012345678901234567890123🔤❗️\n" +
            "  🍉\n🍉\n";

        public const string Forever =
            "🏁 🍇\n  🔁 👍 🍇\n    ⏲🐇🧵 100000❗️\n  🍉\n🍉\n";

        /// <summary>Unwraps an environment variable that is not set.</summary>
        public const string Crashes =
            "🏁 🍇\n  😀 🔤before the crash🔤❗️\n  😀 🍺 🌳🐇💻 🔤BLAZEMOJI_SURELY_NOT_SET🔤❗️❗️\n🍉\n";

        public const string PrintsEnvironment =
            "🏁 🍇\n  ↪️ 🌳🐇💻 🔤BLAZEMOJI_TEST🔤❗️ ➡️ value 🍇\n    😀 value❗️\n  🍉\n🍉\n";

        /// <summary>
        /// Listens on the port named by <c>PORT</c>, prints each request it receives, and
        /// answers every one with <c>200 hello</c>. Written with the stock sockets package.
        /// </summary>
        public const string TinyHttpServer =
            "📦 sockets 🏠\n\n" +
            "🏁 🍇\n" +
            "  8080 ➡️ 🖍🆕port\n" +
            "  ↪️ 🌳🐇💻 🔤PORT🔤❗️ ➡️ text 🍇\n" +
            "    ↪️ 🔢 text 10❗️ ➡️ parsed 🍇\n" +
            "      parsed ➡️ 🖍port\n" +
            "    🍉\n" +
            "  🍉\n" +
            "  🍺 🆕🏄 port❗️ ➡️ server\n" +
            "  😀 🔤listening🔤❗️\n" +
            "  🔁 👍 🍇\n" +
            "    🍺 🙋 server❗️ ➡️ client\n" +
            "    🆗 data 👂 client 4096❗️ 🍇\n" +
            "      😀 🍺 🔡 data❗️❗️\n" +
            "      🆗 💬 client 📇 🔤HTTP/1.1 200 OK❌r❌nContent-Type: text/plain❌r❌nContent-Length: 5❌r❌nConnection: close❌r❌n❌r❌nhello🔤❗️❗️ 🍇🍉\n" +
            "      🙅 sendError 🍇🍉\n" +
            "    🍉\n" +
            "    🙅 readError 🍇🍉\n" +
            "    🚪 client❗️\n" +
            "  🍉\n" +
            "🍉\n";

        public static string PrintsMarker(string marker) => $"🏁 🍇\n  😀 🔤marker-{marker}🔤❗️\n🍉\n";

        public static string ExitsWith(int code) => $"🏁 🍇\n  😀 🔤before exit🔤❗️\n  🚪🐇💻 {code}❗️\n🍉\n";
    }
}
