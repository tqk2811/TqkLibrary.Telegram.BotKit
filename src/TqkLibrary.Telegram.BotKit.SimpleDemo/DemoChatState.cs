using TqkLibrary.Telegram.BotKit.Models;

namespace TqkLibrary.Telegram.BotKit.SimpleDemo
{
    /// <summary>
    /// Single source of truth for per-chat state in this demo. Inheriting
    /// <see cref="BotKitChatStateBase"/> exposes <c>PendingInputKey</c> + <c>Language</c> to the
    /// framework automatically — no <c>opts.Map…</c> lambdas needed in
    /// <see cref="BotKitChatStateExtensions.AddBotKitChatState{T}(IServiceCollection, System.Action{BotKitChatStateOptions{T}}?)"/>.
    /// Application-only fields (echo counters, prompt msg id) live alongside.
    /// </summary>
    public class DemoChatState : BotKitChatStateBase
    {
        public int EchoCount { get; set; }
        public string? LastEchoText { get; set; }
        public int? EchoPromptMessageId { get; set; }
    }
}
