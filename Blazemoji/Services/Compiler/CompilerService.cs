using Blazemoji.Contracts.Models;
using System.Diagnostics;

namespace Blazemoji.Services.Compiler
{
    public class CompilerService(ILogger<CompilerService> logger) : ICompilerService
    {
        private readonly int _timeoutThreshhold = 30_000;
        private readonly ILogger<CompilerService> _logger = logger;

        public EmojicodeResult CompileEmojicode(string code)
        {
            var result = new EmojicodeResult();
            string outputFileName = "temp.o";
            string stdOutput = string.Empty;
            string stdError = string.Empty;
            string tempPath = Path.GetTempPath() + "temp." + Path.GetRandomFileName() + ".🍇";
            var directory = Directory.GetCurrentDirectory();

            try
            {
                File.WriteAllText(tempPath, code);

                //Compile file to binary
                Process process = EmojicodeProcessFactory.Create("emojicodec/emojicodec", tempPath + $" -o {Path.Combine(directory, outputFileName)}");

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        stdError += e?.Data + Environment.NewLine;
                    }
                };

                process.Start();

                process.BeginErrorReadLine();

                process.WaitForExit();

                stdOutput = process.StandardOutput.ReadToEnd();

                result.Error = !string.IsNullOrWhiteSpace(stdError);

                if (!result.Error)
                {
                    result.Result = directory + ":" + outputFileName;
                    result.Message = stdOutput;
                }
                else
                {
                    _logger.LogInformation("Compiler error {stdError}", stdError);
                    result.Message = stdError;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compile");
                result.Error = true;
                result.Message = ex.Message;
            }
            finally
            {
                try
                {
                    File.Delete(tempPath);
                    Console.WriteLine("Deleted file: " + tempPath);
                }
                catch { }
            }

            return result;
        }

        public EmojicodeResult ExecuteCode(string filename)
        {
            var timer = new Stopwatch();
            var codeResult = new EmojicodeResult();
            string stdOutput = string.Empty;
            string stdError = string.Empty;
            string directory = filename.Split(":")[0];
            filename = filename.Split(":")[1];

            timer.Start();
            try
            {
                Process emojicode = EmojicodeProcessFactory.Create(filename, directory: directory);

                emojicode.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        stdError += e?.Data + Environment.NewLine;
                    }
                };

                emojicode.Start();

                emojicode.BeginErrorReadLine();

                if (!emojicode.WaitForExit(_timeoutThreshhold))
                {
                    emojicode.Kill();
                    stdOutput = emojicode.StandardOutput.ReadToEnd();
                    codeResult.Error = true;
                    codeResult.Message = $"Process exited due to timeout of {_timeoutThreshhold / 1000} seconds";
                    codeResult.Result = stdOutput;
                }
                else
                {
                    stdOutput = emojicode.StandardOutput.ReadToEnd();
                    codeResult.Error = !string.IsNullOrWhiteSpace(stdError);
                    codeResult.Message = stdError;
                    codeResult.Result = stdOutput;
                }

                timer.Stop();
                codeResult.ExecutionTime = timer.Elapsed;

                return codeResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute code");
                return new EmojicodeResult
                {
                    Error = true,
                    Message = ex.Message
                };
            }
        }

        public async Task<EmojicodeResult> ExecuteCodeAsync(string filename)
        {
            return await Task.Run(() => ExecuteCode(filename));
        }
    }
}
