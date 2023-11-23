using Blazemoji.Compiler.Services.Compiler;
using Blazemoji.Contracts.Models;
using System.Diagnostics;

namespace Blazemoji.Services.Compiler
{
    public class CompilerService : ICompilerService
    {
        private readonly int _timeoutThreshhold = 30_000;

        public EmojicodeResult CompileEmojicode(string code)
        {
            var result = new EmojicodeResult();
            string outputFileName = "temp.o";
            string stdOutput = string.Empty;
            string stdError = string.Empty;
            string tempPath = Directory.GetCurrentDirectory() + "/" + "temp." + Path.GetRandomFileName() + ".🍇";

            try
            {
                File.WriteAllText(tempPath, code);

                //Compile file to binary
                Process process = EmojicodeProcessFactory.Create("emojicodec/emojicodec", tempPath + $" -o {outputFileName}");

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        stdOutput += e?.Data + Environment.NewLine;
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        stdError += e?.Data + Environment.NewLine;
                    }
                };

                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit();
            }
            catch (Exception ex)
            {
                result.Error = true;
                result.Message = ex.Message;
            }
            finally
            {
                File.Delete(tempPath);
                Console.WriteLine("Deleted file: " + tempPath);
            }

            if (!result.Error)
                result.Result = outputFileName;

            return result;
        }

        public EmojicodeResult ExecuteCode(string filename)
        {
            var timer = new Stopwatch();
            var codeResult = new EmojicodeResult();
            string stdOutput = string.Empty;
            string stdError = string.Empty;

            timer.Start();
            Process emojicode = EmojicodeProcessFactory.Create(filename);

            emojicode.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    stdOutput += e?.Data + Environment.NewLine;
                }
            };

            emojicode.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    stdError += e?.Data + Environment.NewLine;
                }
            };

            emojicode.Start();

            emojicode.BeginOutputReadLine();
            emojicode.BeginErrorReadLine();

            var exited = emojicode.WaitForExit(_timeoutThreshhold);
            if (!exited)
            {
                emojicode.Kill();
                codeResult.Error = true;
                codeResult.Message = $"Process exited due to timeout of {_timeoutThreshhold / 1000} seconds";
                codeResult.Result = stdOutput;
            }
            else
            {
                codeResult.Error = !string.IsNullOrWhiteSpace(stdError);
                codeResult.Message = stdError;
                codeResult.Result = stdOutput;
            }

            timer.Stop();
            codeResult.ExecutionTime = timer.Elapsed;

            return codeResult;
        }

        public async Task<EmojicodeResult> ExecuteCodeAsync(string filename)
        {
            return await Task.Run(() => ExecuteCode(filename));
        }
    }
}
