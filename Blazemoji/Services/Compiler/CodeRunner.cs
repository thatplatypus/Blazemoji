using Blazemoji.Contracts.Messages;
using Blazemoji.Contracts.Models;
using MassTransit;

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
                _logger.LogDelegateAssignment(nameof(ExecuteInternalAsync));
                _emojicodeCompilerDelegate = ExecuteInternalAsync;
            }
            else
            {
                _logger.LogDelegateAssignment(nameof(RequestAsync));
                _emojicodeCompilerDelegate = RequestAsync; 
            }
        }

        public async Task<EmojicodeResult> ExecuteAsync(string code)
        {
            return await _emojicodeCompilerDelegate.Invoke(code);
        }

        private async Task<EmojicodeResult> ExecuteInternalAsync(string code)
        {
            _logger.LogExecutingCode("internal", code);
            return await _compilerService.CompileAndExecuteAsync(code);
        }

        private async Task<EmojicodeResult> RequestAsync(string code)
        {
            _logger.LogRequestingCode("external", code);
            var response = await _requestClient.GetResponse<EmojicodeResult>(new { EmojicodeProgram = code }, CancellationToken.None, timeout: RequestTimeout.After(s: 61));
            
            _logger.LogEmojicodeResponseReceived(response.Message);
            return response.Message;
        }
    }
    public static partial class CodeRunnerLogTemplates
    {
        [LoggerMessage(
            Level = LogLevel.Information, 
            Message = "Registering `{Delegate}` as execution delegate")]
        public static partial void LogDelegateAssignment(this ILogger logger, string Delegate);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = @"Executing {destination} code request:
                  {code}")]
        public static partial void LogExecutingCode(this ILogger logger, string destination, string code);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = @"Requesting {destination} code result:
                  {code}")]
        public static partial void LogRequestingCode(this ILogger logger, string destination, string code);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Received emojicode response: {emojicodeResult}")]
        public static partial void LogEmojicodeResponseReceived(this ILogger logger, EmojicodeResult emojicodeResult);
    }



}
