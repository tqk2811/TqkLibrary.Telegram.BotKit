namespace TqkLibrary.Telegram.BotKit.Middleware
{
    /// <summary>
    /// Class-based bot middleware (ASP.NET Core style). Resolved per-update from the scoped
    /// <see cref="IServiceProvider"/> on <see cref="BotMiddlewareContext.Services"/>, so
    /// implementations may declare scoped dependencies in their constructor.
    ///
    /// Two canonical uses:
    /// <list type="bullet">
    /// <item><b>Gate</b> — inspect <see cref="BotMiddlewareContext.Message"/>/CallbackQuery,
    /// short-circuit by returning without calling <c>next(ctx)</c>.</item>
    /// <item><b>Exception handler</b> — wrap <c>await next(ctx)</c> in try/catch, log/report,
    /// optionally reply to the user.</item>
    /// </list>
    /// Register with <c>opts.UseMiddleware&lt;T&gt;()</c>; the order of registration is the
    /// pipeline order — the first registered runs outermost (sees everything below it).
    /// </summary>
    public interface IBotMiddleware
    {
        Task InvokeAsync(BotMiddlewareContext context, BotRequestDelegate next);
    }
}
