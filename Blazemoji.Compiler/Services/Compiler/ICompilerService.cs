using Blazemoji.Contracts.Models;

namespace Blazemoji.Services.Compiler
{
    public interface ICompilerService
    {
        //Compiles emojicode text into a binary and returns a result
        public ExecuteCodeResult CompileEmojicode(string code);
    }
}
