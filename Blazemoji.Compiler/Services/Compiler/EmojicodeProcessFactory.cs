using System;
using System.Diagnostics;

namespace Blazemoji.Compiler.Services.Compiler
{
    public static class EmojicodeProcessFactory
    {
        public static Process Create(string path, string? arguments = default)
        {
            Process emojicode = new();
            emojicode.StartInfo.FileName = path;
            emojicode.StartInfo.UseShellExecute = false;
            emojicode.StartInfo.RedirectStandardOutput = true;
            emojicode.StartInfo.RedirectStandardError = true;
            emojicode.StartInfo.CreateNoWindow = true;
            emojicode.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();

            if (!string.IsNullOrWhiteSpace(arguments))
                emojicode.StartInfo.Arguments = arguments;

            return emojicode;
        }
    }
}
