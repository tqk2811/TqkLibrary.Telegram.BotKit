namespace TqkLibrary.Telegram.BotKit.Tests;

// ── Fixture handlers ───────────────────────────────────────────────────

public class SampleCommand : CommandModule
{
    [TelegramCommand("start", order: 0, Description = "Begin")]
    public Task Start(Message message, CancellationToken ct) => Task.CompletedTask;

    [TelegramCommand("help", order: 10, Description = "Help")]
    [TelegramCommand("h", order: 11)] // alias hidden from menu (no Description)
    public Task Help(Message message, CancellationToken ct) => Task.CompletedTask;
}

public class SampleInlineModule : CallbackModule
{
    [InlineButton("check|{id:guid}")]
    public Task Do(Guid id, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;

    [InlineButton("check|{id:guid}|apv")]
    public Task Approve(Guid id, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;

    [InlineButton("flow|reset")]
    public Task Reset(CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;

    [InlineButton("flow|{key}")]
    public Task Generic(string key, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;
}

[CallbackPrefix("pfx")]
public class SamplePrefixedModule : CallbackModule
{
    [InlineButton("{id:guid}|apv", Title = "OK")]
    public Task Approve(Guid id, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;

    [InlineButton("{id:guid}|rjt", Title = "No")]
    public Task Reject(Guid id, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;
}

public class SampleOnUserInputModule : CallbackModule
{
    [OnUserInput("wait:amount")]
    public Task OnAmount(Message message, CancellationToken ct) => Task.CompletedTask;
}

public class SampleRegexModule : CallbackModule
{
    [TelegramRegex(@"^\d{10,}$")]
    public Task Numeric(Message message, CancellationToken ct) => Task.CompletedTask;
}

public class DuplicateCommand : CommandModule
{
    [TelegramCommand("start")]
    public Task Other(Message message, CancellationToken ct) => Task.CompletedTask;
}

public class UnbindableParameterCommand : CommandModule
{
    [TelegramCommand("bad")]
    public Task Bad(int unknownParam) => Task.CompletedTask;
}

// Invalid: Command attr on Module → must throw at scan.
public class InvalidCommandOnModule : CallbackModule
{
    [TelegramCommand("x")]
    public Task X(Message message, CancellationToken ct) => Task.CompletedTask;
}

// Invalid: Inline attr on Command → must throw at scan.
public class InvalidInlineOnCommand : CommandModule
{
    [InlineButton("x|{id:guid}")]
    public Task X(Guid id, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;
}

// Invalid: CallbackPrefix attr on Command → must throw at scan.
[CallbackPrefix("inv")]
public class InvalidCallbackPrefixOnCommand : CommandModule
{
    [TelegramCommand("y")]
    public Task Y(Message message, CancellationToken ct) => Task.CompletedTask;
}

// Multi-type constraint + object param → invoker tries each candidate.
[CallbackPrefix("mt")]
public class MultiTypeObjectModule : CallbackModule
{
    [InlineButton("{a:guid|int}")]
    public Task Act(object a, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;
}

// Single-type constraint + method param type differs from constraint → throws at registration.
[CallbackPrefix("cf")]
public class ConstraintConflictModule : CallbackModule
{
    [InlineButton("{a:int}")]
    public Task Act(Guid a, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;
}

// No constraint + method param object → type cannot be inferred → throws.
[CallbackPrefix("infer")]
public class InferObjectModule : CallbackModule
{
    [InlineButton("{a}")]
    public Task Act(object a, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;
}

// Multi-type constraint + concrete param → narrow to param type; param outside the set → throws.
[CallbackPrefix("narrow")]
public class NarrowFromMultiTypeModule : CallbackModule
{
    [InlineButton("{a:guid|int}")]
    public Task Act(Guid a, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;
}

[CallbackPrefix("outof")]
public class OutOfSetModule : CallbackModule
{
    [InlineButton("{a:guid|int}")]
    public Task Act(string a, CallbackQuery cb, CancellationToken ct) => Task.CompletedTask;
}

// ── Tests ──────────────────────────────────────────────────────────────

[TestClass]
public class RegistryTests
{
    [TestMethod]
    public void Scan_FindsAllHandlers()
    {
        var registry = ModuleActionRegistry.ForTests(
            typeof(SampleCommand),
            typeof(SampleInlineModule),
            typeof(SampleOnUserInputModule),
            typeof(SampleRegexModule));

        Assert.IsNotNull(registry.FindCommand("start"));
        Assert.IsNotNull(registry.FindCommand("help"));
        Assert.IsNotNull(registry.FindCommand("h"));
        Assert.IsNotNull(registry.FindUserInputHandler("wait:amount"));
        Assert.AreEqual(1, registry.AllRegexes.Count);
        Assert.IsTrue(registry.HandlerTypes.Contains(typeof(SampleCommand)));
    }

    [TestMethod]
    public void BotCommands_FiltersByDescriptionAndSortsByOrder()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(SampleCommand));
        var list = registry.GetBotCommands().Select(c => c.Command).ToList();
        CollectionAssert.AreEqual(new[] { "start", "help" }, list);
        Assert.IsNotNull(registry.FindCommand("h"));
    }

    [TestMethod]
    public void InlineButton_PrefersLiteralBeforePlaceholder()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(SampleInlineModule));
        var m1 = registry.MatchInlineButton("flow|reset");
        Assert.IsNotNull(m1);
        Assert.AreEqual(nameof(SampleInlineModule.Reset), m1.Value.descriptor.Method.Name);

        var m2 = registry.MatchInlineButton("flow|custom");
        Assert.IsNotNull(m2);
        Assert.AreEqual(nameof(SampleInlineModule.Generic), m2.Value.descriptor.Method.Name);
        Assert.AreEqual("custom", m2.Value.values["key"]);
    }

    [TestMethod]
    public void InlineButton_MatchesWithGuidConstraint()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(SampleInlineModule));
        Guid id = Guid.NewGuid();
        var match = registry.MatchInlineButton($"check|{id:D}|apv");
        Assert.IsNotNull(match);
        Assert.AreEqual(nameof(SampleInlineModule.Approve), match.Value.descriptor.Method.Name);
        Assert.AreEqual(id.ToString("D"), match.Value.values["id"]);
    }

    [TestMethod]
    public void InlineButton_UnknownPrefix_ReturnsNull()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(SampleInlineModule));
        Assert.IsNull(registry.MatchInlineButton("unknown|xxx"));
    }

    [TestMethod]
    public void ModulePrefix_ComposesFullTemplate_MatchesCallbackWithSlash()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(SamplePrefixedModule));
        Guid id = Guid.NewGuid();
        var match = registry.MatchInlineButton($"pfx/{id:D}|apv");
        Assert.IsNotNull(match);
        Assert.AreEqual(nameof(SamplePrefixedModule.Approve), match.Value.descriptor.Method.Name);
        Assert.AreEqual(id.ToString("D"), match.Value.values["id"]);
    }

    [TestMethod]
    public void ModulePrefix_PrefixIndexUsesModuleName()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(SamplePrefixedModule));
        Assert.IsNull(registry.MatchInlineButton($"wrong/{Guid.NewGuid():D}|apv"));
    }

    [TestMethod]
    public void DuplicateCommand_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModuleActionRegistry.ForTests(typeof(SampleCommand), typeof(DuplicateCommand)));
    }

    [TestMethod]
    public void UnbindableParameter_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModuleActionRegistry.ForTests(typeof(UnbindableParameterCommand)));
    }

    [TestMethod]
    public void CommandAttrOnModule_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModuleActionRegistry.ForTests(typeof(InvalidCommandOnModule)));
    }

    [TestMethod]
    public void InlineAttrOnCommand_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModuleActionRegistry.ForTests(typeof(InvalidInlineOnCommand)));
    }

    [TestMethod]
    public void CallbackPrefixOnCommand_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModuleActionRegistry.ForTests(typeof(InvalidCallbackPrefixOnCommand)));
    }

    // ── Multi-type constraint & method param resolution ───────────────────

    [TestMethod]
    public void MultiTypeConstraint_WithObjectParam_MatchesEitherType()
    {
        var registry = ModuleActionRegistry.ForTests(typeof(MultiTypeObjectModule));
        Guid g = Guid.NewGuid();
        Assert.IsNotNull(registry.MatchInlineButton($"mt/{g:D}"));
        Assert.IsNotNull(registry.MatchInlineButton("mt/42"));
        Assert.IsNull(registry.MatchInlineButton("mt/abc"));
    }

    [TestMethod]
    public void ConstraintConflict_ThrowsAtRegistration()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModuleActionRegistry.ForTests(typeof(ConstraintConflictModule)));
    }

    [TestMethod]
    public void NoConstraintWithObjectParam_ThrowsAtRegistration()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModuleActionRegistry.ForTests(typeof(InferObjectModule)));
    }

    [TestMethod]
    public void MultiTypeConstraint_NarrowsToConcreteParamType()
    {
        // Method param = Guid, template = {a:guid|int}. After binding, TryMatch only accepts Guid.
        var registry = ModuleActionRegistry.ForTests(typeof(NarrowFromMultiTypeModule));
        Guid g = Guid.NewGuid();
        Assert.IsNotNull(registry.MatchInlineButton($"narrow/{g:D}"));
        Assert.IsNull(registry.MatchInlineButton("narrow/42"));
    }

    [TestMethod]
    public void MultiTypeConstraint_ParamOutOfSet_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModuleActionRegistry.ForTests(typeof(OutOfSetModule)));
    }

    [TestMethod]
    public void NoConstraintConcreteParam_TightensMatchToParamType()
    {
        // {key} + string key (SampleInlineModule.Generic): stays string after binding (matches anything except separator).
        // Guard test against regression: {key} should accept both "reset" and "abc".
        var registry = ModuleActionRegistry.ForTests(typeof(SampleInlineModule));
        Assert.IsNotNull(registry.MatchInlineButton("flow|any_string_value"));
    }
}
