namespace TqkLibrary.Telegram.BotKit.Tests;

public enum FixtureSiteName
{
    Alpha = 1,
    Bravo = 2,
    Charlie = 3,
}

public class ExpressionFixtureModule : CallbackModule
{
    [InlineButton("ef|{id:guid}")]
    public Task Approve(Guid id, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;

    [InlineButton("ef|{id:guid}|back")]
    public Task Back(Guid id, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;

    [InlineButton("ef|entry")]
    public Task Entry(CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;

    [InlineButton("ef|g|{site}")]
    public Task PickByInt(int site, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;
}

[TestClass]
public class InlineButtonBuilderTests
{
    static ModuleActionRegistry Registry => _registry ??= ModuleActionRegistry.ForTests(typeof(ExpressionFixtureModule));
    static ModuleActionRegistry? _registry;

    [TestMethod]
    public void BuildCallbackData_PassesScalarArgument()
    {
        Guid id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        string data = Registry.BuildCallbackData<ExpressionFixtureModule>(c => c.Approve(id, default!, default));
        Assert.AreEqual($"ef|{id:D}", data);
    }

    [TestMethod]
    public void BuildCallbackData_WithLiteralTrailingSegment()
    {
        Guid id = Guid.NewGuid();
        string data = Registry.BuildCallbackData<ExpressionFixtureModule>(c => c.Back(id, default!, default));
        Assert.AreEqual($"ef|{id:D}|back", data);
    }

    [TestMethod]
    public void BuildCallbackData_EntryNoArgs()
    {
        string data = Registry.BuildCallbackData<ExpressionFixtureModule>(c => c.Entry(default!, default));
        Assert.AreEqual("ef|entry", data);
    }

    [TestMethod]
    public void ToInlineButton_ProducesButtonWithTextAndCallback()
    {
        Guid id = Guid.NewGuid();
        InlineKeyboardButton btn = Registry.ToInlineButton<ExpressionFixtureModule>(
            c => c.Approve(id, default!, default), "OK");
        Assert.AreEqual("OK", btn.Text);
        Assert.AreEqual($"ef|{id:D}", btn.CallbackData);
    }

    [TestMethod]
    public void ToInlineButton_NoText_NoAttribute_FallsBackToMethodName()
    {
        Guid id = Guid.NewGuid();
        InlineKeyboardButton btn = Registry.ToInlineButton<ExpressionFixtureModule>(
            c => c.Approve(id, default!, default));
        // Approve has no Title/TitleResourceName → falls back to method name.
        Assert.AreEqual(nameof(ExpressionFixtureModule.Approve), btn.Text);
    }

    [TestMethod]
    public void BuildCallbackData_ClosureCapturedVariable()
    {
        Guid id = Guid.NewGuid();
        Func<Guid, string> build = (g) => Registry.BuildCallbackData<ExpressionFixtureModule>(c => c.Approve(g, default!, default));
        Assert.AreEqual($"ef|{id:D}", build(id));
    }

    [TestMethod]
    public void RoundTrip_BuildThenMatch()
    {
        Guid id = Guid.NewGuid();
        string data = Registry.BuildCallbackData<ExpressionFixtureModule>(c => c.Back(id, default!, default));
        var match = Registry.MatchInlineButton(data);
        Assert.IsNotNull(match);
        Assert.AreEqual(nameof(ExpressionFixtureModule.Back), match.Value.descriptor.Method.Name);
        Assert.AreEqual(id.ToString("D"), match.Value.values["id"]);
    }

    [TestMethod]
    public void BuildCallbackData_EnumCastToInt_RendersNumeric()
    {
        // (int)enum in the expression must propagate the cast — otherwise the formatter sees the
        // enum value, renders the member name, and the int-typed route param fails to match.
        FixtureSiteName site = FixtureSiteName.Bravo;
        string data = Registry.BuildCallbackData<ExpressionFixtureModule>(
            c => c.PickByInt((int)site, default!, default));
        Assert.AreEqual("ef|g|2", data);
    }
}
