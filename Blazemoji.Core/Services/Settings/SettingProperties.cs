using System.Collections.Concurrent;
using System.Reflection;
using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Services.Settings
{
    /// <summary>One setting of a section: its property, what its attribute says, and its default.</summary>
    public sealed class SettingProperty
    {
        /// <summary>The longest text a setting may hold. A page can send anything, so the limit is here and not on a field.</summary>
        public const int LongestText = 1000;

        private readonly PropertyInfo _property;

        internal SettingProperty(PropertyInfo property, SettingAttribute attribute, object? defaultValue)
        {
            if (property.PropertyType != typeof(bool) && property.PropertyType != typeof(int)
                && property.PropertyType != typeof(double) && property.PropertyType != typeof(string))
                throw new NotSupportedException($"{property.DeclaringType?.Name}.{property.Name} is a {property.PropertyType.Name}. A setting is a bool, an int, a double or a string.");

            if (property.SetMethod is null)
                throw new NotSupportedException($"{property.DeclaringType?.Name}.{property.Name} has no setter. A setting needs one, and it may be private.");

            if (attribute.HasRange && attribute.Min > attribute.Max)
                throw new NotSupportedException($"{property.DeclaringType?.Name}.{property.Name} has a minimum of {attribute.Min} above its maximum of {attribute.Max}. No value could be within both.");

            _property = property;
            Attribute = attribute;
            Default = defaultValue;
            Options = string.IsNullOrWhiteSpace(attribute.Options)
                ? []
                : attribute.Options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public string Name => _property.Name;

        public Type Type => _property.PropertyType;

        public SettingAttribute Attribute { get; }

        public object? Default { get; }

        public IReadOnlyList<string> Options { get; }

        public bool IsFor(SettingHosts host) => (Attribute.Hosts & host) != 0;

        public object? Read(SettingsBase section) => _property.GetValue(section);

        /// <summary>For the settings state alone, which is the one place a setting is changed.</summary>
        public void Assign(SettingsBase section, object? value) => _property.SetValue(section, value);

        /// <summary>
        /// Whether a value may be this setting's, and what it becomes: a number is brought
        /// within the limits. The panel and the kept text both go through here, so nothing
        /// gets in from one that could not get in from the other.
        /// </summary>
        public bool TryAccept(object? value, out object? accepted)
        {
            accepted = null;

            if (Type == typeof(bool))
            {
                if (value is not bool flag)
                    return false;

                accepted = flag;
                return true;
            }

            if (Type == typeof(string))
            {
                if (value is not string text || text.Length > LongestText)
                    return false;

                if (Options.Count > 0 && !Options.Contains(text, StringComparer.Ordinal))
                    return false;

                accepted = text;
                return true;
            }

            double number;
            switch (value)
            {
                case int whole: number = whole; break;
                case long whole: number = whole; break;
                case double fraction: number = fraction; break;
                default: return false;
            }

            if (!double.IsFinite(number))
                return false;

            if (Type == typeof(double))
            {
                accepted = Within(number);
                return true;
            }

            if (number != Math.Truncate(number))
                return false;

            number = Within(number);
            if (number < int.MinValue || number > int.MaxValue)
                return false;

            accepted = (int)number;
            return true;
        }

        private double Within(double number) => Attribute.HasRange ? Math.Clamp(number, Attribute.Min, Attribute.Max) : number;
    }

    /// <summary>The settings shown under one heading. Those with no heading have an empty name.</summary>
    public sealed record SettingGroup(string Name, IReadOnlyList<SettingProperty> Settings);

    /// <summary>The settings of each section type, read once.</summary>
    public static class SettingProperties
    {
        private static readonly ConcurrentDictionary<Type, IReadOnlyList<SettingProperty>> Known = new();

        public static IReadOnlyList<SettingProperty> Of(SettingsBase section) => Of(section.GetType());

        public static IReadOnlyList<SettingProperty> Of(Type sectionType) => Known.GetOrAdd(sectionType, Read);

        /// <summary>Groups in the order each first appears, with the settings in the order given.</summary>
        public static IReadOnlyList<SettingGroup> Grouped(IEnumerable<SettingProperty> settings)
        {
            var groups = new List<(string Name, List<SettingProperty> Settings)>();
            foreach (var setting in settings)
            {
                var name = setting.Attribute.Group ?? string.Empty;
                var group = groups.FirstOrDefault(candidate => candidate.Name == name);
                if (group.Settings is null)
                    groups.Add(group = (name, new List<SettingProperty>()));

                group.Settings.Add(setting);
            }

            return groups.Select(group => new SettingGroup(group.Name, group.Settings)).ToList();
        }

        private static IReadOnlyList<SettingProperty> Read(Type sectionType)
        {
            if (!typeof(SettingsBase).IsAssignableFrom(sectionType) || sectionType.GetConstructor(Type.EmptyTypes) is null)
                throw new NotSupportedException($"{sectionType.Name} must derive from {nameof(SettingsBase)} and have a constructor that takes nothing.");

            // A new one is what holds the defaults: there is nowhere else they are written.
            var fresh = (SettingsBase)Activator.CreateInstance(sectionType)!;
            return sectionType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => (Property: property, Attribute: property.GetCustomAttribute<SettingAttribute>()))
                .Where(found => found.Attribute is not null)
                .OrderBy(found => found.Attribute!.Order)
                .ThenBy(found => found.Property.Name, StringComparer.Ordinal)
                .Select(found => new SettingProperty(found.Property, found.Attribute!, found.Property.GetValue(fresh)))
                .ToList();
        }
    }
}
