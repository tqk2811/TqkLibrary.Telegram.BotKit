namespace TqkLibrary.Telegram.BotKit.SimpleDemo.Modules
{
    /// <summary>
    /// Showcases the inline-button argument-expression fixes:
    /// <list type="bullet">
    ///   <item>Scalar enum rendered by member name (readable callback data).</item>
    ///   <item><c>[Flags]</c> enum rendered as the numeric form to keep callback data short.</item>
    ///   <item><c>(int)enumValue</c> cast respected at render time — renders the underlying number, not the enum name.</item>
    ///   <item>User-defined <c>implicit</c>/<c>explicit operator</c> invoked at render time — wrapper structs unwrap to the underlying value.</item>
    /// </list>
    /// Pressing each button echoes the parsed value back so the round-trip is observable.
    /// </summary>
    [CallbackPrefix("ex")]
    public class ExpressionShowcaseModule : CallbackModule
    {
        public enum SiteName
        {
            Alpha = 1,
            Bravo = 2,
            Charlie = 3,
        }

        [Flags]
        public enum Permission
        {
            None = 0,
            Read = 1,
            Write = 2,
            Execute = 4,
        }

        // Strongly-typed wrapper with explicit conversion to long — exercises the user-defined operator path.
        public readonly record struct OrderId(long Value)
        {
            public static explicit operator long(OrderId id) => id.Value;
        }

        // Implicit conversion to Guid — exercises the implicit-operator path.
        public readonly record struct DocumentRef(Guid Value)
        {
            public static implicit operator Guid(DocumentRef d) => d.Value;
        }

        readonly DemoChatState _state;
        readonly IStringLocalizer _l;

        public ExpressionShowcaseModule(DemoChatState state, IStringLocalizer localizer)
        {
            _state = state;
            _l = localizer;
        }

        [InlineButton("open", TitleResourceName = "BtnExpressions")]
        public async Task Open(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            await Bot.EditMessageText(
                ChatId,
                callbackQuery.Message!.MessageId,
                BuildSummary(),
                replyMarkup: BuildKeyboard(),
                cancellationToken: cancellationToken);
        }

        // 1) Scalar enum — callback data renders the member name (e.g. "ex/site/Bravo").
        [InlineButton("site/{site}")]
        public async Task PickSite(SiteName site, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            _state.LastSite = site;
            await RefreshAsync(callbackQuery, cancellationToken);
        }

        // 2) (int) cast — the cast must propagate so callback data is "ex/site-id/2", not "ex/site-id/Bravo".
        //    Without commit 92d56d6, the int-typed route param would reject "Bravo" and the bot would look unresponsive.
        [InlineButton("site-id/{id}")]
        public async Task PickSiteAsInt(int id, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            _state.LastSiteId = id;
            await RefreshAsync(callbackQuery, cancellationToken);
        }

        // 3) [Flags] enum — callback data renders numerically (e.g. "ex/perm/3" for Read|Write), not "Read,Write".
        //    Match path bypasses Enum.IsDefined for [Flags] types so any bit-combination round-trips.
        [InlineButton("perm/{perm}")]
        public async Task TogglePerm(Permission perm, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            _state.LastPerm ^= perm;
            await RefreshAsync(callbackQuery, cancellationToken);
        }

        // 4) Explicit user-defined operator long — wrapper struct must unwrap to the underlying long.
        //    Without commit 168a904, the formatter would receive the OrderId struct and stringify its record-struct print.
        [InlineButton("order/{id}")]
        public async Task PickOrder(long id, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            _state.LastOrderId = id;
            await RefreshAsync(callbackQuery, cancellationToken);
        }

        // 5) Implicit user-defined operator Guid — same path, no explicit cast at the call site.
        [InlineButton("doc/{id}")]
        public async Task PickDoc(Guid id, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            _state.LastDocId = id;
            await RefreshAsync(callbackQuery, cancellationToken);
        }

        async Task RefreshAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            await Bot.EditMessageText(
                ChatId,
                callbackQuery.Message!.MessageId,
                BuildSummary(),
                replyMarkup: BuildKeyboard(),
                cancellationToken: cancellationToken);
        }

        string BuildSummary()
            => _l["ExpressionsSummary",
                _state.LastSite?.ToString() ?? "—",
                _state.LastSiteId?.ToString() ?? "—",
                _state.LastPerm == Permission.None ? "—" : $"{_state.LastPerm} ({(int)_state.LastPerm})",
                _state.LastOrderId?.ToString() ?? "—",
                _state.LastDocId?.ToString("D") ?? "—"];

        InlineKeyboardMarkup BuildKeyboard()
        {
            // Local fixtures referenced inside the expression trees — captured-closure access exercises
            // the MemberExpression path in EvaluateExpression alongside the cast / operator paths.
            SiteName scalar = SiteName.Bravo;
            Permission flags = Permission.Read | Permission.Write;
            OrderId order = new(98765L);
            DocumentRef doc = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

            var rows = new List<IEnumerable<InlineKeyboardButton>>
            {
                // (1) Plain enum literals — render as "Alpha" / "Bravo" / "Charlie".
                new[]
                {
                    ToInlineButton<ExpressionShowcaseModule>(c => c.PickSite(SiteName.Alpha, default!, default), text: "Site = Alpha"),
                    ToInlineButton<ExpressionShowcaseModule>(c => c.PickSite(SiteName.Bravo, default!, default), text: "Site = Bravo"),
                    ToInlineButton<ExpressionShowcaseModule>(c => c.PickSite(SiteName.Charlie, default!, default), text: "Site = Charlie"),
                },
                // (2) (int)enum cast — must render "2", not "Bravo".
                new[]
                {
                    ToInlineButton<ExpressionShowcaseModule>(c => c.PickSiteAsInt((int)scalar, default!, default), text: "(int)Bravo → 2"),
                },
                // (3) Flags enum — must render numerically.
                new[]
                {
                    ToInlineButton<ExpressionShowcaseModule>(c => c.TogglePerm(Permission.Read, default!, default), text: "^ Read (1)"),
                    ToInlineButton<ExpressionShowcaseModule>(c => c.TogglePerm(Permission.Write, default!, default), text: "^ Write (2)"),
                    ToInlineButton<ExpressionShowcaseModule>(c => c.TogglePerm(Permission.Execute, default!, default), text: "^ Execute (4)"),
                },
                new[]
                {
                    ToInlineButton<ExpressionShowcaseModule>(c => c.TogglePerm(flags, default!, default), text: "^ Read|Write (3)"),
                },
                // (4) Explicit operator long — must unwrap to 98765.
                new[]
                {
                    ToInlineButton<ExpressionShowcaseModule>(c => c.PickOrder((long)order, default!, default), text: "(long)OrderId(98765)"),
                },
                // (5) Implicit operator Guid — wrapper passed as-is, conversion fires implicitly.
                new[]
                {
                    ToInlineButton<ExpressionShowcaseModule>(c => c.PickDoc(doc, default!, default), text: "DocumentRef → Guid"),
                },
                new[]
                {
                    ToInlineButton<MainMenuModule>(c => c.Home(default!, default)),
                },
            };
            return new InlineKeyboardMarkup(rows);
        }
    }
}
