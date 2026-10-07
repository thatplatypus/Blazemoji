namespace Blazemoji.Toolchain
{
    /// <summary>
    /// Source file names come from the caller and are written to disk, so they are
    /// restricted to forward-slash relative paths that stay inside the build directory.
    /// </summary>
    public static class SourceFileNames
    {
        public static bool IsSafe(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (name.IndexOfAny(['\0', '\\', ':']) >= 0)
                return false;

            foreach (var segment in name.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                    return false;
            }

            return true;
        }
    }
}
