using System.ComponentModel;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.Extensions.Localization;
using TqkLibrary.Telegram.BotKit.Extensions;

namespace TqkLibrary.Telegram.BotKit.Handlers
{
    /// <summary>
    /// Base class shared by <see cref="CallbackModule"/> (callback + text/regex/user-input)
    /// and <see cref="CommandModule"/> (<c>/command</c>). Exposes helper properties for the
    /// <see cref="ModuleContext"/> of a single dispatch.
    /// Handlers are created via <c>ActivatorUtilities.CreateInstance</c> per update (scoped DI).
    /// </summary>
    public abstract class BaseTelegramHandler
    {
        ModuleContext? _context;
        ModuleActionRegistry? _registry;

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public ModuleContext ModuleContext
        {
            get => _context ?? throw new InvalidOperationException(
                $"{nameof(ModuleContext)} has not been assigned. Handlers must be instantiated via BotUpdateDispatcher.");
            set => _context = value;
        }

        protected IServiceProvider ServiceProvider => ModuleContext.ServiceProvider;
        protected ITelegramBotClient Bot => ModuleContext.Bot;
        protected string BotToken => ModuleContext.BotToken;
        protected long BotId => ModuleContext.BotId;
        protected long ChatId => ModuleContext.ChatId;
        protected long TelegramUserId => ModuleContext.TelegramUserId;
        protected ILogger Logger => ModuleContext.Logger;

        /// <summary>
        /// Shared <see cref="ModuleActionRegistry"/> singleton used to render type-safe inline
        /// buttons (e.g. <c>Registry.ToInlineButton&lt;TModule&gt;(c =&gt; c.Action(...))</c>).
        /// Cached after the first resolve so multi-button keyboards don't re-walk the DI
        /// container on each call (handler instances are scoped per-update — single-threaded).
        /// </summary>
        protected ModuleActionRegistry Registry =>
            _registry ??= ServiceProvider.GetRequiredService<ModuleActionRegistry>();

        /// <summary>
        /// Resolve the culture for the current update via <see cref="ICultureProvider"/>;
        /// falls back to <see cref="CultureInfo.CurrentUICulture"/> when no provider is registered
        /// or it returns null. Use for <c>ToInlineButton(culture: ResolveCulture())</c> or
        /// per-user resource lookup.
        /// </summary>
        protected CultureInfo ResolveCulture()
        {
            ICultureProvider? provider = ServiceProvider.GetService<ICultureProvider>();
            return provider?.GetCulture() ?? CultureInfo.CurrentUICulture;
        }

        /// <summary>
        /// Render an inline button targeting another module's action. Auto-pulls
        /// <see cref="IStringLocalizer"/> from the scoped <see cref="ServiceProvider"/> so attributes
        /// declaring only <c>TitleResourceName</c> (no <c>TitleResourceType</c>) resolve via the
        /// registered localizer without per-call wiring.
        /// </summary>
        protected InlineKeyboardButton ToInlineButton<TModule>(
            Expression<Func<TModule, Task>> expression,
            string? text = null,
            CultureInfo? culture = null)
            where TModule : CallbackModule
            => Registry.ToInlineButton(
                expression, text, culture,
                ServiceProvider.GetService<IStringLocalizer>());
    }
}
