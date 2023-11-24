using Blazemoji.Contracts.Models;

namespace Blazemoji.Services.Compiler
{

    /// <summary>
    /// Represents an abstraction around running emojicode from the UI
    /// Emojicode actions depend on specific OS architecture to run
    /// This interface guarentees we can get a valid emojicode result from source emojicode
    /// </summary>
    public interface ICodeRunner
    {
        /// <summary>
        /// Executes an emojicode program in string form with emojis
        /// </summary>
        /// <param name="code">Raw emojicode program</param>
        public Task<EmojicodeResult> ExecuteAsync(string code);
    }
}
