using System.Text.Json;
using Blazemoji.Shared.Models.Layout;

namespace Blazemoji.Services.Layout
{
    /// <summary>
    /// A layout as the text a store keeps, the same wherever it is kept. Reading forgives:
    /// what is missing comes from the default, what makes no sense is mended, and text that
    /// is not a layout at all is no layout.
    /// </summary>
    public static class WorkspaceLayoutJson
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        public static string Write(WorkspaceLayout layout) =>
            JsonSerializer.Serialize(new Kept(layout.SidebarShare, layout.EditorShare, layout.SidebarHidden), Json) + "\n";

        public static WorkspaceLayout? Read(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                if (JsonSerializer.Deserialize<Kept>(text, Json) is not { } kept)
                    return null;

                // An older version may not have kept everything a newer one does.
                var otherwise = WorkspaceLayout.Default;
                return new WorkspaceLayout(
                    kept.SidebarShare ?? otherwise.SidebarShare,
                    kept.EditorShare ?? otherwise.EditorShare,
                    kept.SidebarHidden ?? otherwise.SidebarHidden).Mended();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private sealed record Kept(double? SidebarShare, double? EditorShare, bool? SidebarHidden);
    }
}
