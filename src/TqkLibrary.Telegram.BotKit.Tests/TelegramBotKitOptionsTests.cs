namespace TqkLibrary.Telegram.BotKit.Tests;

[TestClass]
public class TelegramBotKitOptionsTests
{
    [TestMethod]
    public void AllowedUpdates_Default_ContainsMessageAndCallbackQuery()
    {
        var opts = new TelegramBotKitOptions();
        Assert.IsNotNull(opts.AllowedUpdates);
        CollectionAssert.AreEquivalent(
            new[] { UpdateType.Message, UpdateType.CallbackQuery },
            opts.AllowedUpdates.ToArray());
    }

    [TestMethod]
    public void AllowedUpdates_CanBeSetToNull_ForTelegramDefaultBehavior()
    {
        var opts = new TelegramBotKitOptions { AllowedUpdates = null };
        Assert.IsNull(opts.AllowedUpdates);
    }

    [TestMethod]
    public void AllowedUpdates_CanBeOverriddenWithCustomList()
    {
        var custom = new[] { UpdateType.Message, UpdateType.EditedMessage, UpdateType.MyChatMember };
        var opts = new TelegramBotKitOptions { AllowedUpdates = custom };
        CollectionAssert.AreEqual(custom, opts.AllowedUpdates!.ToArray());
    }

    [TestMethod]
    public void AutoAnswerCallback_DefaultsToTrue()
    {
        var opts = new TelegramBotKitOptions();
        Assert.IsTrue(opts.AutoAnswerCallback);
    }

    [TestMethod]
    public void AutoAnswerCallback_CanBeDisabled()
    {
        var opts = new TelegramBotKitOptions { AutoAnswerCallback = false };
        Assert.IsFalse(opts.AutoAnswerCallback);
    }
}
