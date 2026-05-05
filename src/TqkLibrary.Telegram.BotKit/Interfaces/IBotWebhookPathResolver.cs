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

    /// <summary>
    /// Validation helpers for paths returned by <see cref="IBotWebhookPathResolver"/>.
    /// Telegram rejects a SetWebhook call whose <c>secret_token</c> falls outside the
    /// <c>[A-Za-z0-9_-]{1,256}</c> charset; the framework validates eagerly so the failure
    /// surfaces at start time with a clear message instead of as a Telegram API error mid-call.
    /// </summary>
    public static class BotWebhookPathValidator
    {
        public const int MaxLength = 256;

        public static void Validate(string path, string resolverName)
        {
            if (path is null)
                throw new InvalidOperationException(
                    $"IBotWebhookPathResolver '{resolverName}' returned null. " +
                    $"Webhook path must be 1-{MaxLength} chars in [A-Za-z0-9_-].");
            if (path.Length == 0 || path.Length > MaxLength)
                throw new InvalidOperationException(
                    $"IBotWebhookPathResolver '{resolverName}' returned a path of length {path.Length}. " +
                    $"Webhook path must be 1-{MaxLength} chars in [A-Za-z0-9_-]. Got: '{path}'.");
            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];
                bool ok = (c >= 'A' && c <= 'Z')
                       || (c >= 'a' && c <= 'z')
                       || (c >= '0' && c <= '9')
                       || c == '_' || c == '-';
                if (!ok)
                    throw new InvalidOperationException(
                        $"IBotWebhookPathResolver '{resolverName}' returned a path containing illegal char '{c}' " +
                        $"(at index {i}). Allowed chars: [A-Za-z0-9_-]. Got: '{path}'.");
            }
        }
    }
}
