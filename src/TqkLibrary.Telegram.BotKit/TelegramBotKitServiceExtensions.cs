using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TqkLibrary.Telegram.BotKit
{
    public static class TelegramBotKitServiceExtensions
    {
        /// <summary>
        /// Register TqkLibrary.Telegram.BotKit. Does not construct a <see cref="TelegramBotClient"/> —
        /// <see cref="TelegramBotHostCollection"/> creates clients on demand from each token.
        /// </summary>
        public static IServiceCollection AddTelegramBotKit(
            this IServiceCollection services,
            Action<TelegramBotKitOptions> configure)
        {
            TelegramBotKitOptions options = new();
            configure(options);
            services.AddSingleton(options);

            ModuleActionRegistry registry = new(
                options.ModuleAssemblies,
                options.ModuleTypes,
                options.CommandTypes);
            services.AddSingleton(registry);

            // Register every handler (Module + Command) as scoped so constructor DI works when
            // ActivatorUtilities.CreateInstance resolves them through the per-update scope.
            foreach (Type t in registry.HandlerTypes)
                services.AddScoped(t);

            services.AddMemoryCache();

            // Per-update ambient context. Dispatcher fills the holder on each scope; user services
            // (chat-state factories, custom DI services) consume the read-only IUpdateContext view.
            services.AddScoped<UpdateContextHolder>();
            services.AddScoped<IUpdateContext>(sp => sp.GetRequiredService<UpdateContextHolder>());

            // Default culture provider — MUST be scoped because it depends on the scoped
            // ILanguageStateAccessor (which itself wraps the per-update DemoChatState/BotKitChatStateBase
            // instance). Registering as singleton would capture a root-scoped accessor that's tied
            // to a "garbage" chat-state instance (BotId=0, ChatId=0) and language never changes.
            // TryAdd so user can override with their own ICultureProvider implementation.
            services.TryAddScoped<ICultureProvider, DefaultCultureProvider>();

            services.AddSingleton<TelegramBotHostCollection>();
            // Default webhook path = SHA-256(token) hex. Override by registering your own
            // IBotWebhookPathResolver before/after calling AddTelegramBotKit.
            services.TryAddSingleton<IBotWebhookPathResolver, DefaultBotWebhookPathResolver>();
            return services;
        }
    }
}
