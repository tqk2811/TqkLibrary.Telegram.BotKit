using System.Globalization;
using TqkLibrary.Telegram.BotKit.Binding;
using TqkLibrary.Telegram.BotKit.Handlers;

namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Dispatcher: receives an update → finds the action → binds parameters → invokes the module.
    /// Each <see cref="TelegramBotHost"/> creates its own instance.
    /// </summary>
    public sealed class BotUpdateDispatcher
    {
        readonly TelegramBotClient _bot;
        readonly string _botToken;
        readonly long _botId;
        readonly IServiceProvider _serviceProvider;
        readonly ModuleActionRegistry _registry;
        readonly ILoggerFactory _loggerFactory;
        readonly ILogger<BotUpdateDispatcher> _logger;
        readonly TelegramBotKitOptions? _options;
        // Per-chat lock: serialize updates for the same chatId so user state mutations stay atomic.
        // Refcounted so the entry is removed (and SemaphoreSlim disposed) once the last waiter releases —
        // bounded memory regardless of how many distinct chats a long-running bot ever sees.
        readonly ConcurrentDictionary<long, ChatLockEntry> _chatLocks = new();

        internal BotUpdateDispatcher(
            TelegramBotClient bot,
            string botToken,
            long botId,
            IServiceProvider serviceProvider,
            ModuleActionRegistry registry,
            ILoggerFactory loggerFactory)
        {
            _bot = bot;
            _botToken = botToken;
            _botId = botId;
            _serviceProvider = serviceProvider;
            _registry = registry;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<BotUpdateDispatcher>();
            _options = serviceProvider.GetService<TelegramBotKitOptions>();
        }

        internal async Task HandleUpdateAsync(ITelegramBotClient _, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Type == UpdateType.Message && update.Message?.From is not null)
                    await OnMessageAsync(update, cancellationToken);
                else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery is not null)
                    await OnCallbackQueryAsync(update, cancellationToken);
                else
                    _logger.LogDebug("Bot {BotId}: unhandled update type {Type}", _botId, update.Type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bot {BotId}: unhandled exception in HandleUpdateAsync", _botId);
            }
        }

        internal Task HandleErrorAsync(ITelegramBotClient _, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Bot {BotId}: polling error [{Source}]", _botId, source);
            return Task.CompletedTask;
        }

        async Task OnMessageAsync(Update update, CancellationToken cancellationToken)
        {
            Message message = update.Message!;
            long chatId = message.Chat.Id;
            long telegramUserId = message.From!.Id;
            string? telegramUsername = message.From.Username;

            using IDisposable _ = await AcquireChatLockOrNoopAsync(chatId, cancellationToken);

            using IServiceScope scope = _serviceProvider.CreateScope();
            PopulateUpdateContext(scope, chatId, telegramUserId);
            await BootstrapChatStatesAsync(scope.ServiceProvider, cancellationToken);
            ApplyCulture(scope.ServiceProvider);
            ModuleContext ctx = CreateContext(scope, chatId, telegramUserId, loggerType: typeof(BotUpdateDispatcher));

            if (_options?.OnUserInteractionAsync is not null)
                await _options.OnUserInteractionAsync(scope.ServiceProvider, telegramUserId, telegramUsername, cancellationToken);

            string? text = message.Text?.Trim();
            if (text is { Length: > 0 } && text[0] == '/')
            {
                ParseCommand(text, out string commandName, out string? commandArgs);
                await DispatchCommandAsync(scope, ctx, commandName, commandArgs, update, message, cancellationToken);
                return;
            }

            // Routing key lives in the user-defined chat-state, exposed through IRoutingStateAccessor
            // when AddBotKitChatState<T>(opts.MapPendingInputKey(...)) was wired. When the accessor
            // isn't registered, [OnUserInput] is silently disabled and we fall through to regex.
            string? pendingKey = scope.ServiceProvider.GetService<IRoutingStateAccessor>()?.PendingInputKey;
            if (!string.IsNullOrWhiteSpace(pendingKey))
                await DispatchUserInputAsync(scope, ctx, pendingKey, update, message, cancellationToken);
            else if (!string.IsNullOrWhiteSpace(text))
                await DispatchRegexAsync(scope, ctx, text, update, message, cancellationToken);
        }

        async Task OnCallbackQueryAsync(Update update, CancellationToken cancellationToken)
        {
            CallbackQuery callbackQuery = update.CallbackQuery!;
            long chatId = callbackQuery.Message!.Chat.Id;
            long telegramUserId = callbackQuery.From.Id;
            string? telegramUsername = callbackQuery.From.Username;

            using IDisposable _ = await AcquireChatLockOrNoopAsync(chatId, cancellationToken);

            using IServiceScope scope = _serviceProvider.CreateScope();
            PopulateUpdateContext(scope, chatId, telegramUserId);
            await BootstrapChatStatesAsync(scope.ServiceProvider, cancellationToken);
            ApplyCulture(scope.ServiceProvider);
            ModuleContext ctx = CreateContext(scope, chatId, telegramUserId, loggerType: typeof(BotUpdateDispatcher));

            try
            {
                if (_options?.OnUserInteractionAsync is not null)
                    await _options.OnUserInteractionAsync(scope.ServiceProvider, telegramUserId, telegramUsername, cancellationToken);

                string data = callbackQuery.Data ?? "";
                var match = _registry.MatchInlineButton(data);
                if (match is null)
                {
                    _logger.LogWarning("Bot {BotId}: no inline handler for callback '{Data}'", _botId, data);
                    return;
                }

                ActionDescriptor desc = match.Value.descriptor;
                ModuleContext actionCtx = ctx with
                {
                    // Re-wrap the logger by module type so logs report the correct category.
                    Logger = (ILogger)scope.ServiceProvider.GetRequiredService(typeof(ILogger<>).MakeGenericType(desc.ModuleType))
                };
                var updCtx = new UpdateContext
                {
                    Module = actionCtx,
                    Update = update,
                    UpdateType = UpdateType.CallbackQuery,
                    CallbackQuery = callbackQuery,
                    RouteValues = match.Value.values,
                    CancellationToken = cancellationToken,
                };
                await ModuleActionInvoker.InvokeAsync(desc, scope.ServiceProvider, updCtx);
            }
            finally
            {
                if (_options?.AutoAnswerCallback ?? true)
                    await TryAutoAnswerCallbackAsync(callbackQuery.Id, cancellationToken);
            }
        }

        /// <summary>
        /// Best-effort auto-answer for the callback query — stops the Telegram client's spinner.
        /// Catches ApiRequestException for the case where the handler already answered manually
        /// (a second answer throws "query is too old").
        /// </summary>
        async Task TryAutoAnswerCallbackAsync(string callbackId, CancellationToken cancellationToken)
        {
            try
            {
                await _bot.AnswerCallbackQuery(callbackId, cancellationToken: cancellationToken);
            }
            catch (global::Telegram.Bot.Exceptions.ApiRequestException ex)
            {
                _logger.LogDebug(ex, "Bot {BotId}: auto-answer callback skipped (already answered)", _botId);
            }
        }

        /// <summary>
        /// Per-update entry point that respects <see cref="TelegramBotKitOptions.PerChatSerialize"/>:
        /// returns a real chat lock when serialization is on (default), or a no-op disposable when
        /// the user has explicitly opted out for stateless handlers.
        /// </summary>
        Task<IDisposable> AcquireChatLockOrNoopAsync(long chatId, CancellationToken cancellationToken)
            => (_options?.PerChatSerialize ?? true)
                ? AcquireChatLockAsync(chatId, cancellationToken)
                : Task.FromResult<IDisposable>(NoopDisposable.Instance);

        sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }

        /// <summary>
        /// Acquire the per-chat lock and return a releaser that frees the slot when disposed.
        /// Refcounted: when the last holder disposes its releaser, the entry is removed from
        /// <see cref="_chatLocks"/> and the underlying <see cref="SemaphoreSlim"/> is disposed,
        /// so memory grows with concurrent in-flight chats — not with lifetime distinct chats.
        /// </summary>
        internal async Task<IDisposable> AcquireChatLockAsync(long chatId, CancellationToken cancellationToken)
        {
            ChatLockEntry entry;
            // Loop handles the race where a releaser marks the entry Removed between our GetOrAdd
            // and our refcount increment — we discard that stale entry and re-add a fresh one.
            while (true)
            {
                entry = _chatLocks.GetOrAdd(chatId, static _ => new ChatLockEntry());
                lock (entry)
                {
                    if (entry.Removed) continue;
                    entry.RefCount++;
                    break;
                }
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
            }
            catch
            {
                ReleaseChatLockRef(chatId, entry);
                throw;
            }
            return new ChatLockReleaser(this, chatId, entry);
        }

        void ReleaseChatLockRef(long chatId, ChatLockEntry entry)
        {
            bool removeNow;
            lock (entry)
            {
                entry.RefCount--;
                removeNow = entry.RefCount == 0;
                if (removeNow) entry.Removed = true;
            }
            if (removeNow)
            {
                // KeyValuePair overload of TryRemove ensures we don't drop a fresh entry that a
                // concurrent acquirer may have just installed under the same key. On netstandard2.0
                // that overload is missing — fall back to the ICollection.Remove implementation
                // ConcurrentDictionary provides, which has the same compare-then-remove semantics.
#if NET5_0_OR_GREATER
                _chatLocks.TryRemove(new KeyValuePair<long, ChatLockEntry>(chatId, entry));
#else
                ((ICollection<KeyValuePair<long, ChatLockEntry>>)_chatLocks)
                    .Remove(new KeyValuePair<long, ChatLockEntry>(chatId, entry));
#endif
                entry.Semaphore.Dispose();
            }
        }

        /// <summary>Test-visible: number of live per-chat lock entries — for verifying refcount cleanup.</summary>
        internal int ChatLockEntryCount => _chatLocks.Count;

        sealed class ChatLockEntry
        {
            public readonly SemaphoreSlim Semaphore = new(1, 1);
            // RefCount and Removed are mutated under lock(this).
            public int RefCount;
            public bool Removed;
        }

        sealed class ChatLockReleaser : IDisposable
        {
            readonly BotUpdateDispatcher _owner;
            readonly long _chatId;
            readonly ChatLockEntry _entry;
            int _disposed;

            public ChatLockReleaser(BotUpdateDispatcher owner, long chatId, ChatLockEntry entry)
            {
                _owner = owner;
                _chatId = chatId;
                _entry = entry;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                // Release before refcount-decrement so any waiter wakes up; the waiter itself
                // already incremented refcount, so the entry won't be removed/disposed under it.
                _entry.Semaphore.Release();
                _owner.ReleaseChatLockRef(_chatId, _entry);
            }
        }

        async Task DispatchCommandAsync(IServiceScope scope, ModuleContext ctx, string name, string? commandArgs, Update update, Message message, CancellationToken cancellationToken)
        {
            ActionDescriptor? desc = _registry.FindCommand(name);
            if (desc is null)
            {
                _logger.LogWarning("Bot {BotId}: no handler for /{Command}", _botId, name);
                return;
            }
            await InvokeAsync(desc, scope, ctx, update, UpdateType.Message, message, callbackQuery: null, routeValues: null, commandArgs, cancellationToken);
        }

        /// <summary>
        /// Parses <c>/start@bot abc</c> → commandName="start", commandArgs="abc".
        /// Strips the <c>@botname</c> mention from the name. Args is the part after the first whitespace
        /// (with leading whitespace trimmed); null when there are no args.
        /// </summary>
        internal static void ParseCommand(string text, out string commandName, out string? commandArgs)
        {
            string raw = text.TrimStart('/');
            int spaceIdx = raw.IndexOf(' ');
            string head = spaceIdx < 0 ? raw : raw[..spaceIdx];
            int atIdx = head.IndexOf('@');
            commandName = atIdx < 0 ? head : head[..atIdx];
            if (spaceIdx < 0)
            {
                commandArgs = null;
            }
            else
            {
                string rest = raw[(spaceIdx + 1)..].TrimStart();
                commandArgs = rest.Length == 0 ? null : rest;
            }
        }

        async Task DispatchUserInputAsync(IServiceScope scope, ModuleContext ctx, string inputKey, Update update, Message message, CancellationToken cancellationToken)
        {
            ActionDescriptor? desc = _registry.FindUserInputHandler(inputKey);
            if (desc is null)
            {
                _logger.LogWarning("Bot {BotId}: OnUserInput handler '{Key}' not found", _botId, inputKey);
                return;
            }
            await InvokeAsync(desc, scope, ctx, update, UpdateType.Message, message, callbackQuery: null, routeValues: null, commandArgs: null, cancellationToken);
        }

        async Task DispatchRegexAsync(IServiceScope scope, ModuleContext ctx, string text, Update update, Message message, CancellationToken cancellationToken)
        {
            // _registry.AllRegexes is already sorted by RegexOrder ascending.
            foreach (ActionDescriptor desc in _registry.AllRegexes)
            {
                if (!desc.Regex!.IsMatch(text)) continue;
                await InvokeAsync(desc, scope, ctx, update, UpdateType.Message, message, callbackQuery: null, routeValues: null, commandArgs: null, cancellationToken);
                if (desc.RegexStopOnMatch) break;
            }
        }

        async Task InvokeAsync(
            ActionDescriptor desc,
            IServiceScope scope,
            ModuleContext ctx,
            Update update,
            UpdateType updateType,
            Message? message,
            CallbackQuery? callbackQuery,
            IReadOnlyDictionary<string, string>? routeValues,
            string? commandArgs,
            CancellationToken cancellationToken)
        {
            ModuleContext actionCtx = ctx with
            {
                Logger = (ILogger)scope.ServiceProvider.GetRequiredService(typeof(ILogger<>).MakeGenericType(desc.ModuleType))
            };
            var updCtx = new UpdateContext
            {
                Module = actionCtx,
                Update = update,
                UpdateType = updateType,
                Message = message,
                CallbackQuery = callbackQuery,
                RouteValues = routeValues,
                CommandArgs = commandArgs,
                CancellationToken = cancellationToken,
            };
            await ModuleActionInvoker.InvokeAsync(desc, scope.ServiceProvider, updCtx);
        }

        /// <summary>
        /// Set <see cref="CultureInfo.CurrentUICulture"/> from the resolved <see cref="ICultureProvider"/>
        /// so user services like <c>IStringLocalizer&lt;T&gt;</c> and <see cref="System.Resources.ResourceManager"/>
        /// pick the right culture for this update without manual setup. The default provider reads
        /// <see cref="ILanguageStateAccessor"/> which is registered when the user wires
        /// <c>opts.MapLanguage(...)</c>. No-op when no provider is registered or it returns null.
        ///
        /// No manual restore is needed: <see cref="CultureInfo.CurrentUICulture"/> on .NET Core 2.0+
        /// flows through <see cref="System.Threading.ExecutionContext"/>, so mutations inside this
        /// async method are scoped to its async-flow and the awaiting caller (the polling loop or
        /// webhook entry) keeps its own culture.
        /// </summary>
        static void ApplyCulture(IServiceProvider scopedProvider)
        {
            ICultureProvider? provider = scopedProvider.GetService<ICultureProvider>();
            if (provider?.GetCulture() is { } culture)
                CultureInfo.CurrentUICulture = culture;
        }

        /// <summary>
        /// Run any registered <see cref="IChatStateBootstrapper"/> for this scope so async-loaded
        /// chat states are in the cache by the time <see cref="ApplyCulture"/> and the handler
        /// constructor resolve them synchronously. No-op when no async chat-state was registered.
        /// </summary>
        static async ValueTask BootstrapChatStatesAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken)
        {
            foreach (IChatStateBootstrapper bootstrapper in scopedProvider.GetServices<IChatStateBootstrapper>())
                await bootstrapper.EnsureLoadedAsync(cancellationToken);
        }

        void PopulateUpdateContext(IServiceScope scope, long chatId, long telegramUserId)
        {
            UpdateContextHolder holder = scope.ServiceProvider.GetRequiredService<UpdateContextHolder>();
            holder.BotId = _botId;
            holder.ChatId = chatId;
            holder.TelegramUserId = telegramUserId;
            holder.BotToken = _botToken;
            holder.Bot = _bot;
        }

        ModuleContext CreateContext(IServiceScope scope, long chatId, long telegramUserId, Type loggerType)
        {
            ILogger logger = (ILogger)scope.ServiceProvider.GetRequiredService(typeof(ILogger<>).MakeGenericType(loggerType));
            return new ModuleContext
            {
                ServiceProvider = scope.ServiceProvider,
                Bot = _bot,
                BotToken = _botToken,
                BotId = _botId,
                ChatId = chatId,
                TelegramUserId = telegramUserId,
                Logger = logger,
            };
        }
    }
}
