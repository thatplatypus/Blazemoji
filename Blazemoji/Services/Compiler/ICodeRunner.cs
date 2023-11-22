using Blazemoji.Contracts.Models;

namespace Blazemoji.Services.Compiler
{
    public interface ICodeRunner
    {
        public Task<ExecuteCodeResult> ExecuteAsync(string code);
    }
}
