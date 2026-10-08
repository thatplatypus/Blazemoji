using System.Text;

namespace Blazemoji.Services.Projects
{
    /// <summary>
    /// The folder on disk that projects and saved snippets are kept under, and the few things
    /// everything kept there does the same way: text is UTF-8, a file is never left half
    /// written, and nothing is destroyed. What would be deleted or written over goes to
    /// <c>.blazemoji/trash</c>, in a folder named for the moment and for where it came from.
    /// </summary>
    internal sealed class ProjectsFolder(string root, TimeProvider clock)
    {
        public const string OwnFolder = ".blazemoji";

        private const string TrashFolder = "trash";

        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private static readonly char[] NotInAName = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];
        private static readonly HashSet<string> NamesWindowsKeeps = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>A leading <c>~/</c> is the user's home.</summary>
        public string Root { get; } =
            root.StartsWith("~/", StringComparison.Ordinal) || root == "~"
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), root[1..].TrimStart('/'))
                : Path.GetFullPath(root);

        public static string In(string folder, string relativePath) => Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));

        /// <returns>The file's text, or null when it is not there or is not text.</returns>
        public static async Task<string?> ReadTextAsync(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                var text = Utf8.GetString(await File.ReadAllBytesAsync(path));
                return text.Contains('\0') ? null : text.TrimStart('﻿');
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        /// <summary>Writes beside the file and then moves into place, so a file is never left half written.</summary>
        public static async Task WriteTextAsync(string path, string text)
        {
            var folder = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(folder);
            var beside = Path.Combine(folder, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
            await File.WriteAllBytesAsync(beside, Utf8.GetBytes(text));
            File.Move(beside, path, overwrite: true);
        }

        /// <param name="what">The file or folder to move.</param>
        /// <param name="from">The folder it belongs to, whose name the place in the trash is given.</param>
        /// <param name="relativePath">The file's path inside <paramref name="from"/>, or null when <paramref name="what"/> is that whole folder.</param>
        public void MoveToTrash(string what, string from, string? relativePath)
        {
            var trash = Path.Combine(Root, OwnFolder, TrashFolder);
            var named = clock.GetLocalNow().ToString("yyyy-MM-dd HHmmss") + " " + Path.GetFileName(from);

            for (var attempt = 1; ; attempt++)
            {
                var place = Path.Combine(trash, attempt == 1 ? named : $"{named} {attempt}");
                var target = relativePath is null ? place : In(place, relativePath);
                if (File.Exists(target) || (relativePath is null && Directory.Exists(target)))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (relativePath is null)
                    Directory.Move(what, target);
                else
                    File.Move(what, target);

                return;
            }
        }

        /// <summary>A name as a file or folder can be named on any of the systems the app runs on.</summary>
        public static string NameOnDisk(string name, string whenNothingIsLeft)
        {
            var onDisk = new string(name.Select(c => char.IsControl(c) || NotInAName.Contains(c) ? '-' : c).ToArray())
                .TrimStart('.', ' ')
                .TrimEnd('.', ' ');

            if (onDisk.Length == 0)
                return whenNothingIsLeft;

            return NamesWindowsKeeps.Contains(onDisk) ? onDisk + "-" : onDisk;
        }

        public static async Task<T> Guarded<T>(Func<Task<T>> useTheDisk)
        {
            try
            {
                return await useTheDisk();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
            {
                throw new ProjectStoreException("The projects folder could not be used.", exception);
            }
        }

        public static Task Guarded(Func<Task> useTheDisk) => Guarded(async () =>
        {
            await useTheDisk();
            return true;
        });
    }
}
