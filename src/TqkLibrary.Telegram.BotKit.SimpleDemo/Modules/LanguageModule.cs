using System.Globalization;

namespace TqkLibrary.Telegram.BotKit.SimpleDemo.Modules
{
    [CallbackPrefix("lang")]
    public class LanguageModule : CallbackModule
    {
        // Languages the demo ships translations for. Each entry maps a BCP-47 culture tag
        // to the resource key whose value renders the picker label in that language.
        public static readonly IReadOnlyList<(string Code, string DisplayKey)> SupportedLanguages =
        [
            ("en", "LangDisplayEnglish"),
            ("vi", "LangDisplayVietnamese"),
        ];

        readonly DemoChatState _state;
        readonly IStringLocalizer _l;

        public LanguageModule(DemoChatState state, IStringLocalizer localizer)
        {
            _state = state;
            _l = localizer;
        }

        [InlineButton("open", TitleResourceName = "BtnLanguage")]
        public async Task OpenPicker(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            await Bot.EditMessageText(
                ChatId,
                callbackQuery.Message!.MessageId,
                _l["LangPrompt"],
                replyMarkup: BuildPickerKeyboard(Registry, _l),
                cancellationToken: cancellationToken);
        }

        [InlineButton("set/{code}")]
        public async Task Set(string code, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            string normalized = NormalizeLanguage(code);
            _state.Language = normalized;
            // The dispatcher already applied the previous culture for this update; flip it now so
            // every string we render below comes from the new language's resx.
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(normalized);
            Logger.LogInformation("Language changed: ChatId={ChatId} Lang={Lang}", ChatId, normalized);

            // Refresh the per-chat Telegram client menu in the chosen language.
            // Per-chat scope wins over default + per-language menus, so the user sees their pick
            // regardless of their Telegram client locale.
            var commands = Registry.GetBotCommands(typeof(BotCommands), CultureInfo.CurrentUICulture, _l).ToList();
            await Bot.SetMyCommands(
                commands,
                scope: new BotCommandScopeChat { ChatId = ChatId },
                cancellationToken: cancellationToken);

            string display = _l[GetDisplayKey(normalized)];
            string confirmation = _l["LangChanged", display] + "\n\n" + _l["WelcomeText"];

            await Bot.EditMessageText(
                ChatId,
                callbackQuery.Message!.MessageId,
                confirmation,
                replyMarkup: BuildBackKeyboard(Registry, _l),
                cancellationToken: cancellationToken);
        }

        public static async Task SendLanguagePickerAsync(
            ITelegramBotClient bot,
            long chatId,
            ModuleActionRegistry registry,
            IStringLocalizer localizer,
            CancellationToken cancellationToken)
        {
            await bot.SendMessage(
                chatId,
                localizer["LangPrompt"],
                replyMarkup: BuildPickerKeyboard(registry, localizer),
                cancellationToken: cancellationToken);
        }

        public static string NormalizeLanguage(string? language)
        {
            if (!string.IsNullOrWhiteSpace(language))
            {
                foreach ((string code, _) in SupportedLanguages)
                    if (string.Equals(code, language, StringComparison.OrdinalIgnoreCase))
                        return code;
            }
            return SupportedLanguages[0].Code;
        }

        static string GetDisplayKey(string normalizedCode)
        {
            foreach ((string code, string key) in SupportedLanguages)
                if (code == normalizedCode) return key;
            return SupportedLanguages[0].DisplayKey;
        }

        // Picker rows render the same [InlineButton("set/{code}")] attribute twice with different
        // titles ("English" vs "Tiếng Việt") — title is data-driven (DisplayKey per language), so
        // it can't live on the attribute and must be passed explicitly here.
        // The trailing back button reuses MainMenuModule.Home's auto-localized title (BtnMainMenu).
        static InlineKeyboardMarkup BuildPickerKeyboard(ModuleActionRegistry registry, IStringLocalizer l)
        {
            var rows = SupportedLanguages
                .Select(lang => new[]
                {
                    registry.ToInlineButton<LanguageModule>(
                        c => c.Set(lang.Code, default!, default),
                        text: l[lang.DisplayKey]),
                })
                .ToList();
            rows.Add(new[]
            {
                registry.ToInlineButton<MainMenuModule>(c => c.Home(default!, default), localizer: l),
            });
            return new InlineKeyboardMarkup(rows);
        }

        static InlineKeyboardMarkup BuildBackKeyboard(ModuleActionRegistry registry, IStringLocalizer l)
            => new(new[]
            {
                new[]
                {
                    registry.ToInlineButton<MainMenuModule>(c => c.Home(default!, default), localizer: l),
                },
            });
    }
}
