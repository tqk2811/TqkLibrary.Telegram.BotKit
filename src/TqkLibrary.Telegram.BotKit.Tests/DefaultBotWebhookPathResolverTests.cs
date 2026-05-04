namespace TqkLibrary.Telegram.BotKit.Tests;

[TestClass]
public class DefaultBotWebhookPathResolverTests
{
    [TestMethod]
    public void ResolvePath_ProducesLowercaseHex()
    {
        var r = new DefaultBotWebhookPathResolver();
        string actual = r.ResolvePath("123456:ABC");
        Assert.AreEqual(64, actual.Length);
        Assert.IsTrue(actual.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')));
    }

    [TestMethod]
    public void ResolvePath_DifferentTokens_DifferentPaths()
    {
        var r = new DefaultBotWebhookPathResolver();
        Assert.AreNotEqual(r.ResolvePath("a"), r.ResolvePath("b"));
    }

    [TestMethod]
    public void ResolvePath_Stable_AcrossCalls()
    {
        var r = new DefaultBotWebhookPathResolver();
        Assert.AreEqual(r.ResolvePath("token"), r.ResolvePath("token"));
    }

    [TestMethod]
    public void ResolvePath_Null_Throws()
    {
        var r = new DefaultBotWebhookPathResolver();
        Assert.ThrowsExactly<ArgumentNullException>(() => r.ResolvePath(null!));
    }
}
