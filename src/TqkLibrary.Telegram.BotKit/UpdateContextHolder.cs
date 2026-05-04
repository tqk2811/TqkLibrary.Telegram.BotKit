namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Mutable backing for <see cref="IUpdateContext"/>. <see cref="BotUpdateDispatcher"/>
    /// resolves the holder for each scope it creates and fills the values before any other
    /// scoped service is resolved. Internal so consumers always see the read-only view.
    /// </summary>
    internal sealed class UpdateContextHolder : IUpdateContext
    {
        public long BotId { get; internal set; }
        public long ChatId { get; internal set; }
        public long TelegramUserId { get; internal set; }
        public string BotToken { get; internal set; } = "";
        public ITelegramBotClient Bot { get; internal set; } = null!;
    }
}
