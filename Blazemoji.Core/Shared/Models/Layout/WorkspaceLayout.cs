namespace Blazemoji.Shared.Models.Layout
{
    /// <summary>
    /// How the workspace is divided: how much of its width the sidebar has, how much of the
    /// height beside it the editor has above the output, and whether the sidebar is shown.
    /// These are shares and not pixels, so that a layout means the same in a window of
    /// another size.
    /// </summary>
    /// <param name="SidebarShare">The sidebar's part of the width, from 0 to 1.</param>
    /// <param name="EditorShare">The editor's part of the height it shares with the output, from 0 to 1.</param>
    public sealed record WorkspaceLayout(double SidebarShare, double EditorShare, bool SidebarHidden)
    {
        public const double SmallestShare = 0.05;
        public const double LargestShare = 0.95;

        public static WorkspaceLayout Default { get; } = new(0.25, 0.68, false);

        /// <summary>
        /// The same layout with any share that would leave a panel no room brought back
        /// inside, and any that is not a number replaced by the default. What was kept may
        /// have been written by an older version or edited by hand.
        /// </summary>
        public WorkspaceLayout Mended() => this with
        {
            SidebarShare = Mended(SidebarShare, Default.SidebarShare),
            EditorShare = Mended(EditorShare, Default.EditorShare),
        };

        public static double Mended(double share, double otherwise) =>
            double.IsFinite(share) ? Math.Clamp(share, SmallestShare, LargestShare) : otherwise;
    }
}
