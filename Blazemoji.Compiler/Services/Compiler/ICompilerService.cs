using Blazemoji.Contracts.Models;

namespace Blazemoji.Services.Compiler
{
    /// <summary>
    /// High-level interface for interacting with emojicode source files and binaries
    /// </summary>
    public interface ICompilerService
    {
        /// <summary>
        /// Compiles emojicode text into a binary and returns a result
        /// </summary>
        public EmojicodeResult CompileEmojicode(string code);

        /// <summary>
        /// Executes compiled emojicode from a binary
        /// </summary>
        public EmojicodeResult ExecuteCode(string filename);

        /// <summary>
        /// Proceesses and runs compiled emojicodee from a binary async
        /// </summary>
        public Task<EmojicodeResult> ExecuteCodeAsync(string filename);
    }

    public static partial class ICompilerServiceExtensions
    {
        /// <summary>
        /// Short hand extension method to compile and run a source emojicode file
        /// </summary>
        public static async Task<EmojicodeResult> CompileAndExecuteAsync(this ICompilerService compilerService, string code)
        {
            var compilerResult = await Task.Run(() => compilerService.CompileEmojicode(code));

            if (compilerResult.Error)
                return compilerResult;

            return await Task.Run(() => compilerService.ExecuteCode(compilerResult.Result));
        }
    }
}
