namespace TqkLibrary.Telegram.BotKit.Attributes
{
    /// <summary>
    /// Applied to a <see cref="Handlers.CallbackModule"/> class to provide a shared callback_data prefix
    /// for every <see cref="InlineButtonAttribute"/> declared inside it. Prefix + <c>/</c> + inline template → full callback.
    /// Example: <c>[CallbackPrefix("cs")] + [InlineButton("{id:guid}|apv")]</c> ⇒ callback <c>cs/{id:guid}|apv</c>.
    /// </summary>
    /// <remarks>
    /// Only valid on <see cref="Handlers.CallbackModule"/>; not allowed on
    /// <see cref="Handlers.CommandModule"/>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CallbackPrefixAttribute : Attribute
    {
        public CallbackPrefixAttribute(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException("Callback prefix must not be empty.", nameof(prefix));
            if (prefix.Contains('/') || prefix.Contains('|'))
                throw new ArgumentException(
                    $"Callback prefix '{prefix}' must not contain the separator characters '/' or '|'.",
                    nameof(prefix));
            Prefix = prefix;
        }

        public string Prefix { get; }
    }
}
