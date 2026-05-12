using TqkLibrary.Telegram.BotKit.Handlers;
using TqkLibrary.Telegram.BotKit.Middleware;

namespace TqkLibrary.Telegram.BotKit
{
    public class TelegramBotKitOptions
    {
        /// <summary>Assemblies to scan for modules (<see cref="CallbackModule"/>).</summary>
        internal readonly List<Assembly> ModuleAssemblies = new();

        /// <summary>Manually registered module types. Deduped against assembly scan results.</summary>
        internal readonly List<Type> ModuleTypes = new();

        /// <summary>Manually registered command types. Deduped across calls.</summary>
        internal readonly List<Type> CommandTypes = new();

        /// <summary>
        /// Middleware pipeline entries in registration order. First entry runs outermost
        /// (sees every later stage and the terminal dispatch). Both class-based
        /// <see cref="IBotMiddleware"/> registrations and functional <c>Use(...)</c>
        /// registrations are stored here, already adapted into the same delegate signature.
        /// </summary>
        internal readonly List<Func<BotMiddlewareContext, BotRequestDelegate, Task>> Middlewares = new();

        /// <summary>
        /// Class-based middleware types collected so <see cref="TelegramBotKitServiceExtensions"/>
        /// can register them in DI (TryAddTransient). Functional <c>Use(...)</c> registrations
        /// don't need a DI entry, so they don't appear here.
        /// </summary>
        internal readonly List<Type> MiddlewareTypes = new();

        /// <summary>
        /// Hook invoked on every received update (Message/CallbackQuery). Receives the scoped IServiceProvider,
        /// Telegram UserId, username (nullable), and CancellationToken. Useful for guest upsert, rate limiting, etc.
        /// </summary>
        public Func<IServiceProvider, long, string?, CancellationToken, Task>? OnUserInteractionAsync { get; set; }

        /// <summary>
        /// List of <see cref="UpdateType"/>s Telegram is allowed to push to the bot (passed to the webhook
        /// <c>SetWebhook</c> call and the polling <c>ReceiverOptions.AllowedUpdates</c>).
        /// Default: [<see cref="UpdateType.Message"/>, <see cref="UpdateType.CallbackQuery"/>] —
        /// matching the two types <see cref="BotUpdateDispatcher"/> currently handles.
        /// Set to null to let Telegram apply its own default (every type except chat_member, message_reaction, ...).
        /// </summary>
        public IReadOnlyList<UpdateType>? AllowedUpdates { get; set; } =
            [UpdateType.Message, UpdateType.CallbackQuery];

        /// <summary>
        /// true (default) = after the callback handler returns, the dispatcher calls
        /// <c>AnswerCallbackQuery(callbackId)</c> automatically so the Telegram client stops the spinner —
        /// the handler may still answer manually (e.g. with a text popup); the second auto-answer throws
        /// "query is too old" and is caught silently. Set to false for fully manual handling.
        /// </summary>
        public bool AutoAnswerCallback { get; set; } = true;

        /// <summary>
        /// true (default) = serialize updates per <c>chatId</c> through a refcounted
        /// <see cref="System.Threading.SemaphoreSlim"/>, so two updates from the same chat never
        /// run their handlers concurrently. Set to false when handlers are stateless / read-only
        /// and the serialization cost (and the head-of-line blocking on a slow handler) is not
        /// worth the safety. Disabling does NOT remove the per-update DI scope or chat-state
        /// caching — those still run; only the lock is skipped.
        /// </summary>
        public bool PerChatSerialize { get; set; } = true;

        /// <summary>
        /// Scan the assembly containing <typeparamref name="T"/> for every <see cref="CallbackModule"/>.
        /// Only types deriving from <see cref="CallbackModule"/> are registered — command classes are skipped.
        /// </summary>
        public TelegramBotKitOptions AddModulesFromAssemblyOf<T>()
        {
            Assembly assembly = typeof(T).Assembly;
            if (!ModuleAssemblies.Contains(assembly))
                ModuleAssemblies.Add(assembly);
            return this;
        }

        /// <summary>Manually register a single module type (skipping assembly scan).</summary>
        public TelegramBotKitOptions AddModule<T>() where T : CallbackModule
        {
            if (!ModuleTypes.Contains(typeof(T)))
                ModuleTypes.Add(typeof(T));
            return this;
        }

        /// <summary>Manually register several module types. Each must be a concrete class deriving from <see cref="CallbackModule"/>.</summary>
        public TelegramBotKitOptions AddModules(params Type[] types)
        {
            foreach (Type t in types)
            {
                if (t.IsAbstract || !typeof(CallbackModule).IsAssignableFrom(t))
                    throw new ArgumentException(
                        $"Type {t.FullName} must be a concrete class deriving from {nameof(CallbackModule)}.",
                        nameof(types));
                if (!ModuleTypes.Contains(t))
                    ModuleTypes.Add(t);
            }
            return this;
        }

        /// <summary>Manually register a single command class containing <c>/command</c> handlers.</summary>
        public TelegramBotKitOptions AddCommand<T>() where T : CommandModule
        {
            if (!CommandTypes.Contains(typeof(T)))
                CommandTypes.Add(typeof(T));
            return this;
        }

        /// <summary>Manually register several command types. Each must be a concrete class deriving from <see cref="CommandModule"/>.</summary>
        public TelegramBotKitOptions AddCommands(params Type[] types)
        {
            foreach (Type t in types)
            {
                if (t.IsAbstract || !typeof(CommandModule).IsAssignableFrom(t))
                    throw new ArgumentException(
                        $"Type {t.FullName} must be a concrete class deriving from {nameof(CommandModule)}.",
                        nameof(types));
                if (!CommandTypes.Contains(t))
                    CommandTypes.Add(t);
            }
            return this;
        }

        /// <summary>
        /// Register an inline functional middleware. Runs in registration order — first
        /// <c>Use</c> call wraps the rest of the pipeline (outermost), so place exception
        /// handlers FIRST and gates AFTER. Call <c>await next(ctx)</c> to continue;
        /// return without calling <c>next</c> to short-circuit.
        /// </summary>
        public TelegramBotKitOptions Use(Func<BotMiddlewareContext, BotRequestDelegate, Task> middleware)
        {
            if (middleware is null) throw new ArgumentNullException(nameof(middleware));
            Middlewares.Add(middleware);
            return this;
        }

        /// <summary>
        /// Register a class-based middleware <typeparamref name="T"/>. Resolved from the
        /// per-update scoped <see cref="IServiceProvider"/>, so it can take scoped deps in
        /// its constructor. Same ordering rules as <see cref="Use"/>.
        /// </summary>
        public TelegramBotKitOptions UseMiddleware<T>() where T : IBotMiddleware
        {
            Type type = typeof(T);
            if (!MiddlewareTypes.Contains(type))
                MiddlewareTypes.Add(type);
            Middlewares.Add((ctx, next) =>
            {
                IBotMiddleware mw = (IBotMiddleware)ctx.Services.GetRequiredService(type);
                return mw.InvokeAsync(ctx, next);
            });
            return this;
        }
    }
}
