using System.Globalization;

namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Default <see cref="ICultureProvider"/>: reads <see cref="ILanguageStateAccessor.Language"/>
    /// (registered when the user wires <c>opts.MapLanguage(...)</c>) and parses it into a
    /// <see cref="CultureInfo"/>. Returns null when no accessor is registered, or when the
    /// language tag is null/empty/unparseable so the caller can fall back.
    /// </summary>
    public sealed class DefaultCultureProvider : ICultureProvider
    {
        readonly ILanguageStateAccessor? _accessor;

        public DefaultCultureProvider(ILanguageStateAccessor? accessor = null)
        {
            _accessor = accessor;
        }

        public CultureInfo? GetCulture()
        {
            string? lang = _accessor?.Language;
            if (string.IsNullOrWhiteSpace(lang)) return null;
            try { return CultureInfo.GetCultureInfo(lang); }
            catch (CultureNotFoundException) { return null; }
        }
    }
}
