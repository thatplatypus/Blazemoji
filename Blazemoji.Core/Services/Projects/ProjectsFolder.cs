using System.Text;

namespace Blazemoji.Services.Projects
{
    /// <summary>
    /// The folder on disk that projects and saved snippets are kept under, and the few things
    /// everything kept there does the same way: one thing at a time, text as UTF-8, no file
    /// left half written, and nothing destroyed. What would be deleted or written over goes to
    /// <c>.blazemoji/trash</c>, in a folder named for the moment and for where it came from.
    /// </summary>
    internal sealed class ProjectsFolder(string root, TimeProvider clock)
    {
        public const string OwnFolder = ".blazemoji";

        /// <summary>The folder under the root that saved snippets are kept in. No project is ever given it.</summary>
        public const string SnippetsFolder = "Snippets";

        private const string TrashFolder = "trash";

        // A name on disk can be 255 bytes on most file systems. This leaves room for a number after it.
        private const int LongestTrashName = 240;

        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];
        private static readonly char[] NotInAName = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];
        private static readonly HashSet<string> NamesWindowsKeeps = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        private Task _last = Task.CompletedTask;

        /// <summary>A leading <c>~/</c> is the user's home. Set to nothing, it is the usual folder.</summary>
        public string Root { get; } = Expanded(string.IsNullOrWhiteSpace(root) ? new FileProjectStoreOptions().Root : root);

        private static string Expanded(string root) =>
            root.StartsWith("~/", StringComparison.Ordinal) || root == "~"
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), root[1..].TrimStart('/'))
                : Path.GetFullPath(root);

        /// <summary>
        /// Does one thing with the disk once everything asked for before it has finished, and
        /// reports a disk that cannot be used as a <see cref="ProjectStoreException"/>. Saves
        /// arrive faster than a slow disk takes them, and two at once would each undo part of
        /// the other. Nothing is locked: each waits for the one before it, which is safe on a
        /// window's own thread where a contended lock is not.
        /// </summary>
        public async Task<T> InTurn<T>(Func<Task<T>> useTheDisk)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var before = Interlocked.Exchange(ref _last, done.Task);
            try
            {
                await before;
                return await useTheDisk();
            }
            catch (Exception exception) when (IsTheDisksDoing(exception))
            {
                throw new ProjectStoreException("The projects folder could not be used.", exception);
            }
            finally
            {
                done.SetResult();
            }
        }

        public Task InTurn(Func<Task> useTheDisk) => InTurn(async () =>
        {
            await useTheDisk();
            return true;
        });

        /// <summary>True for what a disk does when it will not be read or written, as opposed to a mistake in the code.</summary>
        public static bool IsTheDisksDoing(Exception exception) =>
            exception is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException or EncoderFallbackException;

        public static string In(string folder, string relativePath) => Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>The same name, to a disk that does not tell the cases of letters apart or how an accent is put on.</summary>
        public static bool SameName(string one, string other) =>
            string.Equals(one.Normalize(), other.Normalize(), StringComparison.OrdinalIgnoreCase);

        public static bool IsALink(FileSystemInfo entry) => entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

        /// <summary>
        /// True when the folder lists an entry spelt exactly so. Asking whether the path exists
        /// is not the same: on a disk that ignores case, <c>Main</c> exists when only <c>main</c> does.
        /// </summary>
        public static bool IsListedExactly(string path) =>
            Path.GetDirectoryName(path) is { } folder
            && Directory.Exists(folder)
            && Directory.EnumerateFileSystemEntries(folder).Any(entry => Path.GetFileName(entry) == Path.GetFileName(path));

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

        /// <summary>
        /// True when the file holds exactly this text, as this class writes it or as it reads
        /// it. Anything else is in the file because something else put it there.
        /// </summary>
        public static async Task<bool> HoldsAsync(string path, string? text)
        {
            if (text is null)
                return false;

            var onDisk = await File.ReadAllBytesAsync(path);
            var expected = Utf8.GetBytes(text);
            return onDisk.AsSpan().SequenceEqual(expected)
                || (onDisk.AsSpan().StartsWith(ByteOrderMark) && onDisk.AsSpan(ByteOrderMark.Length).SequenceEqual(expected));
        }

        /// <summary>Throws what <see cref="IsTheDisksDoing"/> knows when the text cannot be written as UTF-8, before anything is changed.</summary>
        public static void MustBeWritable(string text) => Utf8.GetByteCount(text);

        /// <summary>Writes beside the file and then moves into place, so a file is never left half written.</summary>
        public static async Task WriteTextAsync(string path, string text)
        {
            var bytes = Utf8.GetBytes(text);
            var folder = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(folder);
            var beside = Path.Combine(folder, "." + Guid.NewGuid().ToString("N")[..12] + ".tmp");
            try
            {
                await File.WriteAllBytesAsync(beside, bytes);
                File.Move(beside, path, overwrite: true);
            }
            catch
            {
                File.Delete(beside);
                throw;
            }
        }

        /// <param name="what">The file or folder to move.</param>
        /// <param name="from">The folder it belongs to, whose name the place in the trash is given.</param>
        /// <param name="relativePath">The file's path inside <paramref name="from"/>, or null when <paramref name="what"/> is that whole folder.</param>
        public void MoveToTrash(string what, string from, string? relativePath)
        {
            var trash = Path.Combine(Root, OwnFolder, TrashFolder);
            var named = Shortened(clock.GetLocalNow().ToString("yyyy-MM-dd HHmmss") + " " + Path.GetFileName(from), LongestTrashName);

            for (var attempt = 1; ; attempt++)
            {
                var place = Path.Combine(trash, attempt == 1 ? named : $"{named} {attempt}");
                var target = relativePath is null ? place : In(place, relativePath);
                if (File.Exists(target) || Directory.Exists(target))
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
            var spelt = new StringBuilder();
            foreach (var rune in name.EnumerateRunes())
            {
                // Half of a character that takes two places reads back as the replacement character.
                var usable = rune != Rune.ReplacementChar && !Rune.IsControl(rune) && !(rune.IsBmp && NotInAName.Contains((char)rune.Value));
                spelt.Append(usable ? rune.ToString() : "-");
            }

            var onDisk = spelt.ToString().TrimStart('.', ' ').TrimEnd('.', ' ');

            if (onDisk.Length == 0)
                return whenNothingIsLeft;

            return NamesWindowsKeeps.Contains(onDisk) ? onDisk + "-" : onDisk;
        }

        /// <summary>The start of the name that fits in so many bytes, cut between characters.</summary>
        private static string Shortened(string name, int bytes)
        {
            var kept = new StringBuilder();
            var used = 0;
            foreach (var rune in name.EnumerateRunes())
            {
                used += rune.Utf8SequenceLength;
                if (used > bytes)
                    break;

                kept.Append(rune.ToString());
            }

            return kept.ToString().TrimEnd();
        }
    }
}
