using System.Globalization;
using System.Resources;

namespace TqkLibrary.Telegram.BotKit.Tests;

// Fake resx-generated class: the library locates a "ResourceManager" property via reflection.
public static class TestResources
{
    public static ResourceManager ResourceManager { get; } = new FakeResourceManager(new()
    {
        [("Approve", "")] = "Approve",
        [("Approve", "vi")] = "Chấp thuận",
        [("StartDesc", "")] = "Begin",
        [("StartDesc", "vi")] = "Bắt đầu",
    });
}

sealed class FakeResourceManager(Dictionary<(string Name, string Culture), string> map) : ResourceManager
{
    public override string? GetString(string name, CultureInfo? culture)
    {
        string cultureName = culture?.Name ?? "";
        // Walk up culture parents → empty (InvariantCulture).
        for (CultureInfo? c = culture; c is not null; c = c.Parent == c ? null : c.Parent)
        {
            if (map.TryGetValue((name, c.Name), out string? v)) return v;
            if (c.Parent == c) break;
        }
        return map.TryGetValue((name, ""), out string? fallback) ? fallback : null;
    }

    public override string? GetString(string name) => GetString(name, CultureInfo.CurrentUICulture);
}

// ── Fixture modules using the resx-like class ──────────────────────

public class LocalizedCommand : CommandModule
{
    [TelegramCommand("start", order: 0,
        DescriptionResourceType = typeof(TestResources),
        DescriptionResourceName = "StartDesc")]
    public Task Start(Message message, CancellationToken ct) => Task.CompletedTask;

    [TelegramCommand("fallback", order: 1, Description = "LiteralDesc")]
    public Task Fallback(Message message, CancellationToken ct) => Task.CompletedTask;

    [TelegramCommand("hidden", order: 2)]
    public Task Hidden(Message message, CancellationToken ct) => Task.CompletedTask;
}

public class LocalizedInlineModule : CallbackModule
{
    [InlineButton("loc|{id:guid}",
        TitleResourceType = typeof(TestResources),
        TitleResourceName = "Approve")]
    public Task Resource(Guid id, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;

    [InlineButton("loc2|{id:guid}", Title = "LiteralOnly")]
    public Task Literal(Guid id, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;

    [InlineButton("loc3|{id:guid}")]
    public Task NoTitle(Guid id, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;
}

[TestClass]
public class LocalizedTextTests
{
    [TestMethod]
    public void BotCommands_UseResourceDescription()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(LocalizedCommand));
        var commands = registry.GetBotCommands(CultureInfo.InvariantCulture).ToList();
        Assert.AreEqual(2, commands.Count);
        Assert.AreEqual("start", commands[0].Command);
        Assert.AreEqual("Begin", commands[0].Description);
        Assert.AreEqual("fallback", commands[1].Command);
        Assert.AreEqual("LiteralDesc", commands[1].Description);
    }

    [TestMethod]
    public void BotCommands_RespectCulture()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(LocalizedCommand));
        var vi = registry.GetBotCommands(new CultureInfo("vi")).ToList();
        Assert.AreEqual("Bắt đầu", vi[0].Description);
    }

    [TestMethod]
    public void BotCommands_HiddenCommand_Excluded()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(LocalizedCommand));
        Assert.IsFalse(registry.GetBotCommands().Any(c => c.Command == "hidden"));
        // But it's still routable:
        Assert.IsNotNull(registry.FindCommand("hidden"));
    }

    [TestMethod]
    public void InlineButton_ResolveTitleFromResource()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(LocalizedInlineModule));
        Guid id = Guid.NewGuid();
        var btn = registry.ToInlineButton<LocalizedInlineModule>(
            c => c.Resource(id, default!, default),
            culture: CultureInfo.InvariantCulture);
        Assert.AreEqual("Approve", btn.Text);
        Assert.AreEqual($"loc|{id:D}", btn.CallbackData);
    }

    [TestMethod]
    public void InlineButton_ResolveTitleFromResource_WithCulture()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(LocalizedInlineModule));
        Guid id = Guid.NewGuid();
        var btn = registry.ToInlineButton<LocalizedInlineModule>(
            c => c.Resource(id, default!, default),
            culture: new CultureInfo("vi"));
        Assert.AreEqual("Chấp thuận", btn.Text);
    }

    [TestMethod]
    public void InlineButton_FallsBackToTitleLiteral()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(LocalizedInlineModule));
        Guid id = Guid.NewGuid();
        var btn = registry.ToInlineButton<LocalizedInlineModule>(c => c.Literal(id, default!, default));
        Assert.AreEqual("LiteralOnly", btn.Text);
    }

    [TestMethod]
    public void InlineButton_CallSiteOverridesAttribute()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(LocalizedInlineModule));
        Guid id = Guid.NewGuid();
        var btn = registry.ToInlineButton<LocalizedInlineModule>(
            c => c.Literal(id, default!, default), "CallSite");
        Assert.AreEqual("CallSite", btn.Text);
    }

    [TestMethod]
    public void InlineButton_NoTitleAnywhere_FallsBackToMethodName()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(LocalizedInlineModule));
        Guid id = Guid.NewGuid();
        var btn = registry.ToInlineButton<LocalizedInlineModule>(
            c => c.NoTitle(id, default!, default));
        Assert.AreEqual(nameof(LocalizedInlineModule.NoTitle), btn.Text);
    }
}
