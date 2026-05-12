namespace TqkLibrary.Telegram.BotKit.Middleware
{
    /// <summary>
    /// Terminal delegate for the bot middleware pipeline — invoked by a middleware to pass
    /// control to the next stage. The last stage (terminal) performs the actual dispatch
    /// (OnUserInteraction hook + route match + handler invoke).
    /// </summary>
    public delegate Task BotRequestDelegate(BotMiddlewareContext context);
}
