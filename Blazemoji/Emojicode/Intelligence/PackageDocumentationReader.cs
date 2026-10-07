using System.Text.Json;

namespace Blazemoji.Emojicode.Intelligence
{
    /// <summary>
    /// Reads the report <c>emojicodec -r</c> writes for a package.
    /// </summary>
    public static class PackageDocumentationReader
    {
        /// <returns>Null when the text is not a documentation report.</returns>
        public static PackageDocumentation? Read(string packageName, string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("types", out var types) || types.ValueKind != JsonValueKind.Array)
                    return null;

                var read = types.EnumerateArray()
                    .Where(type => type.ValueKind == JsonValueKind.Object && Text(type, "name").Length > 0)
                    .Select(type => ReadType(packageName, type))
                    .ToList();

                return new PackageDocumentation(packageName, Tidy(Text(root, "documentation")), read);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static TypeDocumentation ReadType(string packageName, JsonElement type) => new(
            packageName,
            Text(type, "name"),
            Text(type, "type") switch
            {
                "Class" => TypeKind.Class,
                "Value Type" => TypeKind.ValueType,
                "Protocol" => TypeKind.Protocol,
                "Enum" => TypeKind.Enumeration,
                _ => TypeKind.Other,
            },
            Tidy(Text(type, "documentation")),
            ReadMethods(type, "methods"),
            ReadMethods(type, "typeMethods"),
            ReadMethods(type, "initializers"));

        private static List<MethodDocumentation> ReadMethods(JsonElement type, string property)
        {
            if (!type.TryGetProperty(property, out var methods) || methods.ValueKind != JsonValueKind.Array)
                return [];

            return methods.EnumerateArray()
                .Where(method => method.ValueKind == JsonValueKind.Object)
                .Select(method => new MethodDocumentation(
                    Text(method, "name"),
                    Text(method, "mood") is { Length: > 0 } mood ? mood : "❗️",
                    Tidy(Text(method, "documentation")),
                    ReadParameters(method),
                    method.TryGetProperty("returnType", out var returnType) ? ReadTypeReference(returnType) : null))
                .ToList();
        }

        private static List<ParameterDocumentation> ReadParameters(JsonElement method)
        {
            if (!method.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Array)
                return [];

            return parameters.EnumerateArray()
                .Where(parameter => parameter.ValueKind == JsonValueKind.Object)
                .Select(parameter => new ParameterDocumentation(
                    Text(parameter, "name"),
                    (parameter.TryGetProperty("type", out var type) ? ReadTypeReference(type) : null) ?? Unknown))
                .ToList();
        }

        private static readonly TypeReference Unknown = new("?", null);

        /// <returns>Null for "returns nothing".</returns>
        private static TypeReference? ReadTypeReference(JsonElement type)
        {
            if (type.ValueKind != JsonValueKind.Object)
                return Unknown;

            var arguments = type.TryGetProperty("arguments", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(argument => ReadTypeReference(argument) ?? Unknown).ToList()
                : [];

            switch (Text(type, "type"))
            {
                case "NoReturn":
                    return null;

                case "Literal":
                    return new TypeReference(Text(type, "name"), null);

                case "Optional" when arguments.Count == 1:
                    return new TypeReference("🍬" + arguments[0].Display, arguments[0].TypeName);

                case "Callable":
                    var parameters = type.TryGetProperty("parameters", out var callableParameters) && callableParameters.ValueKind == JsonValueKind.Array
                        ? callableParameters.EnumerateArray().Select(parameter => (ReadTypeReference(parameter) ?? Unknown).Display)
                        : [];
                    var returned = type.TryGetProperty("return", out var callableReturn) ? ReadTypeReference(callableReturn) : null;
                    return new TypeReference($"🍇{string.Join(' ', parameters)}{(returned is null ? string.Empty : "➡️" + returned.Display)}🍉", null);

                case "" when Text(type, "name") is { Length: > 0 } name:
                    var display = arguments.Count == 0 ? name : $"{name}🐚{string.Join(' ', arguments.Select(argument => argument.Display))}🍆";
                    return new TypeReference(display, name);

                default:
                    return Unknown;
            }
        }

        private static string Text(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

        /// <summary>
        /// Doc comments come with the indentation they had in the source. The indentation of
        /// the first line is taken off every line that has it, so that Markdown does not read
        /// the text as a code block. Lines with less are left where they are.
        /// </summary>
        private static string Tidy(string documentation)
        {
            var lines = documentation.ReplaceLineEndings("\n").Split('\n');
            var first = lines.FirstOrDefault(line => line.Trim().Length > 0) ?? string.Empty;
            var indent = first.Length - first.TrimStart().Length;

            return string.Join('\n', lines.Select(line => Unindent(line, indent))).Trim();
        }

        private static string Unindent(string line, int indent)
        {
            var leading = line.Length - line.TrimStart().Length;
            return line[Math.Min(leading, indent)..].TrimEnd();
        }
    }
}
