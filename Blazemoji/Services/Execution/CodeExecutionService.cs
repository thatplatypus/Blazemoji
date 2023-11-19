using System.Diagnostics;
using Blazemoji.Shared.Models.Execution;

namespace Blazemoji.Services.Execution
{
    public class CodeExecutionService : ICodeExecutionService
    {
        private readonly int _timeoutThreshhold = 60_000;

        public ProcessedCodeResult ProcessCode(string filename)
        {
            var timer = new Stopwatch();
            var codeResult = new ProcessedCodeResult();

            //RunCompiled Code
            Process compiledCode = new Process();
            compiledCode.StartInfo.FileName = filename;
            compiledCode.StartInfo.UseShellExecute = false;
            compiledCode.StartInfo.RedirectStandardOutput = true;
            compiledCode.StartInfo.RedirectStandardError = true;
            compiledCode.StartInfo.CreateNoWindow = true;

            timer.Start();
            compiledCode.Start();

            string codeOutput = string.Empty;
            string codeError = string.Empty;

            compiledCode.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    codeOutput += e.Data + Environment.NewLine;
                }
            };

            compiledCode.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    codeError += e.Data + Environment.NewLine;
                }
            };

            compiledCode.BeginOutputReadLine();
            compiledCode.BeginErrorReadLine();

            //1 Minute timeout
            var exited = compiledCode.WaitForExit(_timeoutThreshhold);
            if (!exited)
            {
                compiledCode.Kill();

                codeResult.Error = true;
                codeResult.Message = $"Process exited due to timeout of {_timeoutThreshhold / 1000} seconds";
                codeResult.Result = codeOutput;
            }
            else
            {
                codeResult.Error = !string.IsNullOrWhiteSpace(codeError);
                codeResult.Message = codeError;
                codeResult.Result = codeOutput;
            }

            timer.Stop();
            codeResult.ExecutionTime = timer.Elapsed;

            return codeResult;
        }

        public async Task<ProcessedCodeResult> ProcessCodeAsync(string filename)
        {
            return await Task.Run(() => ProcessCode(filename));
        }
    }
}
