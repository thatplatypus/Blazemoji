using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Services.Settings
{
    /// <summary>A value the kept text gives a setting, already checked and brought within its limits.</summary>
    public sealed record KeptSetting(SettingsBase Section, SettingProperty Setting, object? Value);

    /// <summary>
    /// Settings as the text a store keeps, the same wherever it is kept: one object for each
    /// section, holding only what is not at its default. Reading forgives. Text that is not
    /// such an object is no settings, and a value that could not have come from the panel is
    /// passed over.
    /// </summary>
    public static class SettingsJson
    {
        public static string Write(IEnumerable<SettingsBase> sections, SettingHosts host)
        {
            var sb = new StringBuilder();

            var firstSection = true;
            foreach (var section in sections.Where(section => (section.Hosts & host) != 0))
            {
                var changed = SettingProperties.Of(section)
                    .Where(setting => setting.IsFor(host) && !Equals(setting.Read(section), setting.Default))
                    .ToList();

                if (changed.Count == 0)
                    continue;

                if (firstSection)
                    sb.AppendLine("{");
                else
                    sb.AppendLine(",");

                firstSection = false;

                sb.Append("  \"").Append(section.SettingsId).AppendLine("\": {");

                var firstSetting = true;
                foreach (var setting in changed)
                {
                    if (!firstSetting)
                        sb.AppendLine(",");
                    firstSetting = false;

                    sb.Append("    \"").Append(KeyOf(setting)).Append("\": ");

                    switch (setting.Read(section))
                    {
                        case bool flag:
                            sb.Append(flag ? "true" : "false");
                            break;
                        case int whole:
                            sb.Append(whole);
                            break;
                        case double fraction:
                            sb.Append(fraction);
                            break;
                        case string text:
                            sb.Append('"').Append(EscapeJsonString(text)).Append('"');
                            break;
                    }
                }

                sb.AppendLine();
                sb.Append("  }");
            }

            if (firstSection)
                return "{}\n";

            sb.AppendLine();
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string EscapeJsonString(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append($"\\u{(int)c:x4}");
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static IReadOnlyList<KeptSetting> Read(string? text, IEnumerable<SettingsBase> sections, SettingHosts host)
        {
            if (string.IsNullOrWhiteSpace(text))
                return [];

            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return [];

                var kept = new List<KeptSetting>();
                foreach (var section in sections.Where(section => (section.Hosts & host) != 0))
                {
                    if (!document.RootElement.TryGetProperty(section.SettingsId, out var values) || values.ValueKind != JsonValueKind.Object)
                        continue;

                    foreach (var setting in SettingProperties.Of(section).Where(setting => setting.IsFor(host)))
                    {
                        if (values.TryGetProperty(KeyOf(setting), out var value) && setting.TryAccept(Plain(value), out var accepted))
                            kept.Add(new KeptSetting(section, setting, accepted));
                    }
                }

                return kept;
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static string KeyOf(SettingProperty setting) => JsonNamingPolicy.CamelCase.ConvertName(setting.Name);

        // A number too large to be one comes out as infinity or not at all, and either way is refused.
        private static object? Plain(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String => value.GetString(),
            _ => null,
        };
    }
}
