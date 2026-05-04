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
                throw new InvalidOperationException(
                    $"Resource '{resourceName}' does not exist in {resourceType.FullName}.");
            }

            if (fallbackLocalizer is not null)
            {
                // IStringLocalizer reads CultureInfo.CurrentUICulture internally — when caller
                // passed an explicit culture (e.g. SetMyCommands publishing per-language menus
                // up-front, before any update set CurrentUICulture), temporarily flip it so the
                // fallback honors the requested culture, then restore.
                CultureInfo? toFlip = culture is not null && !culture.Equals(CultureInfo.CurrentUICulture)
                    ? culture : null;
                CultureInfo? previous = toFlip is not null ? CultureInfo.CurrentUICulture : null;
                if (toFlip is not null) CultureInfo.CurrentUICulture = toFlip;
                try
                {
                    LocalizedString s = fallbackLocalizer[resourceName];
                    if (!s.ResourceNotFound) return s.Value;
                }
                finally
                {
                    if (previous is not null) CultureInfo.CurrentUICulture = previous;
                }
            }

            return literalFallback;
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
