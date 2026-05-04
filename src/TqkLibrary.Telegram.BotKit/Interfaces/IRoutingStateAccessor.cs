namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Read-only port the dispatcher uses to peek at the [OnUserInput] routing key for the
    /// current chat. Implementations forward to a property of the user-defined chat-state class
    /// declared via <c>opts.MapPendingInputKey(...)</c>. When the map is not wired the accessor
    /// is not registered and the dispatcher silently disables [OnUserInput] for this app.
    ///
    /// Writes are NOT exposed here: user modules mutate the underlying state class directly
    /// (e.g. <c>_state.PendingInputKey = "echo:wait_text"</c>) — the framework never writes back.
    /// </summary>
    public interface IRoutingStateAccessor
    {
        string? PendingInputKey { get; }
    }
}
