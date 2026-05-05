namespace TqkLibrary.Telegram.BotKit.Tests;

public enum FixtureSiteName
{
    Alpha = 1,
    Bravo = 2,
    Charlie = 3,
}

public enum FixtureLongEnum
{
    AReasonablyLongMemberNameThatTakesSpace = 1,
}

[Flags]
public enum FixturePerm
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
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

    [InlineButton("ef|e|{site}")]
    public Task PickByEnum(FixtureSiteName site, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;

    // Long literal so that any name-form enum render trips the 64-byte limit and forces the numeric fallback.
    [InlineButton("ef|long_literal_padding_to_force_overflow|{site}")]
    public Task PickLong(FixtureLongEnum site, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;

    [InlineButton("ef|p|{perm}")]
    public Task PickPerm(FixturePerm perm, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;
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

    [TestMethod]
    public void BuildCallbackData_EnumDirect_RendersMemberName()
    {
        // Enum-typed method param + raw enum arg → readable name form.
        string data = Registry.BuildCallbackData<ExpressionFixtureModule>(
            c => c.PickByEnum(FixtureSiteName.Charlie, default!, default));
        Assert.AreEqual("ef|e|Charlie", data);
    }

    [TestMethod]
    public void BuildCallbackData_EnumDirect_RoundTripMatches()
    {
        string data = Registry.BuildCallbackData<ExpressionFixtureModule>(
            c => c.PickByEnum(FixtureSiteName.Alpha, default!, default));
        var match = Registry.MatchInlineButton(data);
        Assert.IsNotNull(match);
        Assert.AreEqual(nameof(ExpressionFixtureModule.PickByEnum), match.Value.descriptor.Method.Name);
        Assert.AreEqual("Alpha", match.Value.values["site"]);
    }

    [TestMethod]
    public void BuildCallbackData_EnumNameTooLong_FallsBackToNumeric()
    {
        // Name form would push the callback past the 64-byte limit; render must fall back to the numeric form.
        string data = Registry.BuildCallbackData<ExpressionFixtureModule>(
            c => c.PickLong(FixtureLongEnum.AReasonablyLongMemberNameThatTakesSpace, default!, default));
        Assert.AreEqual("ef|long_literal_padding_to_force_overflow|1", data);
        var match = Registry.MatchInlineButton(data);
        Assert.IsNotNull(match);
        Assert.AreEqual(nameof(ExpressionFixtureModule.PickLong), match.Value.descriptor.Method.Name);
    }

    // ── Flags enum ───────────────────────────────────────────────────────

    [TestMethod]
    public void BuildCallbackData_FlagsEnum_AlwaysRendersNumeric()
    {
        // Combined flags would otherwise stringify as "Read, Write" — wasted bytes + whitespace risk.
        string data = Registry.BuildCallbackData<ExpressionFixtureModule>(
            c => c.PickPerm(FixturePerm.Read | FixturePerm.Write, default!, default));
        Assert.AreEqual("ef|p|3", data);
    }

    [TestMethod]
    public void BuildCallbackData_FlagsEnum_SingleFlag_RendersNumeric()
    {
        string data = Registry.BuildCallbackData<ExpressionFixtureModule>(
            c => c.PickPerm(FixturePerm.Execute, default!, default));
        Assert.AreEqual("ef|p|4", data);
    }

    [TestMethod]
    public void BuildCallbackData_FlagsEnum_RoundTrip_NumericCombined()
    {
        // Plain numeric matching for combined flags must succeed (Enum.IsDefined would say no — Flags semantic overrides).
        string data = Registry.BuildCallbackData<ExpressionFixtureModule>(
            c => c.PickPerm(FixturePerm.Read | FixturePerm.Execute, default!, default));
        var match = Registry.MatchInlineButton(data);
        Assert.IsNotNull(match);
        Assert.AreEqual(nameof(ExpressionFixtureModule.PickPerm), match.Value.descriptor.Method.Name);
        Assert.AreEqual("5", match.Value.values["perm"]);
    }

    [TestMethod]
    public void BuildCallbackData_FlagsEnum_RoundTrip_NameForm()
    {
        // Manual injection of name form (e.g. older clients or hand-crafted callbacks): must still match.
        var match = Registry.MatchInlineButton("ef|p|Read,Write");
        Assert.IsNotNull(match);
        Assert.AreEqual(nameof(ExpressionFixtureModule.PickPerm), match.Value.descriptor.Method.Name);
        Assert.AreEqual("Read,Write", match.Value.values["perm"]);
    }
}
