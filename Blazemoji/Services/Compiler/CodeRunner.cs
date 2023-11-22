using Blazemoji.Contracts.Messages;
using Blazemoji.Contracts.Models;
using Blazemoji.Services.Execution;
using MassTransit;
using Microsoft.Extensions.Hosting.Internal;

namespace Blazemoji.Services.Compiler
{
    public class CodeRunner : ICodeRunner
    {
        public Func<string, Task<ExecuteCodeResult>> _codeExecutor;
        private readonly ILogger<CodeRunner> _logger;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly IRequestClient<IExecuteCodeRequest> _requestClient;
        private readonly ICompilerService _compilerService;
        private readonly ICodeExecutionService _codeExecutionService;

        public CodeRunner(ILogger<CodeRunner> logger,
                IHostEnvironment hostEnvironment,
                IRequestClient<IExecuteCodeRequest> requestClient,
                ICompilerService compilerService,
                ICodeExecutionService codeExecutionService) 
        {
            _logger = logger;
            _hostEnvironment = hostEnvironment;
            _requestClient = requestClient;
            _compilerService = compilerService;
            _codeExecutionService = codeExecutionService;

            if (_hostEnvironment.IsProduction())
            {
                _codeExecutor = ExecuteInternalAsync;
            }
            else
            {
                _codeExecutor = ExecuteExternalAsync; 
            }

        }
        public async Task<ExecuteCodeResult> ExecuteAsync(string code)
        {
            return await _codeExecutor.Invoke(code);
        }

        private async Task<ExecuteCodeResult> ExecuteInternalAsync(string code)
        {
            var compilerResult = await Task.Run(() => _compilerService.CompileEmojicode(code));

            if (compilerResult.Error)
                return compilerResult; 

            return await Task.Run(() => _codeExecutionService.ProcessCode(compilerResult.Result));
        }

        private async Task<ExecuteCodeResult> ExecuteExternalAsync(string code)
        {
            var compilerApiResult = await _requestClient.GetResponse<ExecuteCodeResult>(new { EmojicodeProgram = code }, CancellationToken.None);

            return compilerApiResult.Message;
        }
    }
}
