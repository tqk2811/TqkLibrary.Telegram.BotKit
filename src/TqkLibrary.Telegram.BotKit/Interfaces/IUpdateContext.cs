namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Per-update ambient context published by <see cref="BotUpdateDispatcher"/> as a scoped
    /// service. Inject into any service registered against the per-update scope (e.g. a user
    /// chat-state factory) to find out which bot/chat/user this update belongs to.
    /// </summary>
    public interface IUpdateContext
    {
        /// <summary>Telegram UserId of the bot itself.</summary>
        long BotId { get; }

        /// <summary>Telegram ChatId the update originated from.</summary>
        long ChatId { get; }

        /// <summary>Telegram UserId of the human (or bot) that produced the update.</summary>
        long TelegramUserId { get; }

        /// <summary>Bot HTTP token. Useful for opening a fresh <see cref="TelegramBotClient"/> inside a service.</summary>
        string BotToken { get; }

        /// <summary>The shared <see cref="ITelegramBotClient"/> for this bot.</summary>
        ITelegramBotClient Bot { get; }
    }
}
