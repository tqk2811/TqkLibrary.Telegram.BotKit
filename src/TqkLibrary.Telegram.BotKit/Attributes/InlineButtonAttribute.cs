namespace TqkLibrary.Telegram.BotKit.Attributes
{
    /// <summary>
    /// Marks a method as the handler for a <see cref="Telegram.Bot.Types.CallbackQuery"/>.
    /// Template: <c>"prefix|{name}|literal|{name:type}"</c>. See <see cref="Routing.RouteTemplate"/>.
    /// Example: <c>[InlineButton("check_source|{id:guid}|apv", Title = "✅ Approve")]</c>.
    /// </summary>
    /// <remarks>
    /// Title can be supplied two ways (resource takes precedence when present):
    ///   1. <see cref="Title"/> literal — fixed, not localized.
    ///   2. <see cref="TitleResourceType"/> + <see cref="TitleResourceName"/> — resolved via
    ///      the <see cref="System.Resources.ResourceManager"/> static property of the resx class.
    ///      Equivalent to <c>[Display(Name = "...", ResourceType = typeof(...))]</c> in ASP.NET.
    /// A text passed at the call site of <c>ToInlineButton(...)</c> always overrides the attribute title.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class InlineButtonAttribute : Attribute
    {
        public InlineButtonAttribute(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
                throw new ArgumentException("Template must not be empty.", nameof(template));
            Template = template;
        }

        public string Template { get; }

        /// <summary>Literal text shown on the button. Null means the value must come from the call site or a resource.</summary>
        public string? Title { get; set; }

        /// <summary>Resx class exposing <c>public static ResourceManager ResourceManager</c>. Must be paired with <see cref="TitleResourceName"/>.</summary>
        public Type? TitleResourceType { get; set; }

        /// <summary>Resx key used to resolve the title for the current culture.</summary>
        public string? TitleResourceName { get; set; }
    }
}
