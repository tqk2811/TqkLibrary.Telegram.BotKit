namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Resolves a webhook path (the trailing URL segment, computed by
    /// <see cref="IBotWebhookPathResolver"/> from the bot token) back to the bot token.
    /// Used by <see cref="TelegramBotHostCollection"/> to auto-start a bot when a webhook
    /// arrives for a bot that is not yet running.
    ///
    /// Typical implementation: persist <c>(webhookPath, botToken)</c> rows when bots are
    /// provisioned (precompute the path with the same resolver) and look up by path here.
    /// </summary>
    public interface IBotTokenResolver
    {
        Task<string?> GetBotTokenAsync(string webhookPath, CancellationToken cancellationToken = default);
    }
}
