namespace TqkLibrary.Telegram.BotKit.Attributes
{
    /// <summary>
    /// Matches a text message against a regex. Only fires when the text is not a /command
    /// and no <see cref="IRoutingStateAccessor.PendingInputKey"/> is pending.
    /// By default, all matching handlers run sequentially in <see cref="Order"/>-ascending order.
    /// Set <see cref="StopOnMatch"/> to true so the first match short-circuits and prevents
    /// later (higher-order) handlers from firing.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class TelegramRegexAttribute : Attribute
    {
        public TelegramRegexAttribute(string pattern, int order = int.MaxValue, bool compiled = true)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
            RegexOptions options = RegexOptions.IgnoreCase;
            if (compiled) options |= RegexOptions.Compiled;
            Regex = new Regex(pattern, options);
            Order = order;
        }

        public Regex Regex { get; }

        /// <summary>Dispatch order when multiple regexes match. Lower runs first. Default <see cref="int.MaxValue"/>.</summary>
        public int Order { get; }

        /// <summary>When true, a successful match prevents handlers with higher Order from running. Default false.</summary>
        public bool StopOnMatch { get; set; }
    }
}
