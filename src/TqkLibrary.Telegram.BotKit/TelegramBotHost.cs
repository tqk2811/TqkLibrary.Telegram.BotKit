using System.Globalization;
using Microsoft.Extensions.Localization;

namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Manages the lifecycle of a single bot. Mode (polling vs webhook) is fixed at
    /// construction time by <see cref="TelegramBotHostCollection"/> and stored on the
    /// instance, so <see cref="StartAsync"/> can restart with the original mode.
    /// </summary>
    public sealed class TelegramBotHost : IAsyncDisposable
    {
        readonly string _token;
        readonly TelegramBotClient _botClient;
        readonly IServiceProvider _serviceProvider;
        readonly ModuleActionRegistry _registry;
        readonly ILoggerFactory _loggerFactory;
        readonly ILogger<TelegramBotHost> _logger;
        readonly string? _webhookUrl;

        BotUpdateDispatcher? _dispatcher;
        CancellationTokenSource? _cts;
        Task? _receiveTask;

        /// <summary>Telegram UserId of the bot — populated after a successful <see cref="StartAsync"/>.</summary>
        public long BotId { get; private set; }

        public bool IsRunning { get; private set; }

        public bool IsWebhookMode => _webhookUrl is not null;

        /// <summary>
        /// Webhook URL path segment / Telegram <c>secret_token</c> derived from the bot
        /// token by <see cref="IBotWebhookPathResolver"/>. <c>null</c> in polling mode.
        /// </summary>
        public string? WebhookPath { get; }

        internal TelegramBotHost(
            string token,
            string? webhookUrl,
            string? webhookPath,
            IServiceProvider serviceProvider,
            ModuleActionRegistry registry,
            ILoggerFactory loggerFactory)
        {
            _token = token;
            _webhookUrl = webhookUrl;
            WebhookPath = webhookPath;
            _botClient = new TelegramBotClient(token);
            _serviceProvider = serviceProvider;
            _registry = registry;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<TelegramBotHost>();
        }

        /// <summary>
        /// Starts (or restarts) the bot using the mode chosen at construction.
        ///   - Webhook → registers the URL with Telegram; <see cref="WebhookPath"/> is sent as <c>secret_token</c>.
        ///   - Polling → calls <c>StartReceiving</c> against Telegram.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (IsRunning) return;

            User me = await _botClient.GetMe(cancellationToken);
            BotId = me.Id;

            _dispatcher = new BotUpdateDispatcher(
                _botClient, _token, BotId,
                _serviceProvider, _registry,
                _loggerFactory);

            TelegramBotKitOptions? kitOptions = _serviceProvider.GetService<TelegramBotKitOptions>();
            IEnumerable<UpdateType>? allowedUpdates = kitOptions?.AllowedUpdates;

            if (_webhookUrl is not null)
            {
                await _botClient.SetWebhook(
                    _webhookUrl,
                    allowedUpdates: allowedUpdates,
                    secretToken: WebhookPath,
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Bot {BotId} (@{Username}) webhook set: {Url}", BotId, me.Username, _webhookUrl);
            }
            else
            {
                _cts = new CancellationTokenSource();
                await _botClient.DeleteWebhook(dropPendingUpdates: true, cancellationToken: cancellationToken);
                // ReceiveAsync (vs fire-and-forget StartReceiving) returns the polling Task so
                // StopAsync can await it before disposing the CTS — otherwise an in-flight handler
                // touching _cts.Token after Dispose would throw ObjectDisposedException.
                _receiveTask = _botClient.ReceiveAsync(
                    updateHandler: (IUpdateHandler)_dispatcher,
                    receiverOptions: new ReceiverOptions { AllowedUpdates = allowedUpdates?.ToArray() },
                    cancellationToken: _cts.Token);
                _logger.LogInformation("Bot {BotId} (@{Username}) started polling", BotId, me.Username);
            }
            IsRunning = true;
        }

        /// <summary>
        /// Stops the bot.
        /// Webhook mode → deletes the webhook on Telegram.
        /// Polling mode → cancels the StartReceiving token.
        /// </summary>
        public async Task StopAsync()
        {
            if (!IsRunning) return;

            if (IsWebhookMode)
            {
                try
                {
                    await _botClient.DeleteWebhook(cancellationToken: default);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Bot {BotId}: failed to delete webhook on stop", BotId);
                }
            }
            else if (_cts is not null)
            {
#if NET8_0_OR_GREATER
                await _cts.CancelAsync();
#else
                _cts.Cancel();
#endif
                if (_receiveTask is not null)
                {
                    try { await _receiveTask; }
                    catch (OperationCanceledException) { /* expected on cancel */ }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Bot {BotId}: polling task ended with exception during stop", BotId);
                    }
                    _receiveTask = null;
                }
                _cts.Dispose();
                _cts = null;
            }

            IsRunning = false;
            _logger.LogInformation("Bot {BotId} stopped", BotId);
        }

        /// <summary>
        /// Push /command list to Telegram (SetMyCommands) from a specific <typeparamref name="TCommand"/>
        /// class registered in <see cref="ModuleActionRegistry"/>. Call manually after
        /// <see cref="StartAsync"/> — not automatic. If the class has no commands with descriptions, no-op.
        ///
        /// <paramref name="scope"/> and <paramref name="languageCode"/> map to Telegram's SetMyCommands
        /// parameters: pass <see cref="BotCommandScope"/> subclasses (BotCommandScopeChat, etc.) to
        /// target a specific chat/user, and a BCP-47 tag to publish a per-language menu.
        /// </summary>
        public Task SetCommandsAsync<TCommand>(
            CultureInfo? culture = null,
            BotCommandScope? scope = null,
            string? languageCode = null,
            CancellationToken cancellationToken = default)
            where TCommand : Handlers.CommandModule
            => SetCommandsAsync(typeof(TCommand), culture, scope, languageCode, cancellationToken);

        /// <inheritdoc cref="SetCommandsAsync{TCommand}"/>
        public async Task SetCommandsAsync(
            Type commandType,
            CultureInfo? culture = null,
            BotCommandScope? scope = null,
            string? languageCode = null,
            CancellationToken cancellationToken = default)
        {
            if (commandType is null) throw new ArgumentNullException(nameof(commandType));
            if (!IsRunning) throw new InvalidOperationException($"Bot {BotId} is not started.");
            // IStringLocalizer (when registered) lets attributes that omit DescriptionResourceType
            // still resolve via the registered fallback localizer, with culture flipped per request.
            IStringLocalizer? localizer = _serviceProvider.GetService<IStringLocalizer>();
            List<BotCommand> commands = _registry.GetBotCommands(commandType, culture, localizer).ToList();
            if (commands.Count == 0)
            {
                _logger.LogWarning(
                    "Bot {BotId}: SetCommandsAsync({Type}, culture={Culture}, languageCode={Lang}) resolved 0 commands — " +
                    "attribute Description/DescriptionResourceName missing, or the fallback IStringLocalizer is not registered. " +
                    "Telegram menu unchanged.",
                    BotId, commandType.Name, culture?.Name ?? "(null)", languageCode ?? "(null)");
                return;
            }
            await _botClient.SetMyCommands(commands, scope: scope, languageCode: languageCode, cancellationToken: cancellationToken);
            _logger.LogInformation(
                "Bot {BotId}: published {Count} command(s) for {Type} (culture={Culture}, languageCode={Lang}, scope={Scope}): {Commands}",
                BotId, commands.Count, commandType.Name, culture?.Name ?? "(null)", languageCode ?? "(null)",
                scope?.GetType().Name ?? "(default)",
                string.Join(", ", commands.Select(c => $"/{c.Command}={c.Description}")));
        }

        /// <summary>
        /// Push a pre-built <see cref="BotCommand"/> list to Telegram (SetMyCommands). Use when the
        /// caller wants full control over the description text — bypasses <see cref="ModuleActionRegistry"/>
        /// and any attribute-based localization. Useful when descriptions come from a non-resx source
        /// (e.g. a custom dictionary, database, or runtime-built per-user catalog).
        /// </summary>
        public async Task SetCommandsAsync(
            IEnumerable<BotCommand> commands,
            BotCommandScope? scope = null,
            string? languageCode = null,
            CancellationToken cancellationToken = default)
        {
            if (commands is null) throw new ArgumentNullException(nameof(commands));
            if (!IsRunning) throw new InvalidOperationException($"Bot {BotId} is not started.");
            await _botClient.SetMyCommands(commands, scope: scope, languageCode: languageCode, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Delete the registered /command list on Telegram (DeleteMyCommands). When <paramref name="scope"/>
        /// is null Telegram clears the default scope; pass a scope to clear only that scope.
        /// </summary>
        public async Task ClearCommandsAsync(
            BotCommandScope? scope = null,
            string? languageCode = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsRunning) throw new InvalidOperationException($"Bot {BotId} is not started.");
            await _botClient.DeleteMyCommands(scope: scope, languageCode: languageCode, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Process an update received from the webhook endpoint.
        /// Webhook-mode only; polling mode receives updates automatically via StartReceiving.
        /// </summary>
        public Task ProcessUpdateAsync(Update update, CancellationToken cancellationToken = default)
        {
            if (_dispatcher is null)
                throw new InvalidOperationException($"Bot {BotId} is not started.");
            return _dispatcher.HandleUpdateAsync(_botClient, update, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }
    }
}
