# TqkLibrary.Telegram.BotKit

A lightweight, attribute-driven framework for building Telegram bots on top of
[Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot). It plugs into
`Microsoft.Extensions.DependencyInjection` and brings ASP.NET-style routing,
per-chat state, and resource-based localization to the `Telegram.Bot` client
without forcing you to write a dispatcher by hand.

## Why

Writing a non-trivial Telegram bot directly against `Telegram.Bot` quickly turns
into a giant `switch` statement over `Update` types, callback strings, and
ad-hoc `Dictionary<chatId, ...>` state. This library replaces that boilerplate
with declarative pieces:

- `[TelegramCommand("start")]` for `/command` handlers.
- `[InlineButton("template")]` + `[CallbackPrefix("...")]` for inline-keyboard
  callback routes (with typed parameter binding).
- `[OnUserInput("key")]` for "wait for the next message" flows.
- `[TelegramRegex("...")]` for plain-text message matching.
- A typed per-chat state class (`BotKitChatStateBase`) cached by
  `IMemoryCache`, used by both your handlers and the framework's routing/i18n.
- `IStringLocalizer`-driven button titles and command descriptions, with culture
  resolved per-update from the chat state.
- A multi-bot host collection so one process can run many bot tokens, in either
  long-polling or webhook mode.

## Status

Pre-release. The public API is still moving. Versioning is driven by GitVersion
(`{Major}.{Minor}.{Patch}.{CommitsSinceVersionSource}`).

## Target frameworks

- `netstandard2.0`
- `net6.0`
- `net8.0`
- `net10.0`

Compiles under `LangVersion=12.0`, `Nullable=enable`.

## Dependencies

- `Telegram.Bot` 22.x
- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.Caching.Memory`
- `Microsoft.Extensions.Localization`

## Installation

NuGet (once published):

```powershell
dotnet add package TqkLibrary.Telegram.BotKit
```

The repository also ships a `NugetPush.ps1` helper and a shared
`CsharpNugetPush` script for local pack/push workflows.

## Quick start

A complete runnable sample lives in
[`src/TqkLibrary.Telegram.BotKit.SimpleDemo`](src/TqkLibrary.Telegram.BotKit.SimpleDemo).
Run it with:

```powershell
dotnet run --project src/TqkLibrary.Telegram.BotKit.SimpleDemo -- <YOUR_BOT_TOKEN>
# or set TELEGRAM_BOT_TOKEN_DEMO and run without args
```

### 1. Register the kit

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTelegramBotKit(options =>
{
    options.AddCommand<BotCommands>();                  // /command handlers
    options.AddModulesFromAssemblyOf<MainMenuModule>(); // CallbackModule scan
});

// Single per-chat state class. Inheriting BotKitChatStateBase lets the
// framework auto-bind PendingInputKey + Language without Map lambdas.
builder.Services.AddBotKitChatState<DemoChatState>();

builder.Services.AddLocalization();
builder.Services.AddSingleton<IStringLocalizer>(sp =>
    sp.GetRequiredService<IStringLocalizerFactory>().Create(typeof(DemoStrings)));
```

### 2. Define commands

```csharp
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
    public async Task Start(Message message, CancellationToken ct)
    {
        await Bot.SendMessage(ChatId, _l["WelcomeText"], cancellationToken: ct);
    }

    [TelegramCommand("echo", order: 2, DescriptionResourceName = "DescEcho")]
    public async Task Echo(Message m, [CommandArg] string? args, CancellationToken ct)
    {
        string reply = string.IsNullOrWhiteSpace(args) ? _l["EchoNoText"]
                                                       : _l["EchoReply", args];
        await Bot.SendMessage(ChatId, reply, cancellationToken: ct);
    }
}
```

`[CommandArg]` captures the text after the command name (e.g. `/echo hello world`
→ `"hello world"`), which is also handy for Telegram deep links
(`https://t.me/yourbot?start=payload`).

### 3. Define callback modules

```csharp
[CallbackPrefix("menu")]
public class MainMenuModule : CallbackModule
{
    [InlineButton("home", TitleResourceName = "BtnMainMenu")]
    public async Task Home(CallbackQuery q, CancellationToken ct) { /* ... */ }

    [InlineButton("about", TitleResourceName = "BtnAbout")]
    public async Task About(CallbackQuery q, CancellationToken ct) { /* ... */ }
}
```

Build keyboards by referencing the handler method directly — the framework
renders the matching callback data and (optionally) a localized button title:

```csharp
InlineKeyboardMarkup markup = new(new[]
{
    new[] { Registry.ToInlineButton<MainMenuModule>(c => c.Home(default!, default), localizer: _l) },
});
```

### 4. Typed route parameters

`[InlineButton]` templates support placeholders with type constraints. Allowed
types: `string`, `int`, `long`, `bool`, `guid` (and union via `|`, e.g.
`{id:guid|int}`). The framework validates worst-case UTF-8 length up front so
you never silently exceed Telegram's 64-byte `callback_data` limit.

```csharp
[CallbackPrefix("lang")]
public class LanguageModule : CallbackModule
{
    [InlineButton("set/{code}")]
    public Task Set(string code, CallbackQuery q, CancellationToken ct) { /* ... */ }
}
```

### 5. "Wait for next message" flows

Set a routing key on the chat-state, decorate the receiver with
`[OnUserInput(key)]`, and the next plain-text message in that chat is delivered
to that handler:

```csharp
const string PendingInputKey = "echo:wait_text";

[InlineButton("start", TitleResourceName = "BtnEchoStart")]
public async Task Start(CallbackQuery q, CancellationToken ct)
{
    _state.PendingInputKey = PendingInputKey;
    // prompt the user...
}

[OnUserInput(PendingInputKey)]
public async Task OnUserText(Message message, CancellationToken ct)
{
    _state.PendingInputKey = null;          // consume
    _state.LastEchoText = message.Text;
    // reply...
}
```

### 6. Regex matching

```csharp
[TelegramRegex(@"^hello\b", order: 0, StopOnMatch = true)]
public Task OnHello(Message m, CancellationToken ct) { /* ... */ }
```

Regex handlers only fire when the text is not a `/command` and no
`PendingInputKey` is pending; matches run in `Order` ascending, with optional
short-circuit via `StopOnMatch`.

### 7. Per-chat state

```csharp
public class DemoChatState : BotKitChatStateBase
{
    public int EchoCount { get; set; }
    public string? LastEchoText { get; set; }
    public int? EchoPromptMessageId { get; set; }
}
```

The state is cached per `(BotId, ChatId)` in `IMemoryCache` with configurable
sliding/absolute expiration. Use the async overload of `AddBotKitChatState` when
you need to hydrate from a database — the dispatcher pre-loads it before the
handler scope is built so handler constructors stay sync:

```csharp
builder.Services.AddBotKitChatState<DemoChatState>(async (sp, ct) =>
{
    var ctx = sp.GetRequiredService<IUpdateContext>();
    var db  = sp.GetRequiredService<MyDbContext>();
    return await db.ChatStates.FindAsync([ctx.BotId, ctx.ChatId], ct)
        ?? new DemoChatState();
});
```

### 8. Localization

`IStringLocalizer` is consumed by:

- `[InlineButton(TitleResourceName = "...")]` for button captions.
- `[TelegramCommand(DescriptionResourceName = "...")]` for the command menu
  shown in the Telegram client.
- Your own handler code (`_l["WelcomeText"]`).

The dispatcher applies `CurrentUICulture` per update from `ICultureProvider`
(default reads `BotKitChatStateBase.Language`), so every resolution runs against
the user's currently selected language. You can publish per-language menus with
`SetCommandsAsync<TCommand>(culture, languageCode)` and per-chat menus with
`scope: new BotCommandScopeChat { ChatId = chatId }`.

## Handler method parameters

Handler methods (`[TelegramCommand]`, `[InlineButton]`, `[OnUserInput]`,
`[TelegramRegex]`) accept three categories of parameters. The framework
binds them per-update — you don't write the wiring.

### Well-known context types

Matched by type, in any order:

| Type | What it is |
|------|-----------|
| `Update` | The full Telegram `Update`. |
| `Message` | The message attached to this update (also set for callbacks that carry a message). |
| `CallbackQuery` | The callback query (only meaningful inside `[InlineButton]` handlers). |
| `UpdateType` | The current update kind. |
| `CancellationToken` | Pipeline cancellation token. |
| `ModuleContext` | Per-update bundle: `ServiceProvider`, `Bot`, `BotToken`, `BotId`, `ChatId`, `TelegramUserId`, `Logger`. |

```csharp
[InlineButton("home")]
public Task Home(CallbackQuery q, ModuleContext ctx, CancellationToken ct) { /* ... */ }
```

### `[CommandArg]` for /command payload

Captures the text after the command name. Must be `string` or `string?`:

```csharp
[TelegramCommand("echo")]
public Task Echo(Message m, [CommandArg] string? args, CancellationToken ct) { /* ... */ }
```

### Route placeholders

Parameters whose name matches an `[InlineButton]` placeholder are filled
from the parsed callback data — see [Typed route parameters](#4-typed-route-parameters).

```csharp
[InlineButton("set/{code}")]
public Task Set(string code, CallbackQuery q, CancellationToken ct) { /* ... */ }
```

### DI services and chat-state

**DI dependencies (including your chat-state class) must be injected through
the module constructor, not as method parameters.** Declaring a service or
chat-state directly on a handler method throws at startup with
`Could not bind parameter ...`.

```csharp
public class MainMenuModule : CallbackModule
{
    readonly DemoChatState _state;
    readonly IStringLocalizer _l;

    public MainMenuModule(DemoChatState state, IStringLocalizer l) // DI here
    {
        _state = state;
        _l = l;
    }

    [InlineButton("home")]
    public Task Home(CallbackQuery q, CancellationToken ct)        // context types only
    {
        // _state and _l are already available
    }
}
```

## Multi-bot hosting

`TelegramBotHostCollection` is registered as a singleton and lets one process
manage many bot tokens:

```csharp
TelegramBotHostCollection bots = host.Services.GetRequiredService<TelegramBotHostCollection>();

TelegramBotHost botHost = await bots.AddAndStartPollingAsync(token, ct);
await botHost.SetCommandsAsync<BotCommands>();
```

### Polling vs. webhook

Mode is chosen per call. Both methods are idempotent — calling them twice with
the same token returns the existing host.

- `AddAndStartPollingAsync(token, ct)` — direct long polling against Telegram.
  No public URL required; ideal for local development.
- `AddAndStartWebhookAsync(token, webhookBaseUrl, ct)` — registers a webhook
  with Telegram. Requires a public HTTPS endpoint.

#### Webhook setup

The full webhook URL is built as `{webhookBaseUrl}/{path}`, where `{path}` is
derived from the bot token by `IBotWebhookPathResolver` (default: lowercase
hex of SHA-256(token), 64 chars). The same `{path}` is sent to Telegram as
the `secret_token`, so the framework can authenticate inbound webhook calls
without you handling that yourself.

```csharp
TelegramBotHost botHost = await bots.AddAndStartWebhookAsync(
    token,
    "https://your-public-host.example.com/api/tg",
    ct);
```

Forward incoming webhook requests from your ASP.NET endpoint. The `{path}`
route value is the same string the resolver produced:

```csharp
app.MapPost("/api/tg/{path}", async (
    string path,
    Update update,
    TelegramBotHostCollection collection,
    CancellationToken ct) =>
{
    await collection.HandleWebhookUpdateAsync(path, update, ct);
    return Results.Ok();
});
```

#### On-demand auto-start

For dynamic environments (bots provisioned outside the process, or many
short-lived bots), use the overload that also takes `webhookBaseUrl` and
register an `IBotTokenResolver`. When a webhook arrives for a bot that isn't
running, the resolver looks up the token by path and the host is started on
the fly:

```csharp
public class DbBotTokenResolver : IBotTokenResolver
{
    readonly MyDbContext _db;
    public DbBotTokenResolver(MyDbContext db) => _db = db;

    public Task<string?> GetBotTokenAsync(string webhookPath, CancellationToken ct)
        => _db.Bots
            .Where(b => b.WebhookPath == webhookPath) // index this column
            .Select(b => b.Token)
            .FirstOrDefaultAsync(ct);
}

builder.Services.AddScoped<IBotTokenResolver, DbBotTokenResolver>();

// In the endpoint:
await collection.HandleWebhookUpdateAsync(
    path, "https://your-public-host.example.com/api/tg", update, ct);
```

When provisioning a new bot, precompute and persist the path with the same
resolver so the lookup is O(1):

```csharp
IBotWebhookPathResolver resolver = sp.GetRequiredService<IBotWebhookPathResolver>();
newBot.WebhookPath = resolver.ResolvePath(newBot.Token);
```

#### Custom path derivation

Register your own `IBotWebhookPathResolver` before `AddTelegramBotKit` (the
default is wired with `TryAddSingleton`, so the first registration wins).
The output must be 1-256 chars from `A-Z a-z 0-9 _ -` (Telegram's
`secret_token` charset):

```csharp
public class ShortHashResolver : IBotWebhookPathResolver
{
    public string ResolvePath(string botToken)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(botToken));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant(); // 16 chars
    }
}

builder.Services.AddSingleton<IBotWebhookPathResolver, ShortHashResolver>();
builder.Services.AddTelegramBotKit(...);
```

## Architecture overview

```
              ┌─────────────────────────────┐
Update ──────▶│  BotUpdateDispatcher        │
              │  - opens DI scope per update│
              │  - sets UpdateContext       │
              │  - applies CurrentUICulture │
              │  - pre-loads chat-state     │
              └──────────────┬──────────────┘
                             │
       ┌─────────────────────┼─────────────────────┐
       ▼                     ▼                     ▼
  CommandModule         CallbackModule        Regex / OnUserInput
  ([TelegramCommand])   ([CallbackPrefix] +    handlers
                         [InlineButton])
                             │
                             ▼
                    ModuleActionRegistry
                    (route table, BotCommand list,
                     ToInlineButton helpers)
```

Key types:

| Type | Role |
|------|------|
| [`TelegramBotHostCollection`](src/TqkLibrary.Telegram.BotKit/TelegramBotHostCollection.cs) | Manages many `TelegramBotHost` instances keyed by token; routes webhooks by path. |
| [`TelegramBotHost`](src/TqkLibrary.Telegram.BotKit/TelegramBotHost.cs) | One bot lifecycle (polling/webhook), `SetCommandsAsync`, dispatch entry. |
| [`BotUpdateDispatcher`](src/TqkLibrary.Telegram.BotKit/BotUpdateDispatcher.cs) | Per-update pipeline: scope, context, culture, routing, handler invocation. |
| [`ModuleActionRegistry`](src/TqkLibrary.Telegram.BotKit/ModuleActionRegistry.cs) | Discovers attributed methods, builds the route table, renders `BotCommand`s and inline buttons. |
| [`RouteTemplate`](src/TqkLibrary.Telegram.BotKit/Routing/RouteTemplate.cs) | Parses, matches, and renders `[InlineButton]` templates within Telegram's 64-byte budget. |
| [`BotKitChatStateBase`](src/TqkLibrary.Telegram.BotKit/Models/BotKitChatStateBase.cs) | Optional base for the user's chat-state class — exposes `PendingInputKey` + `Language` to the framework. |
| [`IUpdateContext`](src/TqkLibrary.Telegram.BotKit/Interfaces/IUpdateContext.cs) | Per-update ambient view (BotId, ChatId, UserId, raw `Update`) injectable into your services. |
| [`ICultureProvider`](src/TqkLibrary.Telegram.BotKit/Interfaces/ICultureProvider.cs) | Resolves the per-update `CultureInfo`; default reads chat-state language. |
| [`IBotWebhookPathResolver`](src/TqkLibrary.Telegram.BotKit/Interfaces/IBotWebhookPathResolver.cs) | Derives the webhook URL path / Telegram `secret_token` from a bot token. Default: SHA-256 hex. |
| [`IBotTokenResolver`](src/TqkLibrary.Telegram.BotKit/Interfaces/IBotTokenResolver.cs) | Optional lookup of `webhookPath → botToken` so `HandleWebhookUpdateAsync` can auto-start unknown bots. |

## Project layout

```
TqkLibrary.Telegram.BotKit/
├── CsharpNugetPush/                 shared NuGet pack/push helpers
├── GitVersion.yml                   GitVersion configuration (loaded by MSBuild)
└── src/
    ├── Directory.Packages.props
    ├── ProjectBuildProperties.targets   shared TargetFrameworks + GitVersion wiring
    ├── TqkLibrary.Telegram.BotKit/      the library
    ├── TqkLibrary.Telegram.BotKit.SimpleDemo/   runnable demo bot
    └── TqkLibrary.Telegram.BotKit.Tests/        unit tests
```

## Building

```powershell
dotnet build src/TqkLibrary.Telegram.BotKit.sln
dotnet test  src/TqkLibrary.Telegram.BotKit.sln
dotnet pack  src/TqkLibrary.Telegram.BotKit/TqkLibrary.Telegram.BotKit.csproj -c Release
```

GitVersion drives the version number automatically; the `.nuspec` next to the
project picks it up via the `SetVersionFromGitVersion` MSBuild target.

## License

MIT. See `LICENSE` (or the `<license>` element in the `.nuspec`).
