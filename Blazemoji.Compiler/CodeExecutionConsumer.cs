using Blazemoji.Contracts.Messages;
using Blazemoji.Services.Compiler;
using Blazemoji.Services.Execution;
using MassTransit;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace Blazemoji.Compiler
{
    public class CodeExecutionConsumer(
        ILogger<CodeExecutionConsumer> logger,
        ICompilerService compilerService,
        ICodeExecutionService codeExecutionService) : IConsumer<IExecuteCodeRequest>
    {
        private readonly ILogger<CodeExecutionConsumer> _logger = logger;
        private readonly ICompilerService _compilerService = compilerService;
        private readonly ICodeExecutionService _codeExecutionService = codeExecutionService;
        private static readonly Regex Regex = new Regex(@"\\[uU]([0-9A-Fa-f]{4})");
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true,  Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)};

        public async Task Consume(ConsumeContext<IExecuteCodeRequest> context)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));
            ArgumentNullException.ThrowIfNull(context?.Message, nameof(context.Message));
            ArgumentException.ThrowIfNullOrWhiteSpace(context?.Message?.EmojicodeProgram);

            var code = context.Message.EmojicodeProgram;

            _logger.LogInformation("Consumed message {MessageId}, compile requested for {NewLine}{Code}", context.MessageId, Environment.NewLine, JsonSerializer.Serialize(code, _jsonSerializerOptions));

            var compilerResult = _compilerService.CompileEmojicode(code);

            if (compilerResult.Error)
            {
                await context.RespondAsync(compilerResult);
                return;
            }

            var executionResult = _codeExecutionService.ProcessCode(compilerResult.Result);

            await context.RespondAsync(executionResult);
        }
    }
}
