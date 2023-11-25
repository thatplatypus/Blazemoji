using Blazemoji.Contracts.Models;

namespace Blazemoji.Services.Compiler
{
    public class CompilerService(ILogger<CompilerService> logger) : ICompilerService
    {
        private readonly int _timeoutThreshhold = 30_000;
        private readonly ILogger<CompilerService> _logger = logger;

        public EmojicodeResult CompileEmojicode(string code)
        {
            var directory = Directory.GetCurrentDirectory();
            var result = new EmojicodeResult();
            string tempPath = Path.GetTempPath() + EmojiStringGenerator.GetRandomEmojis(5) + ".🍇";
            string error = string.Empty;
            var outputFile = new CompiledEmojicodeFile
            {
                Path = directory,
                Filename = "temp.o"
            };

            try
            {
                File.WriteAllText(tempPath, code);

                Process process = EmojicodeProcessFactory.Create("emojicodec/emojicodec", tempPath + $" -o {Path.Combine(directory, outputFile.Filename)}");

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        error += e?.Data + Environment.NewLine;
                    }
                };

                process.Start();

                process.BeginErrorReadLine();

                process.WaitForExit();

                HandleProcessResults(process, result, error);
                
                result.CompiledFile = outputFile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compile");
                result.Error = true;
                result.Message = ex.Message;
            }
            finally
            {
                TryDeleteFile(tempPath);
            }

            return result;
        }

        public EmojicodeResult ExecuteCode(CompiledEmojicodeFile file)
        {
            var result = ExecuteCode($"{file.Path}:{file.Filename}");

            TryDeleteFile(Path.Combine(file.Path, file.Filename));

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
                    HandleProcessResults(emojicode, codeResult, stdError);
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

        private void HandleProcessResults(Process process, EmojicodeResult result, string? error)
        {
            string output = process.StandardOutput.ReadToEnd();

            result.Error = !string.IsNullOrWhiteSpace(error);

            if (!result.Error)
            {
                result.Result = output;
                result.Message = process.StandardOutput.ReadToEnd();
            }
            else
            {
                _logger.LogInformation("Process error {error}", error);
                result.Message = error ?? "Process error";
            }
        }

        private void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
                _logger.LogInformation("Deleted file {path}", path);
            }
            catch(Exception ex) 
            { 
                _logger.LogError(ex, "Failed to delete file {path}", path);  
            }
        }
    }
}
