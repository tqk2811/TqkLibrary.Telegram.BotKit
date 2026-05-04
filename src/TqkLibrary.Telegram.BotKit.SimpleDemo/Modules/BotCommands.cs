namespace TqkLibrary.Telegram.BotKit.SimpleDemo.Modules
{
    public class BotCommands : CommandModule
    {
        readonly DemoChatState _state;
        readonly IStringLocalizer _l;

        public BotCommands(DemoChatState state, IStringLocalizer localizer)
        {
            _state = state;
            _l = localizer;
        }

        [TelegramCommand("start", order: 0, DescriptionResourceName = "DescStart")]
        public async Task Start(Message message, CancellationToken cancellationToken)
        {
            Logger.LogInformation("/start ChatId={ChatId} UserId={UserId}", ChatId, TelegramUserId);

            _state.PendingInputKey = null;
            _state.EchoPromptMessageId = null;

            await MainMenuModule.SendMainMenuAsync(Bot, ChatId, Registry, _l, cancellationToken);
        }

        [TelegramCommand("help", order: 1, DescriptionResourceName = "DescHelp")]
        public async Task Help(Message message, CancellationToken cancellationToken)
        {
            await Bot.SendMessage(ChatId, _l["HelpText"], cancellationToken: cancellationToken);
        }

        [TelegramCommand("echo", order: 2, DescriptionResourceName = "DescEcho")]
        public async Task Echo(Message message, [CommandArg] string? args, CancellationToken cancellationToken)
        {
            string reply = string.IsNullOrWhiteSpace(args)
                ? _l["EchoNoText"]
                : _l["EchoReply", args];

            await Bot.SendMessage(ChatId, reply, cancellationToken: cancellationToken);
        }

        [TelegramCommand("lang", order: 3, DescriptionResourceName = "DescLang")]
        public async Task Lang(Message message, CancellationToken cancellationToken)
        {
            await LanguageModule.SendLanguagePickerAsync(Bot, ChatId, Registry, _l, cancellationToken);
        }
    }
}
