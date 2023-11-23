using Blazemoji.Contracts.Messages;
using Blazemoji.Contracts.Models;
using MassTransit;
using EnvironmentName = Microsoft.AspNetCore.Hosting.EnvironmentName;

namespace Blazemoji.Services.Compiler
{
    public class CodeRunner : ICodeRunner
    {
        public Func<string, Task<EmojicodeResult>> _emojicodeCompilerDelegate;
        private readonly ILogger<CodeRunner> _logger;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly IRequestClient<IExecuteCodeRequest> _requestClient;
        private readonly ICompilerService _compilerService;

        public CodeRunner(ILogger<CodeRunner> logger,
                IHostEnvironment hostEnvironment,
                IRequestClient<IExecuteCodeRequest> requestClient,
                ICompilerService compilerService)
        {
            _logger = logger;
            _hostEnvironment = hostEnvironment;
            _requestClient = requestClient;
            _compilerService = compilerService;

            if (_hostEnvironment.IsProduction())
            {
                _logger.LogInformation("Registering {Delegate} as execution delegate", nameof(ExecuteInternalAsync));
                _emojicodeCompilerDelegate = ExecuteInternalAsync;
            }
            else
            {
                _logger.LogInformation("Registering {Delegate} as execution delegate", nameof(ExecuteExternalAsync));
                _emojicodeCompilerDelegate = ExecuteExternalAsync; 
            }
        }

        public async Task<EmojicodeResult> ExecuteAsync(string code)
        {
            return await _emojicodeCompilerDelegate.Invoke(code);
        }

        private async Task<EmojicodeResult> ExecuteInternalAsync(string code)
        {
            _logger.LogInformation("Executing internal code request for {Code}", code);
            return await _compilerService.CompileAndExecuteAsync(code);
        }

        private async Task<EmojicodeResult> ExecuteExternalAsync(string code)
        {
            _logger.LogInformation("Executing external code request for {Code}", code);
            var compilerApiResult = await _requestClient.GetResponse<EmojicodeResult>(new { EmojicodeProgram = code }, CancellationToken.None);

            return compilerApiResult.Message;
        }
    }
}
