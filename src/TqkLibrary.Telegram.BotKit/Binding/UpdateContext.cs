using TqkLibrary.Telegram.BotKit.Handlers;

namespace TqkLibrary.Telegram.BotKit.Binding
{
    /// <summary>
    /// Per-dispatch data bundle — collected so the parameter binder can read everything from one place.
    /// Not every field is populated (e.g. callbacks have no <see cref="Message"/>,
    /// commands have no <see cref="CallbackQuery"/>).
    /// </summary>
    public sealed class UpdateContext
    {
        public required ModuleContext Module { get; init; }
        public required Update Update { get; init; }
        public required UpdateType UpdateType { get; init; }
        public Message? Message { get; init; }
        public CallbackQuery? CallbackQuery { get; init; }
        public IReadOnlyDictionary<string, string>? RouteValues { get; init; }
        /// <summary>Args portion after the command name (e.g. <c>/start abc</c> → <c>"abc"</c>). Only set on /command dispatch.</summary>
        public string? CommandArgs { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }
}
