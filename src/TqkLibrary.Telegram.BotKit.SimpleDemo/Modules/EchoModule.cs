namespace TqkLibrary.Telegram.BotKit.SimpleDemo.Modules
{
    [CallbackPrefix("echo")]
    public class EchoModule : CallbackModule
    {
        const string PendingInputKey = "echo:wait_text";

        readonly DemoChatState _state;
        readonly IStringLocalizer _l;

        public EchoModule(DemoChatState state, IStringLocalizer localizer)
        {
            _state = state;
            _l = localizer;
        }

        [InlineButton("start", TitleResourceName = "BtnEchoStart")]
        public async Task Start(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            await Bot.EditMessageText(
                ChatId,
                callbackQuery.Message!.MessageId,
                _l["EchoPrompt"],
                replyMarkup: BuildCancelKeyboard(),
                cancellationToken: cancellationToken);

            _state.EchoPromptMessageId = callbackQuery.Message.MessageId;
            _state.PendingInputKey = PendingInputKey;
        }

        [InlineButton("cancel", TitleResourceName = "BtnEchoCancel")]
        public async Task Cancel(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            _state.PendingInputKey = null;
            _state.EchoPromptMessageId = null;

            await Bot.EditMessageText(
                ChatId,
                callbackQuery.Message!.MessageId,
                _l["EchoCancelled"],
                replyMarkup: BuildBackKeyboard(),
                cancellationToken: cancellationToken);
        }

        [OnUserInput(PendingInputKey)]
        public async Task OnUserText(Message message, CancellationToken cancellationToken)
        {
            string? text = message.Text;
            _state.PendingInputKey = null;
            _state.EchoPromptMessageId = null;

            _state.EchoCount++;
            _state.LastEchoText = text;

            await Bot.SendMessage(
                ChatId,
                _l["EchoReply", text ?? string.Empty],
                replyMarkup: BuildBackKeyboard(),
                cancellationToken: cancellationToken);
        }

        InlineKeyboardMarkup BuildCancelKeyboard()
            => new(new[]
            {
                new[]
                {
                    ToInlineButton<EchoModule>(c => c.Cancel(default!, default)),
                },
            });

        InlineKeyboardMarkup BuildBackKeyboard()
            => new(new[]
            {
                new[]
                {
                    ToInlineButton<MainMenuModule>(c => c.Home(default!, default)),
                },
            });
    }
}
