namespace Blazemoji.Services.Compiler
{
    public class CompilerService : ICompilerService
    {
        public EmojicodecResult CompileEmojicode(string code)
        { 
            var result = new EmojicodecResult();
            string outputFileName = "temp.o";

            //Save code to temp emojicode file
            string tempPath = Directory.GetCurrentDirectory() + "/" + "temp." + Path.GetRandomFileName() + ".🍇";
            File.WriteAllText(tempPath, code);

            var processStartInfo = new ProcessStartInfo("emojicodec/emojicodec")
            {
                Arguments = tempPath + $" -o {outputFileName}",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            //Compile file to binary
            Process process = new()
            {
                StartInfo = processStartInfo
            };

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
                result.Result = outputFileName;

            try
            {
                File.Delete(tempPath);
                Console.WriteLine("Deleted file: " + tempPath);
            }
            catch
            {
                //File already gone
            }
        
            return result;
        }
    }
}
