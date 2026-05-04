using System.Globalization;

namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Resolves the <see cref="CultureInfo"/> used to localize text (button titles, command
    /// descriptions) for the current update. Register an implementation in DI to override
    /// the default behavior, which reads <see cref="ILanguageStateAccessor.Language"/>.
    /// </summary>
    public interface ICultureProvider
    {
        /// <summary>
        /// Returns the culture for this update, or null to fall back to <see cref="CultureInfo.CurrentUICulture"/>.
        /// Implementations are scoped per-update — they may inject <see cref="ILanguageStateAccessor"/>
        /// or any other scoped service via DI to read the current chat's preference.
        /// </summary>
        CultureInfo? GetCulture();
    }
}
