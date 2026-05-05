using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;

namespace TqkLibrary.Telegram.BotKit.Extensions
{
    /// <summary>
    /// Resolves text from a (resourceType + resourceName) pair or a literal fallback.
    /// Used for <see cref="InlineButtonAttribute"/> titles and <see cref="TelegramCommandAttribute"/> descriptions.
    /// Priority: attribute resourceType → fallbackLocalizer → literal → null.
    /// </summary>
    public static class LocalizedTextResolver
    {
        static readonly ConcurrentDictionary<Type, ResourceManager?> _cache = new();

        /// <summary>
        /// Resolve text. <paramref name="resourceType"/> must expose <c>public static ResourceManager ResourceManager</c>
        /// (matching the resx auto-generated class shape). When <paramref name="culture"/> is null, <see cref="CultureInfo.CurrentUICulture"/> is used.
        ///
        /// <para><paramref name="fallbackLocalizer"/> is used when <paramref name="resourceType"/> is null but
        /// <paramref name="resourceName"/> is not empty — allowing apps to register only an <see cref="IStringLocalizer"/>
        /// in DI without declaring a resource type in the attribute. Note:
        /// <see cref="IStringLocalizer"/> always reads <see cref="CultureInfo.CurrentUICulture"/> internally, so
        /// the <paramref name="culture"/> argument is not honored on this fallback branch.</para>
        /// </summary>
        public static string? Resolve(
            Type? resourceType,
            string? resourceName,
            string? literalFallback,
            CultureInfo? culture = null,
            IStringLocalizer? fallbackLocalizer = null)
        {
            if (string.IsNullOrEmpty(resourceName))
                return literalFallback;

            if (resourceType is not null)
            {
                ResourceManager? rm = GetResourceManager(resourceType);
                if (rm is null)
                    throw new InvalidOperationException(
                        $"{resourceType.FullName} does not expose 'public static ResourceManager ResourceManager'. " +
                        $"Ensure you pass a resx auto-generated type.");
                string? text = rm.GetString(resourceName, culture ?? CultureInfo.CurrentUICulture);
                if (text is not null) return text;
                // Resource missing: prefer the literal fallback if the caller provided one rather
                // than crashing the entire keyboard render. Only throw when there is genuinely
                // nothing to show, so a partial localization (e.g. new key not yet translated)
                // degrades to the developer-supplied literal instead of taking the bot down.
                if (!string.IsNullOrEmpty(literalFallback)) return literalFallback;
                throw new InvalidOperationException(
                    $"Resource '{resourceName}' does not exist in {resourceType.FullName} " +
                    $"and no literal fallback was provided.");
            }

            if (fallbackLocalizer is not null)
            {
                // IStringLocalizer reads CultureInfo.CurrentUICulture internally — when caller
                // passed an explicit culture (e.g. SetMyCommands publishing per-language menus
                // up-front, before any update set CurrentUICulture), temporarily flip it via the
                // scope below so the fallback honors the requested culture. The scope is a no-op
                // when the requested culture already matches CurrentUICulture, avoiding a needless
                // assign/restore on the dispatch hot path.
                using (new UICultureScope(culture))
                {
                    LocalizedString s = fallbackLocalizer[resourceName];
                    if (!s.ResourceNotFound) return s.Value;
                }
            }

            return literalFallback;
        }

        /// <summary>
        /// Sets <see cref="CultureInfo.CurrentUICulture"/> to <paramref name="newCulture"/> on
        /// construction and restores the previous value on <see cref="Dispose"/>. No-ops when
        /// <paramref name="newCulture"/> is null or already matches CurrentUICulture, so callers
        /// can wrap unconditionally without paying for redundant assigns. The scope is sync-only
        /// (no <c>await</c> between Enter and Dispose) so the flip cannot leak across async
        /// boundaries via <see cref="System.Threading.ExecutionContext"/>.
        /// </summary>
        readonly struct UICultureScope : IDisposable
        {
            readonly CultureInfo? _previous;

            public UICultureScope(CultureInfo? newCulture)
            {
                if (newCulture is not null && !newCulture.Equals(CultureInfo.CurrentUICulture))
                {
                    _previous = CultureInfo.CurrentUICulture;
                    CultureInfo.CurrentUICulture = newCulture;
                }
                else
                {
                    _previous = null;
                }
            }

            public void Dispose()
            {
                if (_previous is not null) CultureInfo.CurrentUICulture = _previous;
            }
        }

        static ResourceManager? GetResourceManager(Type resourceType)
        {
            return _cache.GetOrAdd(resourceType, static t =>
            {
                PropertyInfo? prop = t.GetProperty(
                    "ResourceManager",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (prop is not null && typeof(ResourceManager).IsAssignableFrom(prop.PropertyType))
                    return (ResourceManager?)prop.GetValue(null);
                return null;
            });
        }
    }
}
