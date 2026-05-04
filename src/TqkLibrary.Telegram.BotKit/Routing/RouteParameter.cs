namespace TqkLibrary.Telegram.BotKit.Routing
{
    /// <summary>
    /// A single placeholder <c>{name}</c>, <c>{name:type}</c>, or <c>{name:type1|type2}</c>
    /// in a route template.
    /// </summary>
    public sealed class RouteParameter
    {
        Type[] _clrTypes;

        public RouteParameter(string name, string? constraint, int segmentIndex)
        {
            Name = name;
            Constraint = constraint;
            SegmentIndex = segmentIndex;
            _clrTypes = RouteParameterConverter.ResolveClrTypes(constraint);
        }

        public string Name { get; }

        /// <summary>Raw constraint name from the template (e.g. "guid", "int", "guid|int"). Null = default to string.</summary>
        public string? Constraint { get; }

        /// <summary>Segment index in the template (0-based, separated by <c>|</c>/<c>/</c>).</summary>
        public int SegmentIndex { get; }

        /// <summary>First (primary) type — kept for backwards compatibility.</summary>
        public Type ClrType => _clrTypes[0];

        /// <summary>
        /// All types accepted at match time. At Parse time = inferred from the constraint; after
        /// <see cref="Binding.ParameterBindingFactory"/> binds, this list may be narrowed to the
        /// type of the corresponding method param (see <see cref="ApplyEffectiveTypes"/>).
        /// </summary>
        public IReadOnlyList<Type> ClrTypes => _clrTypes;

        /// <summary>
        /// Called by registry/binding after method match → updates the effective type list so that
        /// <see cref="RouteTemplate.TryMatch"/> matches strictly against the method param type
        /// (e.g. <c>{a}</c> + <c>Guid a</c> → effective <c>[Guid]</c>).
        /// </summary>
        internal void ApplyEffectiveTypes(Type[] types)
        {
            if (types is null || types.Length == 0)
                throw new ArgumentException("Effective types must not be empty.", nameof(types));
            _clrTypes = types;
        }
    }
}
