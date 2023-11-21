using Blazemoji.Contracts.Models;

namespace Blazemoji.Services.Execution
{
    public interface ICodeExecutionService
    {
        public ExecuteCodeResult ProcessCode(string filename);

        public Task<ExecuteCodeResult> ProcessCodeAsync(string filename);

    }
}
