using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TqkLibrary.Telegram.BotKit.Binding;

namespace TqkLibrary.Telegram.BotKit.Tests;

/// <summary>
/// Test <see cref="ModuleActionInvoker"/> — binds params and invokes the action
/// without requiring network or a real TelegramBotClient.
/// </summary>
public class InvokerCaptureService
{
    public readonly List<object?> Captured = new();
}

public class InvokerFixtureModule(InvokerCaptureService capture) : CallbackModule
{
    [InlineButton("inv|{id:guid}|{n:int}")]
    public Task Act(Guid id, int n, CallbackQuery cb, CancellationToken ct)
    {
        capture.Captured.Add(id);
        capture.Captured.Add(n);
        capture.Captured.Add(cb);
        capture.Captured.Add(ct);
        capture.Captured.Add(ChatId);
        capture.Captured.Add(BotToken);
        return Task.CompletedTask;
    }

    [OnUserInput("ctx-user-state")]
    public Task OnLast(Message message, ModuleContext ctx)
    {
        capture.Captured.Add(message);
        capture.Captured.Add(ctx);
        return Task.CompletedTask;
    }
}

public class InvokerFixtureCommand(InvokerCaptureService capture) : CommandModule
{
    [TelegramCommand("cmd")]
    public Task Cmd(Message message, CancellationToken ct)
    {
        capture.Captured.Add(message);
        capture.Captured.Add(ct);
        return Task.CompletedTask;
    }

    [TelegramCommand("greet")]
    public Task Greet([CommandArg] string? payload, CancellationToken ct)
    {
        capture.Captured.Add(payload);
        return Task.CompletedTask;
    }
}

// Multi-type constraint + object param: invoker picks the candidate that parses.
[CallbackPrefix("multi")]
public class MultiTypeInvokerModule(InvokerCaptureService capture) : CallbackModule
{
    [InlineButton("{a:guid|int}")]
    public Task Act(object a, CallbackQuery cb, CancellationToken ct)
    {
        capture.Captured.Add(a);
        return Task.CompletedTask;
    }
}

[TestClass]
public class DispatcherTests
{
    static (IServiceScope scope, InvokerCaptureService capture, ModuleActionRegistry registry) Build()
    {
        var services = new ServiceCollection();
        services.AddScoped<InvokerCaptureService>();
        services.AddScoped<InvokerFixtureModule>();
        services.AddScoped<InvokerFixtureCommand>();
        services.AddScoped<MultiTypeInvokerModule>();
        var registry = ModuleActionRegistry.ForTests(
            typeof(InvokerFixtureModule),
            typeof(InvokerFixtureCommand),
            typeof(MultiTypeInvokerModule));
        services.AddSingleton(registry);
        IServiceProvider sp = services.BuildServiceProvider();
        IServiceScope scope = sp.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<InvokerCaptureService>(), registry);
    }

    static ModuleContext MakeContext(IServiceProvider sp) => new()
    {
        ServiceProvider = sp,
        Bot = null!,
        BotToken = "fake-token",
        BotId = 123,
        ChatId = 555,
        TelegramUserId = 777,
        Logger = NullLogger.Instance,
    };

    [TestMethod]
    public async Task InvokesInlineAction_WithBoundRouteAndContext()
    {
        var (scope, capture, registry) = Build();
        using var _ = scope;
        IServiceProvider sp = scope.ServiceProvider;
        Guid id = Guid.NewGuid();
        var match = registry.MatchInlineButton($"inv|{id:D}|42");
        Assert.IsNotNull(match);

        var ctx = MakeContext(sp);
        var cb = new CallbackQuery { Id = "cb1" };
        var updCtx = new UpdateContext
        {
            Module = ctx,
            Update = new Update { CallbackQuery = cb },
            UpdateType = UpdateType.CallbackQuery,
            CallbackQuery = cb,
            RouteValues = match.Value.values,
            CancellationToken = CancellationToken.None,
        };
        await ModuleActionInvoker.InvokeAsync(match.Value.descriptor, sp, updCtx);

        Assert.AreEqual(id, capture.Captured[0]);
        Assert.AreEqual(42, capture.Captured[1]);
        Assert.AreSame(cb, capture.Captured[2]);
        Assert.AreEqual(CancellationToken.None, capture.Captured[3]);
        Assert.AreEqual(555L, capture.Captured[4]);
        Assert.AreEqual("fake-token", capture.Captured[5]);
    }

    [TestMethod]
    public async Task InvokesCommand_WithMessageAndCancellationToken()
    {
        var (scope, capture, registry) = Build();
        using var _ = scope;
        IServiceProvider sp = scope.ServiceProvider;
        var desc = registry.FindCommand("cmd");
        Assert.IsNotNull(desc);

        var ctx = MakeContext(sp);
        var msg = new Message { Id = 7 };
        var updCtx = new UpdateContext
        {
            Module = ctx,
            Update = new Update { Message = msg },
            UpdateType = UpdateType.Message,
            Message = msg,
            CancellationToken = CancellationToken.None,
        };
        await ModuleActionInvoker.InvokeAsync(desc!, sp, updCtx);
        Assert.AreSame(msg, capture.Captured[0]);
    }

    [TestMethod]
    public async Task InvokesMultiType_WithObjectParam_PassesParsedGuid()
    {
        var (scope, capture, registry) = Build();
        using var _ = scope;
        IServiceProvider sp = scope.ServiceProvider;
        Guid g = Guid.NewGuid();
        var match = registry.MatchInlineButton($"multi/{g:D}");
        Assert.IsNotNull(match);

        var ctx = MakeContext(sp);
        var cb = new CallbackQuery { Id = "cb2" };
        var updCtx = new UpdateContext
        {
            Module = ctx,
            Update = new Update { CallbackQuery = cb },
            UpdateType = UpdateType.CallbackQuery,
            CallbackQuery = cb,
            RouteValues = match.Value.values,
            CancellationToken = CancellationToken.None,
        };
        await ModuleActionInvoker.InvokeAsync(match.Value.descriptor, sp, updCtx);
        Assert.AreEqual(g, capture.Captured[0]);
    }

    [TestMethod]
    public async Task InvokesMultiType_WithObjectParam_PassesParsedInt()
    {
        var (scope, capture, registry) = Build();
        using var _ = scope;
        IServiceProvider sp = scope.ServiceProvider;
        var match = registry.MatchInlineButton("multi/42");
        Assert.IsNotNull(match);

        var ctx = MakeContext(sp);
        var cb = new CallbackQuery { Id = "cb3" };
        var updCtx = new UpdateContext
        {
            Module = ctx,
            Update = new Update { CallbackQuery = cb },
            UpdateType = UpdateType.CallbackQuery,
            CallbackQuery = cb,
            RouteValues = match.Value.values,
            CancellationToken = CancellationToken.None,
        };
        await ModuleActionInvoker.InvokeAsync(match.Value.descriptor, sp, updCtx);
        Assert.AreEqual(42, capture.Captured[0]);
    }

    [TestMethod]
    public async Task InvokesCommand_WithCommandArgPayload()
    {
        var (scope, capture, registry) = Build();
        using var _ = scope;
        IServiceProvider sp = scope.ServiceProvider;
        var desc = registry.FindCommand("greet");
        Assert.IsNotNull(desc);

        var ctx = MakeContext(sp);
        var msg = new Message { Id = 12 };
        var updCtx = new UpdateContext
        {
            Module = ctx,
            Update = new Update { Message = msg },
            UpdateType = UpdateType.Message,
            Message = msg,
            CommandArgs = "ref-abc",
            CancellationToken = CancellationToken.None,
        };
        await ModuleActionInvoker.InvokeAsync(desc!, sp, updCtx);
        Assert.AreEqual("ref-abc", capture.Captured[0]);
    }

    [TestMethod]
    public async Task InvokesCommand_WithoutCommandArg_BindsNull()
    {
        var (scope, capture, registry) = Build();
        using var _ = scope;
        IServiceProvider sp = scope.ServiceProvider;
        var desc = registry.FindCommand("greet");
        Assert.IsNotNull(desc);

        var ctx = MakeContext(sp);
        var msg = new Message { Id = 13 };
        var updCtx = new UpdateContext
        {
            Module = ctx,
            Update = new Update { Message = msg },
            UpdateType = UpdateType.Message,
            Message = msg,
            CommandArgs = null,
            CancellationToken = CancellationToken.None,
        };
        await ModuleActionInvoker.InvokeAsync(desc!, sp, updCtx);
        Assert.IsNull(capture.Captured[0]);
    }

    [TestMethod]
    public async Task InvokesUserInput_BindsMessageAndModuleContext()
    {
        var (scope, capture, registry) = Build();
        using var _ = scope;
        IServiceProvider sp = scope.ServiceProvider;
        var desc = registry.FindUserInputHandler("ctx-user-state");
        Assert.IsNotNull(desc);

        var ctx = MakeContext(sp);
        var msg = new Message { Id = 9 };
        var updCtx = new UpdateContext
        {
            Module = ctx,
            Update = new Update { Message = msg },
            UpdateType = UpdateType.Message,
            Message = msg,
            CancellationToken = CancellationToken.None,
        };
        await ModuleActionInvoker.InvokeAsync(desc!, sp, updCtx);
        Assert.AreSame(msg, capture.Captured[0]);
        Assert.AreSame(ctx, capture.Captured[1]);
    }

    // ── Regex order + StopOnMatch ─────────────────────────────────────

    [TestMethod]
    public void RegexRegistry_SortsByOrderAscending()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(RegexOrderModule));
        var orders = registry.AllRegexes.Select(d => d.RegexOrder).ToArray();
        CollectionAssert.AreEqual(new[] { 1, 5, 10 }, orders);
    }

    [TestMethod]
    public void RegexRegistry_StopOnMatch_FlagPropagated()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(RegexOrderModule));
        // First in sort order = order 1, declared with StopOnMatch=true.
        Assert.IsTrue(registry.AllRegexes[0].RegexStopOnMatch);
        Assert.IsFalse(registry.AllRegexes[1].RegexStopOnMatch);
        Assert.IsFalse(registry.AllRegexes[2].RegexStopOnMatch);
    }
}

public class RegexOrderModule : CallbackModule
{
    [TelegramRegex(@"^a$", order: 10)]
    public Task A(Message m, CancellationToken ct) => Task.CompletedTask;

    [TelegramRegex(@"^b$", order: 1, StopOnMatch = true)]
    public Task B(Message m, CancellationToken ct) => Task.CompletedTask;

    [TelegramRegex(@"^c$", order: 5)]
    public Task C(Message m, CancellationToken ct) => Task.CompletedTask;
}

[TestClass]
public class BotUpdateDispatcherTests
{
    static BotUpdateDispatcher CreateDispatcher()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        IServiceProvider sp = services.BuildServiceProvider();
        var registry = ModuleActionRegistry.ForTests();
        // TelegramBotClient is null because no handler is invoked in this test.
        return new BotUpdateDispatcher(
            bot: null!, botToken: "fake", botId: 0,
            serviceProvider: sp, registry: registry,
            loggerFactory: NullLoggerFactory.Instance);
    }

    [TestMethod]
    public async Task AcquireChatLock_RemovesEntryAfterLastRelease()
    {
        var d = CreateDispatcher();
        IDisposable r = await d.AcquireChatLockAsync(123, default);
        Assert.AreEqual(1, d.ChatLockEntryCount);
        r.Dispose();
        Assert.AreEqual(0, d.ChatLockEntryCount);
    }

    [TestMethod]
    public async Task AcquireChatLock_DifferentChatIds_DoNotBlock()
    {
        var d = CreateDispatcher();
        IDisposable a = await d.AcquireChatLockAsync(123, default);
        IDisposable b = await d.AcquireChatLockAsync(456, default);
        Assert.AreEqual(2, d.ChatLockEntryCount);
        a.Dispose();
        b.Dispose();
        Assert.AreEqual(0, d.ChatLockEntryCount);
    }

    [TestMethod]
    public void ParseCommand_NameOnly_NullArgs()
    {
        BotUpdateDispatcher.ParseCommand("/start", out var name, out var args);
        Assert.AreEqual("start", name);
        Assert.IsNull(args);
    }

    [TestMethod]
    public void ParseCommand_NameWithSinglePayload()
    {
        BotUpdateDispatcher.ParseCommand("/start abc123", out var name, out var args);
        Assert.AreEqual("start", name);
        Assert.AreEqual("abc123", args);
    }

    [TestMethod]
    public void ParseCommand_NameWithMultiTokenArgs()
    {
        BotUpdateDispatcher.ParseCommand("/transfer 100 alice", out var name, out var args);
        Assert.AreEqual("transfer", name);
        Assert.AreEqual("100 alice", args);
    }

    [TestMethod]
    public void ParseCommand_StripsBotMention()
    {
        BotUpdateDispatcher.ParseCommand("/start@MyBot abc", out var name, out var args);
        Assert.AreEqual("start", name);
        Assert.AreEqual("abc", args);
    }

    [TestMethod]
    public void ParseCommand_BotMentionWithoutArgs()
    {
        BotUpdateDispatcher.ParseCommand("/start@MyBot", out var name, out var args);
        Assert.AreEqual("start", name);
        Assert.IsNull(args);
    }

    [TestMethod]
    public void ParseCommand_TrimsLeadingWhitespaceInArgs()
    {
        BotUpdateDispatcher.ParseCommand("/start    abc", out var name, out var args);
        Assert.AreEqual("start", name);
        Assert.AreEqual("abc", args);
    }

    [TestMethod]
    public async Task AcquireChatLock_SerializesConcurrentWaitsForSameChatId()
    {
        var d = CreateDispatcher();
        IDisposable first = await d.AcquireChatLockAsync(999, default);

        bool secondAcquired = false;
        Task<IDisposable> secondTask = Task.Run(async () =>
        {
            IDisposable r = await d.AcquireChatLockAsync(999, default);
            secondAcquired = true;
            return r;
        });

        // Let the task run briefly to prove it still blocks even though the entry has refcount=2.
        await Task.Delay(50);
        Assert.IsFalse(secondAcquired);
        Assert.AreEqual(1, d.ChatLockEntryCount);

        first.Dispose();
        IDisposable second = await secondTask;
        Assert.IsTrue(secondAcquired);

        second.Dispose();
        Assert.AreEqual(0, d.ChatLockEntryCount);
    }

    [TestMethod]
    public async Task AcquireChatLock_RaceBetweenReleaseAndAcquire_StillSerializes()
    {
        // Reproduce the race the loop in AcquireChatLockAsync handles: many threads acquire+release
        // the same chatId in tight succession. The dispatcher must remove the entry when idle
        // yet re-create it cleanly on the next acquire, with no two holders concurrently.
        var d = CreateDispatcher();
        const int iterations = 500;
        const int workers = 8;
        int activeHolders = 0;
        int maxObservedConcurrent = 0;

        async Task Worker()
        {
            for (int i = 0; i < iterations; i++)
            {
                using IDisposable r = await d.AcquireChatLockAsync(42, default);
                int now = Interlocked.Increment(ref activeHolders);
                int prev = Volatile.Read(ref maxObservedConcurrent);
                while (now > prev && Interlocked.CompareExchange(ref maxObservedConcurrent, now, prev) != prev)
                    prev = Volatile.Read(ref maxObservedConcurrent);
                Interlocked.Decrement(ref activeHolders);
            }
        }

        Task[] tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(Worker)).ToArray();
        await Task.WhenAll(tasks);

        Assert.AreEqual(1, maxObservedConcurrent, "Per-chat lock allowed concurrent holders.");
        Assert.AreEqual(0, d.ChatLockEntryCount, "Entry was not cleaned up after last release.");
    }
}
