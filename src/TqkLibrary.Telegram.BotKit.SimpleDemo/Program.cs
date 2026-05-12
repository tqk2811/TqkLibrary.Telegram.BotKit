using System.Globalization;
using Microsoft.Extensions.Hosting;
using TqkLibrary.Telegram.BotKit.SimpleDemo.Middleware;
using TqkLibrary.Telegram.BotKit.SimpleDemo.Modules;

namespace TqkLibrary.Telegram.BotKit.SimpleDemo
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            string? botToken = args.FirstOrDefault() ?? Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN_DEMO");
            if (string.IsNullOrWhiteSpace(botToken))
            {
                Console.Error.WriteLine("Please provide BOT_TOKEN as the first argument or via the TELEGRAM_BOT_TOKEN_DEMO environment variable.");
                Console.Error.WriteLine("Example: dotnet run -- 123456:ABC...");
                return 1;
            }

            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(o =>
            {
                o.TimestampFormat = "HH:mm:ss ";
                o.SingleLine = true;
            });
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            // Demo runs in long-polling mode via AddAndStartPollingAsync below.
            builder.Services.AddTelegramBotKit(options =>
            {
                options.AddCommand<BotCommands>();
                options.AddModulesFromAssemblyOf<MainMenuModule>();

                // ---- Middleware pipeline ---------------------------------------------
                // Registered in OUTSIDE-IN order: the first Use/UseMiddleware sees every
                // later stage and the terminal dispatch. Two canonical uses:
                //
                //   1) Exception handler — wraps next(ctx) in try/catch.
                //   2) Gate — short-circuits by NOT calling next(ctx).
                //
                // Class-based middleware is resolved per-update from the scoped provider
                // (so it can declare scoped deps). Register first so it catches everything
                // downstream, including gate-thrown exceptions.
                options.UseMiddleware<ExceptionLoggingMiddleware>();

                // Functional middleware: keep this bot 1-1 only. If somehow added to a
                // group, leave immediately and short-circuit — no handler will run.
                options.Use(async (ctx, next) =>
                {
                    if (ctx.Message is { Chat.Type: var t and not ChatType.Private })
                    {
                        ctx.Logger.LogWarning(
                            "Bot {BotId}: added to chat type={ChatType} (chat={ChatId}) — leaving",
                            ctx.BotId, t, ctx.ChatId);
                        try { await ctx.Bot.LeaveChat(ctx.ChatId, ctx.CancellationToken); }
                        catch (Exception ex)
                        {
                            ctx.Logger.LogWarning(ex, "LeaveChat {ChatId} failed", ctx.ChatId);
                        }
                        return; // short-circuit — terminal dispatch never runs.
                    }
                    await next(ctx);
                });
            });
            // Single state class for the whole bot. DemoChatState : BotKitChatStateBase, so the
            // framework auto-binds the routing/language ports to the base properties — no Map
            // lambdas needed. Writes happen in module code via _state.PendingInputKey = …
            // / _state.Language = …. Everything else (EchoCount, LastEchoText, …) is pure
            // application data the framework never touches.
            //
            // For DB-backed loading swap to:
            //   builder.Services.AddBotKitChatState<DemoChatState>(async (sp, ct) =>
            //   {
            //       var ctx = sp.GetRequiredService<IUpdateContext>();
            //       var db  = sp.GetRequiredService<MyDbContext>();
            //       return await db.ChatStates.FindAsync([ctx.BotId, ctx.ChatId], ct)
            //           ?? new DemoChatState();
            //   });
            builder.Services.AddBotKitChatState<DemoChatState>();

            // Localization: IStringLocalizer<DemoStrings> reads Resources/DemoStrings(.{culture}).resx.
            // SDK default for resx without a paired .Designer.cs: invariant manifest is
            // "{RootNamespace}.{filename}.resources" in the main assembly, vi resx becomes
            // satellite "vi/{asm}.resources.dll" with manifest "{RootNamespace}.{filename}.vi.resources" —
            // exactly what ResourceManager probes. No ResourcesPath here so the localizer factory
            // composes the same invariant base name ("{rootNamespace}.{trimmed type name}").
            //
            // The dispatcher applies CurrentUICulture per update from ICultureProvider, so the
            // localizer automatically picks the right resx for each handler invocation.
            builder.Services.AddLocalization();
            // Demo only ships one resource set, so a single non-generic IStringLocalizer is enough —
            // modules inject IStringLocalizer instead of IStringLocalizer<DemoStrings>. Bound here
            // to typeof(DemoStrings) which feeds LocalizedTextResolver/ResourceManager the same
            // base name as the open-generic registration would have produced.
            builder.Services.AddSingleton<IStringLocalizer>(sp =>
                sp.GetRequiredService<IStringLocalizerFactory>().Create(typeof(DemoStrings)));
            //or builder.Services.AddJsonLocalization from Askmethat.Aspnet.JsonLocalizer
            // ICultureProvider auto-registered as scoped by AddTelegramBotKit — override via
            // TryAddScoped<ICultureProvider, MyProvider>() if you need custom resolution.

            using IHost host = builder.Build();
            await host.StartAsync();

            TelegramBotHostCollection collection = host.Services.GetRequiredService<TelegramBotHostCollection>();
            ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();

            TelegramBotHost botHost;
            try
            {
                botHost = await collection.AddAndStartPollingAsync(botToken, host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);

                // For webhook mode (requires a public HTTPS endpoint), use AddAndStartWebhookAsync
                // instead. The full webhook URL is built as "{baseUrl}/{path}", where {path} is
                // derived from the token by IBotWebhookPathResolver (default = SHA-256 hex). The
                // same path doubles as Telegram's secret_token for request authenticity.
                //
                //   botHost = await collection.AddAndStartWebhookAsync(
                //       botToken,
                //       "https://your-public-host.example.com/api/tg",
                //       host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
                //
                // Then forward incoming webhook requests from your ASP.NET endpoint:
                //
                //   app.MapPost("/api/tg/{path}", async (string path, Update update, CancellationToken ct) =>
                //       await collection.HandleWebhookUpdateAsync(path, update, ct));
                //
                // To enable on-demand auto-start (resolve token via IBotTokenResolver when the
                // webhook arrives for a bot that isn't running), pass the base URL too:
                //
                //   await collection.HandleWebhookUpdateAsync(
                //       path, "https://your-public-host.example.com/api/tg", update, ct);
                //
                // To customize how the path is derived, register your own implementation BEFORE
                // AddTelegramBotKit (DI uses TryAddSingleton, so first registration wins):
                //
                //   builder.Services.AddSingleton<IBotWebhookPathResolver, MyShortHashResolver>();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start bot. Please verify BOT_TOKEN.");
                return 2;
            }

            // Default-scope English menu — fallback when the user's Telegram client locale
            // does not match any per-language menu we publish below. Descriptions come from
            // [TelegramCommand(DescriptionResourceType=typeof(DemoStrings), DescriptionResourceName=...)]
            // resolved through ResourceManager with the requested culture.
            await botHost.ClearCommandsAsync();
            await botHost.SetCommandsAsync<BotCommands>();
            // One per-language menu so clients with a matching locale see localized descriptions.
            // foreach ((string code, _) in LanguageModule.SupportedLanguages)
            //     await botHost.SetCommandsAsync<BotCommands>(
            //         culture: CultureInfo.GetCultureInfo(code),
            //         languageCode: code);

            logger.LogInformation("Bot {BotId} is running in polling mode. Press Ctrl+C to stop.", botHost.BotId);

            await host.WaitForShutdownAsync();
            await collection.DisposeAsync();
            return 0;
        }
    }
}
