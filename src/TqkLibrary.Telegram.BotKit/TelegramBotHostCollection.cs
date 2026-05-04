namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Manages many <see cref="TelegramBotHost"/> instances — one per token.
    /// Thread-safe; registered as a singleton.
    /// </summary>
    public sealed class TelegramBotHostCollection : IAsyncDisposable
    {
        // key = BotToken
        readonly ConcurrentDictionary<string, TelegramBotHost> _hosts = new(StringComparer.OrdinalIgnoreCase);
        // key = WebhookPath (resolved from token via IBotWebhookPathResolver) — used to route incoming webhooks
        readonly ConcurrentDictionary<string, TelegramBotHost> _webhookHosts = new(StringComparer.Ordinal);
        // Per-token in-flight start. The first caller to GetOrAdd publishes the Lazy that wraps
        // StartHostAsync; concurrent callers for the same token await the same Task. Callers for
        // different tokens never block each other (was: a single global SemaphoreSlim).
        readonly ConcurrentDictionary<string, Lazy<Task<TelegramBotHost>>> _startTasks =
            new(StringComparer.OrdinalIgnoreCase);

        readonly IServiceProvider _serviceProvider;
        readonly ModuleActionRegistry _registry;
        readonly ILoggerFactory _loggerFactory;
        readonly ILogger<TelegramBotHostCollection> _logger;
        readonly IBotWebhookPathResolver _pathResolver;

        public TelegramBotHostCollection(
            IServiceProvider serviceProvider,
            ModuleActionRegistry registry,
            ILoggerFactory loggerFactory,
            IBotWebhookPathResolver pathResolver)
        {
            _serviceProvider = serviceProvider;
            _registry = registry;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<TelegramBotHostCollection>();
            _pathResolver = pathResolver;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Add a bot and start it in long-polling mode. If the bot is already added,
        /// the existing host is returned (mode is whatever it was first started with).
        /// </summary>
        public Task<TelegramBotHost> AddAndStartPollingAsync(string token, CancellationToken cancellationToken = default)
            => AddAndStartCoreAsync(token, webhookBaseUrl: null, cancellationToken);

        /// <summary>
        /// Add a bot and start it in webhook mode. The full webhook URL is built as
        /// <c>{webhookBaseUrl}/{path}</c> where <c>path</c> comes from
        /// <see cref="IBotWebhookPathResolver"/>; the same path is sent as Telegram's
        /// <c>secret_token</c>. If the bot is already added, the existing host is
        /// returned (mode + URL come from the first registration).
        /// </summary>
        public Task<TelegramBotHost> AddAndStartWebhookAsync(string token, string webhookBaseUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(webhookBaseUrl))
                throw new ArgumentException("webhookBaseUrl must not be empty.", nameof(webhookBaseUrl));
            return AddAndStartCoreAsync(token, webhookBaseUrl, cancellationToken);
        }

        Task<TelegramBotHost> AddAndStartCoreAsync(string token, string? webhookBaseUrl, CancellationToken cancellationToken)
        {
            if (_hosts.TryGetValue(token, out TelegramBotHost? existing))
                return Task.FromResult(existing);

            Lazy<Task<TelegramBotHost>> lazy = _startTasks.GetOrAdd(
                token,
                t => new Lazy<Task<TelegramBotHost>>(
                    () => StartHostAsync(t, webhookBaseUrl, cancellationToken),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            return AwaitStartAsync(token, lazy);
        }

        async Task<TelegramBotHost> AwaitStartAsync(string token, Lazy<Task<TelegramBotHost>> lazy)
        {
            try
            {
                return await lazy.Value;
            }
            catch
            {
                // Remove the failed Lazy so a subsequent call can retry. Compare-then-remove via
                // KeyValuePair overload (or its ICollection equivalent on netstandard2.0) ensures
                // we don't drop a fresh Lazy that another thread may have just installed.
#if NET5_0_OR_GREATER
                _startTasks.TryRemove(new KeyValuePair<string, Lazy<Task<TelegramBotHost>>>(token, lazy));
#else
                ((ICollection<KeyValuePair<string, Lazy<Task<TelegramBotHost>>>>)_startTasks)
                    .Remove(new KeyValuePair<string, Lazy<Task<TelegramBotHost>>>(token, lazy));
#endif
                throw;
            }
        }

        async Task<TelegramBotHost> StartHostAsync(string token, string? webhookBaseUrl, CancellationToken cancellationToken)
        {
            string? webhookPath = null;
            string? webhookUrl = null;
            if (webhookBaseUrl is not null)
            {
                webhookPath = _pathResolver.ResolvePath(token);
                webhookUrl = $"{webhookBaseUrl.TrimEnd('/')}/{webhookPath}";
            }

            TelegramBotHost host = new(
                token, webhookUrl, webhookPath,
                _serviceProvider, _registry, _loggerFactory);

            await host.StartAsync(cancellationToken);
            _hosts[token] = host;
            if (webhookPath is not null)
                _webhookHosts[webhookPath] = host;

            return host;
        }

        /// <summary>Stop the bot and remove it from the collection.</summary>
        public async Task StopAsync(string token)
        {
            if (_hosts.TryRemove(token, out TelegramBotHost? host))
            {
                await host.StopAsync();
                if (host.WebhookPath is not null)
                    _webhookHosts.TryRemove(host.WebhookPath, out _);
            }
            else
            {
                _logger.LogWarning("StopAsync: token not found in collection");
            }
        }

        public bool Contains(string token) => _hosts.ContainsKey(token);

        /// <summary>Returns the host for the given token, or null if not added.</summary>
        public TelegramBotHost? GetHost(string token)
            => _hosts.TryGetValue(token, out TelegramBotHost? h) ? h : null;

        public bool IsRunning(string token)
            => _hosts.TryGetValue(token, out TelegramBotHost? h) && h.IsRunning;

        /// <summary>Number of bots currently running.</summary>
        public int RunningCount => _hosts.Values.Count(h => h.IsRunning);

        /// <summary>
        /// Routes a webhook update to the correct bot. The bot must already be registered
        /// via <see cref="AddAndStartWebhookAsync"/>; if it isn't, the update is dropped
        /// (warning logged). Use the overload with <c>webhookBaseUrl</c> if you want
        /// on-demand auto-start via <see cref="IBotTokenResolver"/>.
        /// </summary>
        public async Task HandleWebhookUpdateAsync(string webhookPath, Update update, CancellationToken cancellationToken = default)
        {
            if (_webhookHosts.TryGetValue(webhookPath, out TelegramBotHost? host))
            {
                await host.ProcessUpdateAsync(update, cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "HandleWebhookUpdateAsync: webhookPath '{Path}' not registered. " +
                    "Use the overload with webhookBaseUrl to enable on-demand auto-start.",
                    webhookPath);
            }
        }

        /// <summary>
        /// Routes a webhook update to the correct bot, auto-starting via
        /// <see cref="IBotTokenResolver"/> if the bot is not yet registered.
        /// <paramref name="webhookBaseUrl"/> is required only for the auto-start path so
        /// the lib can register the webhook URL with Telegram.
        /// </summary>
        public async Task HandleWebhookUpdateAsync(string webhookPath, string webhookBaseUrl, Update update, CancellationToken cancellationToken = default)
        {
            if (!_webhookHosts.TryGetValue(webhookPath, out TelegramBotHost? host))
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                IBotTokenResolver? resolver = scope.ServiceProvider.GetService<IBotTokenResolver>();
                if (resolver is null)
                {
                    _logger.LogWarning("HandleWebhookUpdateAsync: unknown webhookPath '{Path}' and IBotTokenResolver not registered", webhookPath);
                    return;
                }

                string? token = await resolver.GetBotTokenAsync(webhookPath, cancellationToken);
                if (token is null)
                {
                    _logger.LogWarning("HandleWebhookUpdateAsync: webhookPath '{Path}' not found in DB", webhookPath);
                    return;
                }

                host = await AddAndStartWebhookAsync(token, webhookBaseUrl, cancellationToken);
                _logger.LogInformation("Bot {BotId} started on-demand from webhook path {Path}", host.BotId, webhookPath);
            }

            await host.ProcessUpdateAsync(update, cancellationToken);
        }

        // ── Dispose ────────────────────────────────────────────────────────────

        public async ValueTask DisposeAsync()
        {
            foreach (TelegramBotHost host in _hosts.Values)
                await host.DisposeAsync();
            _hosts.Clear();
            _webhookHosts.Clear();
        }
    }
}
