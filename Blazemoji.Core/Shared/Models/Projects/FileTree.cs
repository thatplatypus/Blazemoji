namespace Blazemoji.Shared.Models.Projects
{
    /// <param name="Path">A file's path in the project, or a folder's path without a trailing slash.</param>
    public sealed record FileTreeNode(string Name, string Path, bool IsFolder, IReadOnlyList<FileTreeNode> Children);

    /// <summary>
    /// A project keeps a flat list of files. Folders exist only as the leading parts of their
    /// paths, and this turns the list into the tree a person expects to see.
    /// </summary>
    public static class FileTree
    {
        public static IReadOnlyList<FileTreeNode> Build(IReadOnlyList<ProjectFile> files) =>
            Build(files.Select(file => file.Path).ToList(), prefix: string.Empty);

        private static List<FileTreeNode> Build(IReadOnlyList<string> paths, string prefix)
        {
            var folders = new List<FileTreeNode>();
            var leaves = new List<FileTreeNode>();

            var here = paths.Select(path => path[prefix.Length..]).ToList();
            foreach (var group in here.GroupBy(rest => rest.Split('/')[0]).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var isFolder = group.Any(rest => rest.Contains('/'));
                if (isFolder)
                {
                    var folderPath = prefix + group.Key;
                    var inside = paths.Where(path => path.StartsWith(folderPath + "/", StringComparison.Ordinal)).ToList();
                    folders.Add(new FileTreeNode(group.Key, folderPath, true, Build(inside, folderPath + "/")));
                }
                else
                {
                    leaves.Add(new FileTreeNode(group.Key, prefix + group.Key, false, []));
                }
            }

            return [.. folders, .. leaves];
        }
    }
}
