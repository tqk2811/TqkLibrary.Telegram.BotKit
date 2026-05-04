namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Computes a stable identifier from a bot token, used as the webhook URL path
    /// segment AND the Telegram <c>secret_token</c>. The result must contain only
    /// characters allowed by Telegram's secret_token (<c>A-Z a-z 0-9 _ -</c>) and be
    /// 1-256 chars long.
    ///
    /// Override the default registration to customize the derivation (e.g. shorten the
    /// hash, switch to HMAC, prefix with an environment tag).
    /// </summary>
    public interface IBotWebhookPathResolver
    {
        string ResolvePath(string botToken);
    }
}
