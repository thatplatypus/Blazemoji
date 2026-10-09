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
        // Letters with accents are written as they are, not as \u escapes: the file is for a
        // person to read too, and it is never put inside a page. The writer still escapes
        // emoji, as it does every character outside the basic plane; they read back the same.
        private static readonly JsonWriterOptions Readable = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static string Write(IEnumerable<SettingsBase> sections, SettingHosts host)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, Readable))
            {
                writer.WriteStartObject();
                foreach (var section in sections.Where(section => (section.Hosts & host) != 0))
                {
                    var changed = SettingProperties.Of(section)
                        .Where(setting => setting.IsFor(host) && !Equals(setting.Read(section), setting.Default))
                        .ToList();
                    if (changed.Count == 0)
                        continue;

                    writer.WriteStartObject(section.SettingsId);
                    foreach (var setting in changed)
                    {
                        writer.WritePropertyName(KeyOf(setting));
                        switch (setting.Read(section))
                        {
                            case bool flag: writer.WriteBooleanValue(flag); break;
                            case int whole: writer.WriteNumberValue(whole); break;
                            case double fraction: writer.WriteNumberValue(fraction); break;
                            case string text: writer.WriteStringValue(text); break;
                            default: writer.WriteNullValue(); break;
                        }
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
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
