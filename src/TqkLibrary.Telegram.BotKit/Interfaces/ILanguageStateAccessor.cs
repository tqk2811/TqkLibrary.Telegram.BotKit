namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Read-only port the default <see cref="ICultureProvider"/> uses to resolve the user's
    /// language tag (BCP-47, e.g. "vi") for the current chat. Implementations forward to a
    /// property of the user-defined chat-state class declared via <c>opts.MapLanguage(...)</c>.
    /// When the map is not wired the accessor is not registered, the default provider returns
    /// null, and the dispatcher leaves <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> untouched.
    ///
    /// Writes are NOT exposed here: user modules mutate the underlying state class directly
    /// (e.g. <c>_state.Language = "vi"</c>) — the framework never writes back.
    /// </summary>
    public interface ILanguageStateAccessor
    {
        string? Language { get; }
    }
}
