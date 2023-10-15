using System.Diagnostics;
using Blazemoji.Models.Compiler;

namespace Blazemoji.Services.Compiler
{
    public class CompilerService : ICompilerService
    {
        public EmojicodecResult CompileEmojicode(string code)
        {
            var result = new EmojicodecResult();
            string outputFileName = "temp";

            //Save code to temp emojicode file
            string tempPath = Path.GetTempFileName() + ".🍇";
            File.WriteAllText(tempPath, code);

            //Compile file to binaryy
            Process process = new Process();
            process.StartInfo.FileName = "emojicodec/emojicodec";
            process.StartInfo.Arguments = tempPath + $" -o {outputFileName}";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            process.Start();

            string standardOutput = string.Empty;
            string standardError = string.Empty;

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    standardOutput += e.Data + Environment.NewLine;
                    result.Message = standardOutput;
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    standardError += e.Data + Environment.NewLine;
                    result.Error = true;
                    result.Message = standardError;
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();

            if(!result.Error)
            {
                result.Result = outputFileName;
            }

            return result;
        }
    }
}
