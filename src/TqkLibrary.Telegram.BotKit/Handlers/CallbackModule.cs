namespace TqkLibrary.Telegram.BotKit.Handlers
{
    /// <summary>
    /// Base class for callback handlers. Actions are marked with
    /// <see cref="Attributes.InlineButtonAttribute"/>,
    /// <see cref="Attributes.OnUserInputAttribute"/>, or
    /// <see cref="Attributes.TelegramRegexAttribute"/>.
    /// You may apply <see cref="Attributes.CallbackPrefixAttribute"/> on the class to prefix callback data
    /// (e.g. <c>[CallbackPrefix("cs")]</c> → <c>cs/…</c>).
    /// </summary>
    public abstract class CallbackModule : BaseTelegramHandler
    {
    }
}
