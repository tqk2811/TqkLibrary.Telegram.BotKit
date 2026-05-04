using System.Globalization;

namespace TqkLibrary.Telegram.BotKit.Tests;

sealed class StubLanguageAccessor(string? lang) : ILanguageStateAccessor
{
    public string? Language { get; } = lang;
}

[TestClass]
public class CultureProviderTests
{
    [TestMethod]
    public void DefaultProvider_NoAccessorRegistered_ReturnsNull()
    {
        var p = new DefaultCultureProvider();
        Assert.IsNull(p.GetCulture());
    }

    [TestMethod]
    public void DefaultProvider_NullLanguage_ReturnsNull()
    {
        var p = new DefaultCultureProvider(new StubLanguageAccessor(null));
        Assert.IsNull(p.GetCulture());
    }

    [TestMethod]
    public void DefaultProvider_EmptyLanguage_ReturnsNull()
    {
        var p = new DefaultCultureProvider(new StubLanguageAccessor(""));
        Assert.IsNull(p.GetCulture());
    }

    [TestMethod]
    public void DefaultProvider_ValidLanguageTag_ReturnsCulture()
    {
        var p = new DefaultCultureProvider(new StubLanguageAccessor("vi"));
        CultureInfo? c = p.GetCulture();
        Assert.IsNotNull(c);
        Assert.AreEqual("vi", c.Name);
    }

    [TestMethod]
    public void DefaultProvider_RegionalTag_ReturnsCulture()
    {
        var p = new DefaultCultureProvider(new StubLanguageAccessor("vi-VN"));
        CultureInfo? c = p.GetCulture();
        Assert.IsNotNull(c);
        Assert.AreEqual("vi-VN", c.Name);
    }

    [TestMethod]
    public void DefaultProvider_InvalidTag_ReturnsNull()
    {
        var p = new DefaultCultureProvider(new StubLanguageAccessor("xx-INVALID-NOT-A-CULTURE"));
        Assert.IsNull(p.GetCulture());
    }
}
