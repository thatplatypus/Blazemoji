using System;
using System.Diagnostics;

namespace Blazemoji.Services.Compiler
{
    public static class EmojicodeProcessFactory
    {
        public static Process Create(string path, string? arguments = default, string? directory = default)
        {
            Process emojicode = new();
            emojicode.StartInfo.FileName = path;
            emojicode.StartInfo.UseShellExecute = false;
            emojicode.StartInfo.RedirectStandardOutput = true;
            emojicode.StartInfo.RedirectStandardError = true;
            emojicode.StartInfo.CreateNoWindow = true;
            if (!string.IsNullOrEmpty(directory))
            {
                emojicode.StartInfo.WorkingDirectory = directory;
            }
            else
            {
                emojicode.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            }

            if (!string.IsNullOrWhiteSpace(arguments))
                emojicode.StartInfo.Arguments = arguments;

            return emojicode;
        }
    }
}
