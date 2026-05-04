namespace TqkLibrary.Telegram.BotKit
{
    public static class BotKitChatStateExtensions
    {
        /// <summary>
        /// Register a user-defined per-chat state class as the single source of truth for
        /// (a) framework routing/i18n state and (b) application data. Each Telegram chat gets
        /// its own instance, persisted in <see cref="IMemoryCache"/> keyed by (botId, chatId).
        ///
        /// <para>Accessor wiring (read-only ports the framework uses):
        /// <list type="number">
        ///   <item>If <c>opts.MapPendingInputKey(...)</c> / <c>MapLanguage(...)</c> was called → use that lambda.</item>
        ///   <item>Else if <typeparamref name="T"/> inherits <see cref="BotKitChatStateBase"/> → read base property.</item>
        ///   <item>Else → accessor not registered, the corresponding feature ([OnUserInput] / auto culture) is silently disabled.</item>
        /// </list>
        /// </para>
        ///
        /// The framework never writes to the state — your module code does
        /// (e.g. <c>_state.PendingInputKey = "..."</c>).
        /// </summary>
        public static IServiceCollection AddBotKitChatState<T>(
            this IServiceCollection services,
            Action<BotKitChatStateOptions<T>>? configure = null)
            where T : class, new()
            => services.AddBotKitChatState(static () => new T(), configure);

        /// <summary>
        /// Sync factory overload — runs lazily on the first DI resolve inside the update scope.
        /// Use when constructing <typeparamref name="T"/> needs no async work (just <c>new()</c>
        /// or simple field initialization).
        /// </summary>
        public static IServiceCollection AddBotKitChatState<T>(
            this IServiceCollection services,
            Func<T> factory,
            Action<BotKitChatStateOptions<T>>? configure = null)
            where T : class
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));

            BotKitChatStateOptions<T> options = new();
            configure?.Invoke(options);

            services.AddScoped(sp =>
            {
                IUpdateContext ctx = sp.GetRequiredService<IUpdateContext>();
                IMemoryCache cache = sp.GetRequiredService<IMemoryCache>();
                string key = BuildCacheKey(typeof(T), ctx, options);
                return cache.GetOrCreate(key, entry =>
                {
                    ApplyExpiration(entry, options);
                    return factory();
                })!;
            });

            RegisterAccessors(services, options);
            return services;
        }

        /// <summary>
        /// Async factory overload — for cases where loading <typeparamref name="T"/> needs I/O
        /// (database query, remote service). The dispatcher pre-loads the state via
        /// <see cref="IChatStateBootstrapper"/> at the start of each update so subsequent sync DI
        /// resolves (handler ctor, accessors) hit the cache without sync-over-async.
        ///
        /// Cache hit → loader skipped. Cache miss → loader runs once per (botId, chatId)
        /// while the per-chat lock is held, result cached with the configured TTL.
        /// </summary>
        public static IServiceCollection AddBotKitChatState<T>(
            this IServiceCollection services,
            Func<IServiceProvider, CancellationToken, ValueTask<T>> asyncFactory,
            Action<BotKitChatStateOptions<T>>? configure = null)
            where T : class
        {
            if (asyncFactory is null) throw new ArgumentNullException(nameof(asyncFactory));

            BotKitChatStateOptions<T> options = new();
            configure?.Invoke(options);

            services.AddScoped(sp =>
            {
                IUpdateContext ctx = sp.GetRequiredService<IUpdateContext>();
                IMemoryCache cache = sp.GetRequiredService<IMemoryCache>();
                string key = BuildCacheKey(typeof(T), ctx, options);
                if (cache.TryGetValue(key, out T? value) && value is not null) return value;
                throw new InvalidOperationException(
                    $"Chat-state {typeof(T).FullName} for chat {ctx.ChatId} was not pre-loaded. " +
                    $"Did the dispatcher skip {nameof(IChatStateBootstrapper)} bootstrap, " +
                    $"or did the async factory return null?");
            });

            services.AddScoped<IChatStateBootstrapper>(sp =>
                new AsyncChatStateBootstrapper<T>(sp, options, asyncFactory));

            RegisterAccessors(services, options);
            return services;
        }

        static string BuildCacheKey<T>(Type stateType, IUpdateContext ctx, BotKitChatStateOptions<T> options)
            where T : class
            => options.CacheKeyBuilder is not null
                ? options.CacheKeyBuilder(stateType, ctx)
                : $"{options.CacheKeyPrefix}{stateType.FullName}:{ctx.BotId}:{ctx.ChatId}";

        static void ApplyExpiration<T>(ICacheEntry entry, BotKitChatStateOptions<T> options) where T : class
        {
            if (options.SlidingExpiration is not null)
                entry.SlidingExpiration = options.SlidingExpiration;
            if (options.AbsoluteExpirationRelativeToNow is not null)
                entry.AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow;
        }

        // Picks getter precedence: explicit Map lambda → BotKitChatStateBase property → none.
        static void RegisterAccessors<T>(IServiceCollection services, BotKitChatStateOptions<T> options)
            where T : class
        {
            Func<T, string?>? routingGet = options.PendingInputKeyGetter
                ?? (typeof(BotKitChatStateBase).IsAssignableFrom(typeof(T))
                    ? static s => ((BotKitChatStateBase)(object)s).PendingInputKey
                    : null);
            if (routingGet is not null)
                services.AddScoped<IRoutingStateAccessor>(sp =>
                    new RoutingAccessor<T>(sp.GetRequiredService<T>(), routingGet));

            Func<T, string?>? langGet = options.LanguageGetter
                ?? (typeof(BotKitChatStateBase).IsAssignableFrom(typeof(T))
                    ? static s => ((BotKitChatStateBase)(object)s).Language
                    : null);
            if (langGet is not null)
                services.AddScoped<ILanguageStateAccessor>(sp =>
                    new LanguageAccessor<T>(sp.GetRequiredService<T>(), langGet));
        }

        sealed class RoutingAccessor<TState> : IRoutingStateAccessor where TState : class
        {
            readonly TState _state;
            readonly Func<TState, string?> _get;
            public RoutingAccessor(TState state, Func<TState, string?> get) { _state = state; _get = get; }
            public string? PendingInputKey => _get(_state);
        }

        sealed class LanguageAccessor<TState> : ILanguageStateAccessor where TState : class
        {
            readonly TState _state;
            readonly Func<TState, string?> _get;
            public LanguageAccessor(TState state, Func<TState, string?> get) { _state = state; _get = get; }
            public string? Language => _get(_state);
        }

        sealed class AsyncChatStateBootstrapper<T> : IChatStateBootstrapper where T : class
        {
            readonly IServiceProvider _sp;
            readonly BotKitChatStateOptions<T> _options;
            readonly Func<IServiceProvider, CancellationToken, ValueTask<T>> _factory;

            public AsyncChatStateBootstrapper(
                IServiceProvider sp,
                BotKitChatStateOptions<T> options,
                Func<IServiceProvider, CancellationToken, ValueTask<T>> factory)
            {
                _sp = sp;
                _options = options;
                _factory = factory;
            }

            public async ValueTask EnsureLoadedAsync(CancellationToken cancellationToken)
            {
                IUpdateContext ctx = _sp.GetRequiredService<IUpdateContext>();
                IMemoryCache cache = _sp.GetRequiredService<IMemoryCache>();
                string key = BuildCacheKey(typeof(T), ctx, _options);
                if (cache.TryGetValue(key, out _)) return;

                T value = await _factory(_sp, cancellationToken);
                using ICacheEntry entry = cache.CreateEntry(key);
                ApplyExpiration(entry, _options);
                entry.Value = value;
            }
        }
    }
}
