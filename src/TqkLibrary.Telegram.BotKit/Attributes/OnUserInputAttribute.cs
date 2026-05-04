namespace TqkLibrary.Telegram.BotKit.Attributes
{
    /// <summary>
    /// Marks a method as the handler that receives the next text message when
    /// <see cref="IRoutingStateAccessor.PendingInputKey"/> equals <see cref="Key"/>.
    /// User modules write the routing key directly on their chat-state class
    /// (e.g. <c>state.PendingInputKey = "echo:wait_text"</c>). Requires
    /// <c>AddBotKitChatState&lt;T&gt;(opts.MapPendingInputKey(...))</c> to register the
    /// read accessor — without it, [OnUserInput] handlers never fire.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class OnUserInputAttribute : Attribute
    {
        public OnUserInputAttribute(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("OnUserInput key must not be empty.", nameof(key));
            Key = key;
        }

        public string Key { get; }
    }
}
