using Blazemoji.Contracts.Models;

namespace Blazemoji.Services.Compiler
{
    public class CodeRunner : ICodeRunner
    {
        public Func<string, Task<EmojicodeResult>> _emojicodeCompilerDelegate;
        private readonly ILogger<CodeRunner> _logger;
        private readonly ICompilerService _compilerService;

        public CodeRunner(ILogger<CodeRunner> logger,
                ICompilerService compilerService)
        {
            _logger = logger;
            _compilerService = compilerService;
        }

        public async Task<EmojicodeResult> ExecuteAsync(string code)
        {
            _logger.LogExecutingCode("internal", code);
            return await _compilerService.CompileAndExecuteAsync(code);
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
