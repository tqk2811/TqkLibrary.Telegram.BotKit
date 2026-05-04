namespace TqkLibrary.Telegram.BotKit.Binding
{
    /// <summary>Data source for one parameter of an action method.</summary>
    public enum ParameterBindingKind
    {
        /// <summary>Bound from a route placeholder by name.</summary>
        RouteToken,
        /// <summary>Bound from the context (Update/Message/CallbackQuery/CancellationToken/...).</summary>
        Context,
        /// <summary>Bound from <see cref="UpdateContext.CommandArgs"/> — the text after the /command name (marked with [CommandArg]).</summary>
        CommandArg,
    }

    public enum ContextValue
    {
        Update,
        Message,
        CallbackQuery,
        UpdateType,
        CancellationToken,
        ModuleContext,
    }

    public sealed class ParameterBinding
    {
        public required ParameterBindingKind Kind { get; init; }
        public required Type ParameterType { get; init; }
        public required string ParameterName { get; init; }

        // RouteToken
        public string? RouteName { get; init; }

        /// <summary>
        /// Candidate types that can be parsed for this route token. The factory derives them from the
        /// constraint + method param type: concrete param → <c>[param type]</c>; param <c>object</c> + multi-constraint
        /// → all declared types (in the original order — the invoker tries each in turn). Non-null when
        /// <see cref="ParameterBindingKind.RouteToken"/>.
        /// </summary>
        public Type[]? CandidateTypes { get; init; }

        // Context
        public ContextValue? ContextValue { get; init; }
    }
}
