namespace TqkLibrary.Telegram.BotKit.Routing
{
    /// <summary>
    /// Route template for <see cref="Attributes.InlineButtonAttribute"/>.
    /// Segments are separated by <c>|</c> (intra-module) or <c>/</c> (module prefix boundary); each segment is
    /// either a literal or a placeholder <c>{name}</c> / <c>{name:constraint}</c>.
    /// Examples: <c>"check_source|{id:guid}|apv"</c>, <c>"cs/{id:guid}|apv"</c>, <c>"wallet|{id:guid}"</c>.
    /// </summary>
    public sealed class RouteTemplate
    {
        public const char PipeSeparator = '|';
        public const char SlashSeparator = '/';
        static readonly char[] Separators = [PipeSeparator, SlashSeparator];

        /// <summary>Telegram callback_data hard limit 64 bytes (UTF-8).</summary>
        public const int MaxCallbackDataBytes = 64;

        readonly string _raw;
        readonly RouteSegment[] _segments;
        readonly char[] _separatorsBetween; // length = _segments.Length - 1; char separator preceding segment[i+1]
        readonly RouteParameter[] _parameters;

        RouteTemplate(string raw, RouteSegment[] segments, char[] separatorsBetween, RouteParameter[] parameters)
        {
            _raw = raw;
            _segments = segments;
            _separatorsBetween = separatorsBetween;
            _parameters = parameters;
        }

        /// <summary>The original template string.</summary>
        public string Raw => _raw;

        /// <summary>First literal segment — used as the prefix index for fast callback routing.</summary>
        public string Prefix => _segments[0].IsLiteral
            ? _segments[0].Literal!
            : throw new InvalidOperationException(
                $"Route template '{_raw}' must start with a literal segment (prefix).");

        public IReadOnlyList<RouteParameter> Parameters => _parameters;

        public int SegmentCount => _segments.Length;

        public static RouteTemplate Parse(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
                throw new ArgumentException("Route template must not be empty.", nameof(template));

            // Split manually so we remember the separator char between each pair of segments.
            // Brace depth prevents splitting on '|' inside '{...}' (multi-type constraint, e.g. {a:guid|int}).
            List<string> rawSegments = new();
            List<char> seps = new();
            int start = 0;
            int brace = 0;
            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                if (c == '{') brace++;
                else if (c == '}') { if (brace > 0) brace--; }
                else if (brace == 0 && (c == PipeSeparator || c == SlashSeparator))
                {
                    rawSegments.Add(template[start..i]);
                    seps.Add(c);
                    start = i + 1;
                }
            }
            rawSegments.Add(template[start..]);

            var segments = new RouteSegment[rawSegments.Count];
            var parameters = new List<RouteParameter>();

            for (int i = 0; i < rawSegments.Count; i++)
            {
                string s = rawSegments[i];
                if (s.Length >= 2 && s[0] == '{' && s[^1] == '}')
                {
                    string inside = s[1..^1];
                    int colon = inside.IndexOf(':');
                    string name = colon < 0 ? inside : inside[..colon];
                    string? constraint = colon < 0 ? null : inside[(colon + 1)..];
                    if (string.IsNullOrWhiteSpace(name))
                        throw new ArgumentException(
                            $"Route template '{template}' has an empty placeholder at segment {i}.", nameof(template));
                    var p = new RouteParameter(name, constraint, i);
                    parameters.Add(p);
                    segments[i] = RouteSegment.OfParameter(p);
                }
                else
                {
                    if (s.Contains('{') || s.Contains('}'))
                        throw new ArgumentException(
                            $"Route template '{template}' segment '{s}' contains a misplaced '{{' or '}}'.",
                            nameof(template));
                    segments[i] = RouteSegment.OfLiteral(s);
                }
            }

            if (!segments[0].IsLiteral)
                throw new ArgumentException(
                    $"Route template '{template}' must start with a literal segment to use as the prefix.",
                    nameof(template));

            ValidateWorstCaseBytes(template, segments);
            return new RouteTemplate(template, segments, seps.ToArray(), parameters.ToArray());
        }

        /// <summary>
        /// Compute the worst-case UTF-8 byte count of callback data rendered from the template
        /// (literal bytes + 1 byte separator between each segment + max bytes per placeholder by CLR type).
        /// Throws early when it exceeds <see cref="MaxCallbackDataBytes"/>. The check is skipped when a placeholder
        /// type has no upper bound (string, or a method param resolved after dispatch).
        /// </summary>
        static void ValidateWorstCaseBytes(string raw, RouteSegment[] segments)
        {
            int total = segments.Length - 1; // 1-byte separator between each pair of segments
            for (int i = 0; i < segments.Length; i++)
            {
                RouteSegment seg = segments[i];
                if (seg.IsLiteral)
                {
                    total += System.Text.Encoding.UTF8.GetByteCount(seg.Literal!);
                }
                else
                {
                    int maxForParam = -1;
                    foreach (Type t in seg.Parameter!.ClrTypes)
                    {
                        int b = MaxBytesForClrType(t);
                        if (b < 0) return; // unknown upper bound — skip parse-time check
                        if (b > maxForParam) maxForParam = b;
                    }
                    total += maxForParam;
                }
            }
            if (total > MaxCallbackDataBytes)
                throw new InvalidOperationException(
                    $"Route template '{raw}' worst-case callback data {total} bytes exceeds the {MaxCallbackDataBytes}-byte Telegram callback_data limit. Shorten the template.");
        }

        static int MaxBytesForClrType(Type t)
        {
            if (t == typeof(int)) return 11;   // -2147483648
            if (t == typeof(long)) return 20;  // -9223372036854775808
            if (t == typeof(bool)) return 5;   // "false"
            if (t == typeof(Guid)) return 36;  // D format
            return -1;                          // string + unknown
        }

        /// <summary>Match callback data against the template. On match → returns dict {name → raw string}.</summary>
        public bool TryMatch(string callbackData, out IReadOnlyDictionary<string, string> values)
        {
            values = EmptyDict;
            if (callbackData is null) return false;

            string[] parts = callbackData.Split(Separators);
            if (parts.Length != _segments.Length) return false;

            Dictionary<string, string>? captures = null;
            for (int i = 0; i < _segments.Length; i++)
            {
                RouteSegment seg = _segments[i];
                if (seg.IsLiteral)
                {
                    if (!string.Equals(seg.Literal, parts[i], StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                else
                {
                    // Match if any type in ClrTypes parses. Multi-type may come from a constraint
                    // (e.g. {a:guid|int}) or be narrowed by binding to the method param type.
                    bool parsed = false;
                    foreach (Type t in seg.Parameter!.ClrTypes)
                    {
                        if (RouteParameterConverter.TryParse(parts[i], t, out _)) { parsed = true; break; }
                    }
                    if (!parsed) return false;
                    captures ??= new(StringComparer.Ordinal);
                    captures[seg.Parameter.Name] = parts[i];
                }
            }

            values = (IReadOnlyDictionary<string, string>?)captures ?? EmptyDict;
            return true;
        }

        /// <summary>Render callback data from the template and placeholder values. Preserves the original separator chars.</summary>
        public string Format(IReadOnlyDictionary<string, object?> values)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _segments.Length; i++)
            {
                if (i > 0) sb.Append(_separatorsBetween[i - 1]);
                RouteSegment seg = _segments[i];
                if (seg.IsLiteral)
                {
                    sb.Append(seg.Literal);
                }
                else
                {
                    string name = seg.Parameter!.Name;
                    if (!values.TryGetValue(name, out object? v))
                        throw new ArgumentException(
                            $"Route template '{_raw}': missing value for placeholder '{name}'.",
                            nameof(values));
                    string formatted = RouteParameterConverter.Format(v);
                    if (formatted.IndexOfAny(Separators) >= 0)
                        throw new ArgumentException(
                            $"Route template '{_raw}': value for placeholder '{name}' contains a separator character '/' or '|'.",
                            nameof(values));
                    sb.Append(formatted);
                }
            }

            string data = sb.ToString();
            int bytes = System.Text.Encoding.UTF8.GetByteCount(data);
            if (bytes > MaxCallbackDataBytes)
                throw new InvalidOperationException(
                    $"Callback data '{data}' exceeds {MaxCallbackDataBytes} bytes ({bytes} bytes). " +
                    $"Template '{_raw}' needs to be shorter.");
            return data;
        }

        public override string ToString() => _raw;

        static readonly IReadOnlyDictionary<string, string> EmptyDict =
            new Dictionary<string, string>(0);

        readonly struct RouteSegment
        {
            public bool IsLiteral { get; }
            public string? Literal { get; }
            public RouteParameter? Parameter { get; }

            RouteSegment(bool isLiteral, string? literal, RouteParameter? parameter)
            {
                IsLiteral = isLiteral;
                Literal = literal;
                Parameter = parameter;
            }

            public static RouteSegment OfLiteral(string value) => new(true, value, null);
            public static RouteSegment OfParameter(RouteParameter p) => new(false, null, p);
        }
    }
}
