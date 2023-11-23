using Blazemoji.Contracts.Messages;
using Blazemoji.Services.Compiler;
using MassTransit;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Blazemoji.Compiler
{
    public class CodeExecutionConsumer(
        ILogger<CodeExecutionConsumer> logger,
        ICompilerService compilerService) : IConsumer<IExecuteCodeRequest>
    {
        private readonly ILogger<CodeExecutionConsumer> _logger = logger;
        private readonly ICompilerService _compilerService = compilerService;

        public async Task Consume(ConsumeContext<IExecuteCodeRequest> context)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));
            ArgumentNullException.ThrowIfNull(context?.Message, nameof(context.Message));
            ArgumentException.ThrowIfNullOrWhiteSpace(context?.Message?.EmojicodeProgram);

            var code = context.Message.EmojicodeProgram;

            _logger.LogInformation("Consumed message {MessageId} compile requested", context.MessageId);

            var compilerResult = _compilerService.CompileEmojicode(code);

            if (compilerResult.Error)
            {
                _logger.LogError("Message {MessageId} error {Error}", context.MessageId, compilerResult.Message);
                await context.RespondAsync(compilerResult);
                return;
            }

            var executionResult = _compilerService.ExecuteCode(compilerResult.Result);

            _logger.LogInformation("Message {MessageId} result {Result}", context.MessageId, executionResult.Result);

            await context.RespondAsync(executionResult);
        }
    }
}
