using Blazemoji.Contracts.Models;

namespace Blazemoji.Services.Compiler
{
    public interface ICodeRunner
    {
        public Task<EmojicodeResult> ExecuteAsync(string code);
    }
}
