namespace TqkLibrary.Telegram.BotKit.Attributes
{
    /// <summary>
    /// Marks a <see cref="string"/> parameter that receives the text following the command name —
    /// e.g. <c>/start abc123</c> → <c>"abc123"</c>; <c>/transfer 100 alice</c> → <c>"100 alice"</c>.
    /// Null when the command has no args. Useful for Telegram deep links (<c>https://t.me/bot?start=payload</c>).
    /// </summary>
    /// <remarks>
    /// Parameter type must be <see cref="string"/> or <c>string?</c>.
    /// Only applies to methods carrying <see cref="TelegramCommandAttribute"/>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class CommandArgAttribute : Attribute
    {
    }
}
