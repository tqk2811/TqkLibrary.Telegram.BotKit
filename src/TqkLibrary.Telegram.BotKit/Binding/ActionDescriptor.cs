using TqkLibrary.Telegram.BotKit.Routing;

namespace TqkLibrary.Telegram.BotKit.Binding
{
    public enum ActionKind
    {
        Command,
        InlineButton,
        UserInput,
        Regex,
    }

    /// <summary>
    /// Metadata for a single action. One method can produce multiple descriptors (e.g. a method
    /// with two <see cref="Attributes.TelegramCommandAttribute"/> instances).
    /// </summary>
    public sealed class ActionDescriptor
    {
        public required ActionKind Kind { get; init; }
        public required Type ModuleType { get; init; }
        public required MethodInfo Method { get; init; }
        public required ParameterBinding[] Parameters { get; init; }

        // Command
        public string? CommandName { get; init; }
        public int CommandOrder { get; init; }
        public string? CommandDescription { get; init; }
        public Type? CommandDescriptionResourceType { get; init; }
        public string? CommandDescriptionResourceName { get; init; }

        // InlineButton
        public RouteTemplate? RouteTemplate { get; init; }
        public string? InlineTitle { get; init; }
        public Type? InlineTitleResourceType { get; init; }
        public string? InlineTitleResourceName { get; init; }

        // UserInput
        public string? UserInputKey { get; init; }

        // Regex
        public Regex? Regex { get; init; }
        public int RegexOrder { get; init; }
        public bool RegexStopOnMatch { get; init; }
    }
}
