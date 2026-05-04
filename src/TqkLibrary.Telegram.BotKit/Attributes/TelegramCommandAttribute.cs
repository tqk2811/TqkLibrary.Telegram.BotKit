namespace TqkLibrary.Telegram.BotKit.Attributes
{
    /// <summary>
    /// Marks a method on <see cref="Handlers.CallbackModule"/> as the handler for the <c>/{name}</c> command.
    /// Multiple attributes per method are allowed (aliases).
    /// </summary>
    /// <remarks>
    /// The bot menu description can be supplied two ways:
    ///   1. <see cref="Description"/> literal — fixed, not localized.
    ///   2. <see cref="DescriptionResourceType"/> + <see cref="DescriptionResourceName"/> — resolved via
    ///      the <see cref="System.Resources.ResourceManager"/> static property of the resx class.
    /// When the resource pair is set, the literal is ignored. If both are null, the command is hidden
    /// from the menu (but is still routable as /command).
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class TelegramCommandAttribute : Attribute
    {
        public TelegramCommandAttribute(string name, int order = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Command name must not be empty.", nameof(name));
            if (name.Contains(' ') || name.StartsWith("/"))
                throw new ArgumentException(
                    $"Command name '{name}' must not contain whitespace or start with '/'.",
                    nameof(name));

            Name = name;
            Order = order;
        }

        /// <summary>Command name, without the leading '/'.</summary>
        public string Name { get; }

        /// <summary>Display order in the bot menu (SetMyCommands). Smaller values come first.</summary>
        public int Order { get; }

        /// <summary>Literal description. Empty/null with no resource ⇒ hidden from the menu.</summary>
        public string Description { get; set; } = "";

        /// <summary>Resx class exposing <c>public static ResourceManager ResourceManager</c>.</summary>
        public Type? DescriptionResourceType { get; set; }

        /// <summary>Resx key used to resolve the description for the current culture.</summary>
        public string? DescriptionResourceName { get; set; }
    }
}
