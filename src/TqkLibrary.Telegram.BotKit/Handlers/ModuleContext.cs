namespace TqkLibrary.Telegram.BotKit.Handlers
{
    /// <summary>
    /// Per-update context assigned to a module immediately after
    /// <c>ActivatorUtilities.CreateInstance</c>.
    /// </summary>
    public sealed record ModuleContext
    {
        public required IServiceProvider ServiceProvider { get; init; }
        public required ITelegramBotClient Bot { get; init; }
        public required string BotToken { get; init; }
        public required long BotId { get; init; }
        public required long ChatId { get; init; }
        public required long TelegramUserId { get; init; }
        public required ILogger Logger { get; init; }
    }
}
