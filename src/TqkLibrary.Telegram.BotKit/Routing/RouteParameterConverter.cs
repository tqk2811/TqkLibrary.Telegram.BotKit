using System.Globalization;

namespace TqkLibrary.Telegram.BotKit.Routing
{
    /// <summary>
    /// Converts string ↔ typed value for placeholders in a route template.
    /// Supported types: string, int, long, bool, Guid, enum (by name or numeric value).
    /// Nullable T is treated as T (a missing segment throws — route templates have no optionals).
    /// </summary>
    internal static class RouteParameterConverter
    {
        /// <summary>
        /// Resolves a constraint into a list of CLR types. Supports multi-type form <c>guid|int</c>
        /// (the pipe inside <c>{...}</c> — already split by the parser). Null/empty → <c>[typeof(string)]</c>.
        /// </summary>
        public static Type[] ResolveClrTypes(string? constraint)
        {
            if (string.IsNullOrEmpty(constraint)) return [typeof(string)];
#if NET5_0_OR_GREATER
            string[] parts = constraint!.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
#else
            string[] split = constraint!.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            var trimmed = new List<string>(split.Length);
            foreach (string s in split)
            {
                string t = s.Trim();
                if (t.Length > 0) trimmed.Add(t);
            }
            string[] parts = trimmed.ToArray();
#endif
            if (parts.Length == 0) return [typeof(string)];
            var result = new Type[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = ResolveSingleClrType(parts[i]);
            return result;
        }

        /// <summary>Kept for compatibility: returns the first type in <see cref="ResolveClrTypes"/>.</summary>
        public static Type ResolveClrType(string? constraint) => ResolveClrTypes(constraint)[0];

        static Type ResolveSingleClrType(string name) => name.ToLowerInvariant() switch
        {
            "string" => typeof(string),
            "int" => typeof(int),
            "long" => typeof(long),
            "bool" => typeof(bool),
            "guid" => typeof(Guid),
            _ => throw new InvalidOperationException(
                $"Unknown route constraint '{name}'. Supported: string, int, long, bool, guid.")
        };

        /// <summary>
        /// Parse a raw segment into the target type. <paramref name="targetType"/> determines the destination —
        /// this allows enums (from a method parameter) even when the template has no constraint.
        /// </summary>
        public static bool TryParse(string raw, Type targetType, out object? value)
        {
            Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlying == typeof(string)) { value = raw; return true; }
            if (underlying == typeof(int))
            {
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) { value = i; return true; }
            }
            else if (underlying == typeof(long))
            {
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) { value = l; return true; }
            }
            else if (underlying == typeof(bool))
            {
                if (bool.TryParse(raw, out bool b)) { value = b; return true; }
            }
            else if (underlying == typeof(Guid))
            {
                if (Guid.TryParse(raw, out Guid g)) { value = g; return true; }
            }
            else if (underlying.IsEnum)
            {
#if NET5_0_OR_GREATER
                bool parsed = Enum.TryParse(underlying, raw, ignoreCase: true, out object? e);
#else
                object? e = null;
                bool parsed;
                try { e = Enum.Parse(underlying, raw, ignoreCase: true); parsed = e is not null; }
                catch (ArgumentException) { parsed = false; }
                catch (OverflowException) { parsed = false; }
#endif
                if (parsed && e is not null
                    && (char.IsLetter(raw[0]) || Enum.IsDefined(underlying, e)))
                { value = e; return true; }
            }

            value = null;
            return false;
        }

        /// <summary>Render a value to its string form for inclusion in callback data.</summary>
        public static string Format(object? value)
        {
            if (value is null) return string.Empty;
            return value switch
            {
                string s => s,
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                Guid g => g.ToString("D"),
                Enum e => e.ToString(),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            };
        }
    }
}
