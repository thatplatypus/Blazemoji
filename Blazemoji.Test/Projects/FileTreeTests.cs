using Blazemoji.Shared.Models.Projects;

namespace Blazemoji.Test.Projects
{
    public class FileTreeTests
    {
        private static IReadOnlyList<FileTreeNode> Build(params string[] paths) =>
            FileTree.Build(paths.Select(path => new ProjectFile(path, string.Empty)).ToList());

        private static string Describe(IEnumerable<FileTreeNode> nodes, int depth = 0) =>
            string.Concat(nodes.Select(node =>
                new string(' ', depth * 2) + (node.IsFolder ? node.Name + "/" : node.Name) + " <" + node.Path + ">\n" + Describe(node.Children, depth + 1)));

        [Fact]
        public void Files_at_the_top_are_listed_by_name()
        {
            Describe(Build("main.🍇", "a.🍇")).ShouldBe("a.🍇 <a.🍇>\nmain.🍇 <main.🍇>\n");
        }

        [Fact]
        public void Folders_come_from_the_paths_and_are_listed_before_files()
        {
            var tree = Build("main.🍇", "app/main.🍇", "app/routes.🍇", "shared/util.🍇");

            Describe(tree).ShouldBe(
                "app/ <app>\n" +
                "  main.🍇 <app/main.🍇>\n" +
                "  routes.🍇 <app/routes.🍇>\n" +
                "shared/ <shared>\n" +
                "  util.🍇 <shared/util.🍇>\n" +
                "main.🍇 <main.🍇>\n");
        }

        [Fact]
        public void Folders_nest()
        {
            var tree = Build("a/b/c/deep.🍇", "a/b/near.🍇");

            Describe(tree).ShouldBe(
                "a/ <a>\n" +
                "  b/ <a/b>\n" +
                "    c/ <a/b/c>\n" +
                "      deep.🍇 <a/b/c/deep.🍇>\n" +
                "    near.🍇 <a/b/near.🍇>\n");
        }

        [Fact]
        public void No_files_is_an_empty_tree()
        {
            Build().ShouldBeEmpty();
        }
    }
}
