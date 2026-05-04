namespace TqkLibrary.Telegram.BotKit.Models
{
    /// <summary>
    /// Optional base class for the user's per-chat state class. Inherit when you want the
    /// framework to read its two required ports (<see cref="PendingInputKey"/>, <see cref="Language"/>)
    /// without writing explicit <c>opts.Map…</c> lambdas in <see cref="BotKitChatStateExtensions.AddBotKitChatState{T}(IServiceCollection, System.Action{BotKitChatStateOptions{T}}?)"/>.
    ///
    /// Resolution order at registration:
    /// <list type="number">
    ///   <item>If <c>opts.MapPendingInputKey(...)</c> was called → use that lambda.</item>
    ///   <item>Else if <c>T</c> inherits <see cref="BotKitChatStateBase"/> → read <see cref="PendingInputKey"/>.</item>
    ///   <item>Else → accessor not registered, [OnUserInput] silently disabled.</item>
    /// </list>
    /// (Same precedence applies to <see cref="Language"/> via <c>MapLanguage</c>.)
    ///
    /// User code mutates these properties directly (e.g. <c>state.PendingInputKey = "..."</c>);
    /// the framework only reads them. Properties are virtual so subclasses can override for
    /// validation or change-notification.
    /// </summary>
    public abstract class BotKitChatStateBase
    {
        public virtual string? PendingInputKey { get; set; }
        public virtual string? Language { get; set; }
    }
}
