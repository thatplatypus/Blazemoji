using Blazemoji.Models.Compiler;

namespace Blazemoji.Services.Compiler
{
    public interface ICompilerService
    {
        //Compiles emojicode text into a binary and returns a result
        public EmojicodecResult CompileEmojicode(string code);
    }
}
