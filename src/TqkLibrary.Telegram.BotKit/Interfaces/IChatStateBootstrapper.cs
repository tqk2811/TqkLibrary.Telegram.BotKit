namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Per-update hook the dispatcher calls before resolving any handler — gives async-loaded
    /// chat states a chance to fetch their value from the backing store (e.g. database) and
    /// stash it so subsequent sync DI resolves can return it without sync-over-async.
    ///
    /// One implementation is registered (scoped) per <c>AddBotKitChatState&lt;T&gt;(asyncFactory)</c>
    /// call. The dispatcher iterates all registered bootstrappers via <c>GetServices</c> at the
    /// start of each update, so adding/removing async chat-state types is transparent to the
    /// dispatch loop. Sync registrations skip this hook entirely (their factory runs lazily on
    /// the first DI resolve).
    /// </summary>
    public interface IChatStateBootstrapper
    {
        ValueTask EnsureLoadedAsync(CancellationToken cancellationToken);
    }
}
