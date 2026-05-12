using TqkLibrary.Telegram.BotKit.Middleware;

namespace TqkLibrary.Telegram.BotKit.SimpleDemo.Middleware
{
    /// <summary>
    /// Class-based middleware example. Registered via <c>opts.UseMiddleware&lt;ExceptionLoggingMiddleware&gt;()</c>
    /// and resolved per-update from the scoped service provider, so it can take scoped deps
    /// (here: nothing extra — just the typed logger from DI).
    /// </summary>
    public sealed class ExceptionLoggingMiddleware : IBotMiddleware
    {
        readonly ILogger<ExceptionLoggingMiddleware> _logger;

        public ExceptionLoggingMiddleware(ILogger<ExceptionLoggingMiddleware> logger)
        {
            _logger = logger;
        }

        public async Task InvokeAsync(BotMiddlewareContext context, BotRequestDelegate next)
        {
            try
            {
                await next(context);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                // Host shutdown — propagate, this is not a handler bug.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Bot {BotId}: unhandled handler exception (chat={ChatId}, user={UserId}, type={UpdateType})",
                    context.BotId, context.ChatId, context.TelegramUserId, context.UpdateType);
                // Swallow so the polling loop keeps running. Real apps may also notify support.
            }
        }
    }
}
