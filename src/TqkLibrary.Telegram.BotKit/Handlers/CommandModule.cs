namespace TqkLibrary.Telegram.BotKit.Handlers
{
    /// <summary>
    /// Base class for <c>/command</c> handlers. Actions are marked with
    /// <see cref="Attributes.TelegramCommandAttribute"/>.
    /// Do not use <see cref="Attributes.CallbackPrefixAttribute"/> — commands are top-level and have no route prefix.
    /// </summary>
    public abstract class CommandModule : BaseTelegramHandler
    {
    }
}
