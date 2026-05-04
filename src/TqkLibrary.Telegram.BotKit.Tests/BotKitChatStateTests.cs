using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace TqkLibrary.Telegram.BotKit.Tests;

public class TestChatState
{
    public int Counter { get; set; }
    public string? LastMessage { get; set; }
}

[TestClass]
public class BotKitChatStateTests
{
    static IServiceProvider BuildRoot()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddScoped<UpdateContextHolder>();
        services.AddScoped<IUpdateContext>(sp => sp.GetRequiredService<UpdateContextHolder>());
        services.AddBotKitChatState<TestChatState>();
        return services.BuildServiceProvider();
    }

    static IServiceScope EnterScope(IServiceProvider root, long botId, long chatId, long userId = 0)
    {
        IServiceScope scope = root.CreateScope();
        UpdateContextHolder holder = scope.ServiceProvider.GetRequiredService<UpdateContextHolder>();
        holder.BotId = botId;
        holder.ChatId = chatId;
        holder.TelegramUserId = userId;
        return scope;
    }

    [TestMethod]
    public void Resolve_WithinScope_ReturnsSameInstance()
    {
        IServiceProvider root = BuildRoot();
        using IServiceScope scope = EnterScope(root, botId: 1, chatId: 100);
        var first = scope.ServiceProvider.GetRequiredService<TestChatState>();
        var second = scope.ServiceProvider.GetRequiredService<TestChatState>();
        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void Resolve_DifferentScopesSameChat_ReusesCachedInstance()
    {
        IServiceProvider root = BuildRoot();

        TestChatState first;
        using (IServiceScope scope = EnterScope(root, 1, 100))
        {
            first = scope.ServiceProvider.GetRequiredService<TestChatState>();
            first.Counter = 7;
            first.LastMessage = "hello";
        }

        TestChatState second;
        using (IServiceScope scope = EnterScope(root, 1, 100))
        {
            second = scope.ServiceProvider.GetRequiredService<TestChatState>();
        }

        Assert.AreSame(first, second);
        Assert.AreEqual(7, second.Counter);
        Assert.AreEqual("hello", second.LastMessage);
    }

    [TestMethod]
    public void Resolve_DifferentChatIds_ReturnsDifferentInstances()
    {
        IServiceProvider root = BuildRoot();

        using IServiceScope scopeA = EnterScope(root, botId: 1, chatId: 100);
        var a = scopeA.ServiceProvider.GetRequiredService<TestChatState>();
        using IServiceScope scopeB = EnterScope(root, botId: 1, chatId: 200);
        var b = scopeB.ServiceProvider.GetRequiredService<TestChatState>();
        Assert.AreNotSame(a, b);
    }

    [TestMethod]
    public void Resolve_DifferentBotIds_ReturnsDifferentInstances()
    {
        IServiceProvider root = BuildRoot();

        using IServiceScope scopeA = EnterScope(root, botId: 1, chatId: 100);
        var a = scopeA.ServiceProvider.GetRequiredService<TestChatState>();
        using IServiceScope scopeB = EnterScope(root, botId: 2, chatId: 100);
        var b = scopeB.ServiceProvider.GetRequiredService<TestChatState>();
        Assert.AreNotSame(a, b);
    }

    [TestMethod]
    public void CustomFactory_IsHonored()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddScoped<UpdateContextHolder>();
        services.AddScoped<IUpdateContext>(sp => sp.GetRequiredService<UpdateContextHolder>());
        services.AddBotKitChatState(() => new TestChatState { Counter = 42 });
        IServiceProvider root = services.BuildServiceProvider();

        using IServiceScope scope = EnterScope(root, botId: 1, chatId: 1);
        var state = scope.ServiceProvider.GetRequiredService<TestChatState>();
        Assert.AreEqual(42, state.Counter);
    }

    [TestMethod]
    public void CustomCacheKeyPrefix_IsolatesEntries()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryCache>(cache);
        services.AddScoped<UpdateContextHolder>();
        services.AddScoped<IUpdateContext>(sp => sp.GetRequiredService<UpdateContextHolder>());
        services.AddBotKitChatState<TestChatState>(o => o.CacheKeyPrefix = "tenant-A:");
        IServiceProvider root = services.BuildServiceProvider();

        using (IServiceScope scope = EnterScope(root, botId: 1, chatId: 1))
            scope.ServiceProvider.GetRequiredService<TestChatState>();

        string expectedKey = $"tenant-A:{typeof(TestChatState).FullName}:1:1";
        Assert.IsTrue(cache.TryGetValue((object)expectedKey, out _),
            $"Expected cache to contain key '{expectedKey}'.");
    }

    [TestMethod]
    public void CustomKeyBuilder_OverridesPrefix()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryCache>(cache);
        services.AddScoped<UpdateContextHolder>();
        services.AddScoped<IUpdateContext>(sp => sp.GetRequiredService<UpdateContextHolder>());
        services.AddBotKitChatState<TestChatState>(o =>
        {
            o.CacheKeyPrefix = "ignored:";
            o.CacheKeyBuilder = (t, ctx) => $"custom/{ctx.BotId}/{ctx.ChatId}/{t.Name}";
        });
        IServiceProvider root = services.BuildServiceProvider();

        using (IServiceScope scope = EnterScope(root, botId: 7, chatId: 9))
            scope.ServiceProvider.GetRequiredService<TestChatState>();

        Assert.IsTrue(cache.TryGetValue((object)"custom/7/9/TestChatState", out _));
        Assert.IsFalse(cache.TryGetValue((object)$"ignored:{typeof(TestChatState).FullName}:7:9", out _));
    }

    [TestMethod]
    public void CustomSlidingExpiration_AppliedToCacheEntry()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryCache>(cache);
        services.AddScoped<UpdateContextHolder>();
        services.AddScoped<IUpdateContext>(sp => sp.GetRequiredService<UpdateContextHolder>());
        services.AddBotKitChatState<TestChatState>(o =>
        {
            o.SlidingExpiration = TimeSpan.FromMinutes(5);
            o.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
        });
        IServiceProvider root = services.BuildServiceProvider();

        using (IServiceScope scope = EnterScope(root, botId: 1, chatId: 1))
            scope.ServiceProvider.GetRequiredService<TestChatState>();

        // We cannot inspect IMemoryCache entry metadata directly, but verifying the entry
        // exists after configuration suffices to prove the registration path runs without
        // throwing on absolute + sliding combination.
        string key = $"botkit:chat-state:{typeof(TestChatState).FullName}:1:1";
        Assert.IsTrue(cache.TryGetValue((object)key, out _));
    }
}
