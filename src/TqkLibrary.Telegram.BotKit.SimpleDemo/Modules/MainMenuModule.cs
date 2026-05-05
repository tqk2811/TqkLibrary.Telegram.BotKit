namespace TqkLibrary.Telegram.BotKit.SimpleDemo.Modules
{
    [CallbackPrefix("menu")]
    public class MainMenuModule : CallbackModule
    {
        readonly DemoChatState _state;
        readonly IStringLocalizer _l;

        public MainMenuModule(DemoChatState state, IStringLocalizer localizer)
        {
            _state = state;
            _l = localizer;
        }

        [InlineButton("home", TitleResourceName = "BtnMainMenu")]
        public async Task Home(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            _state.PendingInputKey = null;
            _state.EchoPromptMessageId = null;

            await Bot.EditMessageText(
                ChatId,
                callbackQuery.Message!.MessageId,
                _l["WelcomeText"],
                replyMarkup: BuildMenuKeyboard(Registry, _l),
                cancellationToken: cancellationToken);
        }

        [InlineButton("about", TitleResourceName = "BtnAbout")]
        public async Task About(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            string text = _l["AboutText",
                BotId,
                ChatId,
                System.Globalization.CultureInfo.CurrentUICulture.Name,
                _state.EchoCount,
                _state.LastEchoText ?? "—"];

            InlineKeyboardMarkup markup = new(new[]
            {
                new[]
                {
                    ToInlineButton<MainMenuModule>(c => c.Home(default!, default)),
                },
            });

            await Bot.EditMessageText(
                ChatId,
                callbackQuery.Message!.MessageId,
                text,
                replyMarkup: markup,
                cancellationToken: cancellationToken);
        }

        public static async Task SendMainMenuAsync(
            ITelegramBotClient bot,
            long chatId,
            ModuleActionRegistry registry,
            IStringLocalizer localizer,
            CancellationToken cancellationToken)
        {
            await bot.SendMessage(
                chatId,
                localizer["WelcomeText"],
                replyMarkup: BuildMenuKeyboard(registry, localizer),
                cancellationToken: cancellationToken);
        }

        // Static helper has no handler instance, so the localizer is forwarded explicitly.
        // Button titles come from each target method's [InlineButton(TitleResourceName=...)] —
        // the OpenPicker button has no TitleResourceType, falls back to localizer["BtnLanguage"].
        static InlineKeyboardMarkup BuildMenuKeyboard(ModuleActionRegistry registry, IStringLocalizer l)
            => new(new[]
            {
                new[]
                {
                    registry.ToInlineButton<EchoModule>(c => c.Start(default!, default), localizer: l),
                },
                new[]
                {
                    registry.ToInlineButton<ExpressionShowcaseModule>(c => c.Open(default!, default), localizer: l),
                },
                new[]
                {
                    registry.ToInlineButton<MainMenuModule>(c => c.About(default!, default), localizer: l),
                    registry.ToInlineButton<LanguageModule>(c => c.OpenPicker(default!, default), localizer: l),
                },
            });
    }
}
