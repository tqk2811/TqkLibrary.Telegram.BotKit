namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Per-registration options for <see cref="BotKitChatStateExtensions.AddBotKitChatState{T}(IServiceCollection, System.Action{BotKitChatStateOptions{T}}?)"/>.
    /// Caller wires <see cref="MapPendingInputKey"/> and <see cref="MapLanguage"/> to expose the
    /// two read-only ports the framework needs ([OnUserInput] routing key, language tag) onto
    /// whatever property names the user picks in <typeparamref name="T"/>. Writing to those
    /// properties is up to the user's own module code; the framework only reads.
    ///
    /// When a map is omitted the dispatcher treats the corresponding feature as unavailable —
    /// no exception, no [OnUserInput] dispatch, no automatic culture switch.
    /// </summary>
    public sealed class BotKitChatStateOptions<T> where T : class
    {
        /// <summary>
        /// Prefix prepended to the cache key. Final key is
        /// <c>{CacheKeyPrefix}{T.FullName}:{botId}:{chatId}</c>. Customize when several apps
        /// share an <see cref="IMemoryCache"/> (or a distributed cache decorator) and need
        /// per-app namespacing. Default <c>"botkit:chat-state:"</c>.
        /// </summary>
        public string CacheKeyPrefix { get; set; } = "botkit:chat-state:";

        /// <summary>
        /// Sliding TTL for the cache entry — each access by the dispatcher extends the lifetime.
        /// Set to null to disable sliding (only <see cref="AbsoluteExpirationRelativeToNow"/> applies).
        /// Default 24 hours.
        /// </summary>
        public TimeSpan? SlidingExpiration { get; set; } = TimeSpan.FromHours(24);

        /// <summary>
        /// Optional absolute TTL relative to first insertion. The entry expires at the earlier
        /// of <see cref="SlidingExpiration"/> and this value. Null disables absolute expiry.
        /// </summary>
        public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }

        /// <summary>
        /// Optional override for the cache key builder. Receives (typeof(T), IUpdateContext) and
        /// returns the full cache key. When set, <see cref="CacheKeyPrefix"/> is ignored —
        /// the caller owns the entire key.
        /// </summary>
        public Func<Type, IUpdateContext, string>? CacheKeyBuilder { get; set; }

        internal Func<T, string?>? PendingInputKeyGetter { get; private set; }
        internal Func<T, string?>? LanguageGetter { get; private set; }

        /// <summary>
        /// Wire the [OnUserInput] routing key onto a getter of <typeparamref name="T"/>.
        /// Without this call, <c>[OnUserInput]</c> handlers will never fire because the dispatcher
        /// has no port to read the pending key from. User modules write the key directly on the
        /// state class (e.g. <c>state.PendingInputKey = "..."</c>) — no setter is needed here.
        /// </summary>
        public BotKitChatStateOptions<T> MapPendingInputKey(Func<T, string?> getter)
        {
            if (getter is null) throw new ArgumentNullException(nameof(getter));
            PendingInputKeyGetter = getter;
            return this;
        }

        /// <summary>
        /// Wire the user's language tag onto a getter of <typeparamref name="T"/>.
        /// Without this call, <see cref="DefaultCultureProvider"/> always returns null and the
        /// dispatcher leaves <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> untouched.
        /// User modules write the language directly on the state class (e.g.
        /// <c>state.Language = "vi"</c>) — no setter is needed here.
        /// </summary>
        public BotKitChatStateOptions<T> MapLanguage(Func<T, string?> getter)
        {
            if (getter is null) throw new ArgumentNullException(nameof(getter));
            LanguageGetter = getter;
            return this;
        }
    }
}
