using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace TqkLibrary.Telegram.BotKit.Tests;

public class StateInherited : BotKitChatStateBase
{
    public int ExtraData { get; set; }
}

public class StateStandalone
{
    public string? CurrentRoute { get; set; }
    public string? Locale { get; set; }
}

[TestClass]
public class ChatStateExtensionsTests
{
    static IServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddScoped<UpdateContextHolder>();
        services.AddScoped<IUpdateContext>(sp => sp.GetRequiredService<UpdateContextHolder>());
        configure(services);
        return services.BuildServiceProvider();
    }

    static IServiceScope CreateScopedUpdate(IServiceProvider sp, long botId = 1, long chatId = 100)
    {
        IServiceScope scope = sp.CreateScope();
        UpdateContextHolder holder = scope.ServiceProvider.GetRequiredService<UpdateContextHolder>();
        holder.BotId = botId;
        holder.ChatId = chatId;
        holder.TelegramUserId = 999;
        holder.BotToken = "fake";
        return scope;
    }

    // ── Accessor wiring ────────────────────────────────────────────────

    [TestMethod]
    public void Inherited_NoMap_AutoBindsBaseProperties()
    {
        IServiceProvider sp = Build(s => s.AddBotKitChatState<StateInherited>());
        using IServiceScope scope = CreateScopedUpdate(sp);
        StateInherited state = scope.ServiceProvider.GetRequiredService<StateInherited>();
        state.PendingInputKey = "wait";
        state.Language = "vi";

        Assert.AreEqual("wait", scope.ServiceProvider.GetRequiredService<IRoutingStateAccessor>().PendingInputKey);
        Assert.AreEqual("vi", scope.ServiceProvider.GetRequiredService<ILanguageStateAccessor>().Language);
    }

    [TestMethod]
    public void Standalone_NoMap_AccessorsNotRegistered()
    {
        IServiceProvider sp = Build(s => s.AddBotKitChatState<StateStandalone>());
        using IServiceScope scope = CreateScopedUpdate(sp);
        Assert.IsNull(scope.ServiceProvider.GetService<IRoutingStateAccessor>());
        Assert.IsNull(scope.ServiceProvider.GetService<ILanguageStateAccessor>());
    }

    [TestMethod]
    public void Standalone_WithMap_BindsToCustomProperties()
    {
        IServiceProvider sp = Build(s => s.AddBotKitChatState<StateStandalone>(opts =>
        {
            opts.MapPendingInputKey(x => x.CurrentRoute);
            opts.MapLanguage(x => x.Locale);
        }));
        using IServiceScope scope = CreateScopedUpdate(sp);
        StateStandalone state = scope.ServiceProvider.GetRequiredService<StateStandalone>();
        state.CurrentRoute = "step2";
        state.Locale = "en-US";

        Assert.AreEqual("step2", scope.ServiceProvider.GetRequiredService<IRoutingStateAccessor>().PendingInputKey);
        Assert.AreEqual("en-US", scope.ServiceProvider.GetRequiredService<ILanguageStateAccessor>().Language);
    }

    [TestMethod]
    public void Inherited_WithMapOverride_PrefersLambda()
    {
        // Map wins over base property even when both are available.
        IServiceProvider sp = Build(s => s.AddBotKitChatState<StateInherited>(opts =>
        {
            opts.MapPendingInputKey(_ => "from-lambda");
            opts.MapLanguage(_ => "fr");
        }));
        using IServiceScope scope = CreateScopedUpdate(sp);
        StateInherited state = scope.ServiceProvider.GetRequiredService<StateInherited>();
        state.PendingInputKey = "from-base"; // ignored — lambda doesn't read this
        state.Language = "vi";

        Assert.AreEqual("from-lambda", scope.ServiceProvider.GetRequiredService<IRoutingStateAccessor>().PendingInputKey);
        Assert.AreEqual("fr", scope.ServiceProvider.GetRequiredService<ILanguageStateAccessor>().Language);
    }

    // ── Sync vs async factory ──────────────────────────────────────────

    [TestMethod]
    public void SyncFactory_LazyConstructsOnFirstResolve()
    {
        int factoryCalls = 0;
        IServiceProvider sp = Build(s => s.AddBotKitChatState(() =>
        {
            factoryCalls++;
            return new StateStandalone();
        }));

        using (IServiceScope scope = CreateScopedUpdate(sp))
        {
            Assert.AreEqual(0, factoryCalls);
            _ = scope.ServiceProvider.GetRequiredService<StateStandalone>();
            Assert.AreEqual(1, factoryCalls);
        }

        // Same chatId in a fresh scope hits the cache, no second factory call.
        using (IServiceScope scope = CreateScopedUpdate(sp))
        {
            _ = scope.ServiceProvider.GetRequiredService<StateStandalone>();
            Assert.AreEqual(1, factoryCalls);
        }
    }

    [TestMethod]
    public async Task AsyncFactory_RequiresBootstrapBeforeResolve()
    {
        IServiceProvider sp = Build(s => s.AddBotKitChatState<StateInherited>(async (provider, ct) =>
        {
            await Task.Yield();
            return new StateInherited { ExtraData = 42, Language = "vi" };
        }));

        using IServiceScope scope = CreateScopedUpdate(sp);

        // Resolving without bootstrap throws — guards against silent missed-load.
        Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<StateInherited>());

        // Bootstrap then resolve.
        foreach (IChatStateBootstrapper b in scope.ServiceProvider.GetServices<IChatStateBootstrapper>())
            await b.EnsureLoadedAsync(default);

        StateInherited state = scope.ServiceProvider.GetRequiredService<StateInherited>();
        Assert.AreEqual(42, state.ExtraData);
        Assert.AreEqual("vi", scope.ServiceProvider.GetRequiredService<ILanguageStateAccessor>().Language);
    }

    [TestMethod]
    public void DefaultCultureProvider_ScopedLifetime_ReadsCurrentChatLanguage()
    {
        // Regression: ICultureProvider was registered as singleton in the demo, capturing
        // ILanguageStateAccessor → DemoChatState from root scope (BotId=0/ChatId=0 garbage),
        // so language switches in real per-chat scopes never propagated. Library now registers
        // ICultureProvider as scoped via TryAddScoped in AddTelegramBotKit; this test mirrors
        // that wiring and verifies the provider sees mutations made in the same scope.
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddScoped<UpdateContextHolder>();
        services.AddScoped<IUpdateContext>(sp => sp.GetRequiredService<UpdateContextHolder>());
        services.AddBotKitChatState<StateInherited>();
        services.AddScoped<ICultureProvider, DefaultCultureProvider>();
        IServiceProvider sp = services.BuildServiceProvider();

        // Update 1 for chat 100: set vi.
        using (IServiceScope scope = CreateScopedUpdate(sp, chatId: 100))
        {
            scope.ServiceProvider.GetRequiredService<StateInherited>().Language = "vi";
            Assert.AreEqual("vi", scope.ServiceProvider.GetRequiredService<ICultureProvider>().GetCulture()?.Name);
        }

        // Update 2 for chat 100: should still see vi (cached state object reused).
        using (IServiceScope scope = CreateScopedUpdate(sp, chatId: 100))
        {
            Assert.AreEqual("vi", scope.ServiceProvider.GetRequiredService<ICultureProvider>().GetCulture()?.Name);
        }

        // Update for chat 200: independent state, no language set → null.
        using (IServiceScope scope = CreateScopedUpdate(sp, chatId: 200))
        {
            Assert.IsNull(scope.ServiceProvider.GetRequiredService<ICultureProvider>().GetCulture());
        }
    }

    [TestMethod]
    public async Task AsyncFactory_RunsOncePerChatId()
    {
        int loaderCalls = 0;
        IServiceProvider sp = Build(s => s.AddBotKitChatState<StateInherited>(async (_, _) =>
        {
            await Task.Yield();
            Interlocked.Increment(ref loaderCalls);
            return new StateInherited();
        }));

        // First update for chat 100 → loader fires.
        using (IServiceScope scope = CreateScopedUpdate(sp, chatId: 100))
        {
            foreach (IChatStateBootstrapper b in scope.ServiceProvider.GetServices<IChatStateBootstrapper>())
                await b.EnsureLoadedAsync(default);
            _ = scope.ServiceProvider.GetRequiredService<StateInherited>();
        }
        Assert.AreEqual(1, loaderCalls);

        // Second update for chat 100 → cache hit, no second load.
        using (IServiceScope scope = CreateScopedUpdate(sp, chatId: 100))
        {
            foreach (IChatStateBootstrapper b in scope.ServiceProvider.GetServices<IChatStateBootstrapper>())
                await b.EnsureLoadedAsync(default);
            _ = scope.ServiceProvider.GetRequiredService<StateInherited>();
        }
        Assert.AreEqual(1, loaderCalls);

        // Different chatId → new entry, loader fires again.
        using (IServiceScope scope = CreateScopedUpdate(sp, chatId: 200))
        {
            foreach (IChatStateBootstrapper b in scope.ServiceProvider.GetServices<IChatStateBootstrapper>())
                await b.EnsureLoadedAsync(default);
            _ = scope.ServiceProvider.GetRequiredService<StateInherited>();
        }
        Assert.AreEqual(2, loaderCalls);
    }
}
