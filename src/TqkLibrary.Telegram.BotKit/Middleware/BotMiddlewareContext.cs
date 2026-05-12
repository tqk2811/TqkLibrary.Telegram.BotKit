namespace TqkLibrary.Telegram.BotKit.Middleware
{
    /// <summary>
    /// Per-update payload visible to every middleware. Built once after the per-chat lock,
    /// scope creation, chat-state bootstrap and culture apply, then handed to the pipeline.
    /// Middleware can read every field, short-circuit by NOT calling <c>next(ctx)</c>,
    /// or wrap <c>next(ctx)</c> in a try/catch for centralised error handling.
    ///
    /// Per-action data (RouteValues, CommandArgs) is NOT here — it only exists after the
    /// terminal stage matched a route. Middleware that needs it would have to inspect the
    /// resolved descriptor itself, which is a deliberate non-goal of pre-dispatch middleware.
    /// </summary>
    public sealed class BotMiddlewareContext
    {
        /// <summary>Scoped service provider for this update — resolve any scoped service here.</summary>
        public required IServiceProvider Services { get; init; }

        /// <summary>Bot client tied to the dispatching <see cref="TelegramBotHost"/>.</summary>
        public required ITelegramBotClient Bot { get; init; }

        public required long BotId { get; init; }
        public required string BotToken { get; init; }

        /// <summary>The raw Telegram update.</summary>
        public required Update Update { get; init; }

        /// <summary>Convenience copy of <see cref="Update.Type"/>.</summary>
        public required UpdateType UpdateType { get; init; }

        /// <summary>Set when <see cref="UpdateType"/> is <see cref="UpdateType.Message"/>.</summary>
        public Message? Message { get; init; }

        /// <summary>Set when <see cref="UpdateType"/> is <see cref="UpdateType.CallbackQuery"/>.</summary>
        public CallbackQuery? CallbackQuery { get; init; }

        /// <summary>Resolved chat id (falls back to user id for inline-mode callbacks).</summary>
        public required long ChatId { get; init; }

        /// <summary>Telegram user id of the sender (Message.From or CallbackQuery.From).</summary>
        public required long TelegramUserId { get; init; }

        /// <summary>Username of the sender if available (null when the user has none).</summary>
        public string? TelegramUsername { get; init; }

        /// <summary>Logger categorised under <see cref="BotUpdateDispatcher"/>.</summary>
        public required ILogger Logger { get; init; }

        public required CancellationToken CancellationToken { get; init; }
    }
}
