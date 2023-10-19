using Blazemoji.Shared.Models.Execution;

namespace Blazemoji.Services.Execution
{
    public interface ICodeExecutionService
    {
        public ProcessedCodeResult ProcessCode(string filename);

        public Task<ProcessedCodeResult> ProcessCodeAsync(string filename);

    }
}
