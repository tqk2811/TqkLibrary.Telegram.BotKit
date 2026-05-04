using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;

namespace TqkLibrary.Telegram.BotKit.Tests;

sealed class StubResourceManager : ResourceManager
{
    readonly Dictionary<string, string> _data;
    public StubResourceManager(Dictionary<string, string> data) { _data = data; }
    public override string? GetString(string name, CultureInfo? culture)
        => _data.TryGetValue(name, out string? v) ? v : null;
}

public class FakeStrings
{
    public static ResourceManager ResourceManager { get; } = new StubResourceManager(new()
    {
        ["BtnLanguage"] = "Language!",
    });
}

sealed class StubLocalizer : IStringLocalizer
{
    readonly Dictionary<string, string> _data;
    public StubLocalizer(Dictionary<string, string> data) { _data = data; }
    public LocalizedString this[string name]
        => _data.TryGetValue(name, out string? v)
            ? new LocalizedString(name, v, resourceNotFound: false)
            : new LocalizedString(name, name, resourceNotFound: true);
    public LocalizedString this[string name, params object[] arguments]
        => _data.TryGetValue(name, out string? v)
            ? new LocalizedString(name, string.Format(v, arguments), resourceNotFound: false)
            : new LocalizedString(name, name, resourceNotFound: true);
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>Records CurrentUICulture seen at lookup time — proves the resolver flips culture on fallback.</summary>
sealed class CultureCapturingLocalizer : IStringLocalizer
{
    public CultureInfo? SeenCulture { get; private set; }
    public LocalizedString this[string name]
    {
        get
        {
            SeenCulture = CultureInfo.CurrentUICulture;
            return new LocalizedString(name, $"[{CultureInfo.CurrentUICulture.Name}]{name}", resourceNotFound: false);
        }
    }
    public LocalizedString this[string name, params object[] arguments] => this[name];
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

[TestClass]
public class LocalizedTextResolverTests
{
    [TestMethod]
    public void NoType_NoFallback_ReturnsLiteral()
    {
        string? text = LocalizedTextResolver.Resolve(
            resourceType: null, resourceName: "BtnLanguage",
            literalFallback: "lit");
        Assert.AreEqual("lit", text);
    }

    [TestMethod]
    public void NoType_WithLocalizerFallback_UsesLocalizer()
    {
        // Repro for the "OpenPicker" bug: attribute omitted TitleResourceType but app has
        // an IStringLocalizer registered in DI. Resolver falls back to localizer[name].
        string? text = LocalizedTextResolver.Resolve(
            resourceType: null, resourceName: "BtnLanguage",
            literalFallback: null,
            fallbackLocalizer: new StubLocalizer(new() { ["BtnLanguage"] = "Language!" }));
        Assert.AreEqual("Language!", text);
    }

    [TestMethod]
    public void NoType_LocalizerMissingKey_FallsThroughToLiteral()
    {
        string? text = LocalizedTextResolver.Resolve(
            resourceType: null, resourceName: "Unknown",
            literalFallback: "lit",
            fallbackLocalizer: new StubLocalizer(new() { ["BtnLanguage"] = "Language!" }));
        Assert.AreEqual("lit", text);
    }

    [TestMethod]
    public void ExplicitType_WinsOverFallbackLocalizer()
    {
        string? text = LocalizedTextResolver.Resolve(
            resourceType: typeof(FakeStrings), resourceName: "BtnLanguage",
            literalFallback: null,
            fallbackLocalizer: new StubLocalizer(new() { ["BtnLanguage"] = "Sprache" }));
        Assert.AreEqual("Language!", text);
    }

    [TestMethod]
    public void FallbackLocalizer_ExplicitCulture_TemporarilyFlipsCurrentUICulture()
    {
        // Regression: SetMyCommands publishing per-language menus passes culture: vi while the
        // process default may be en (or vice versa). The fallback localizer reads CurrentUICulture
        // internally, so the resolver flips+restores it around the lookup.
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        try
        {
            var localizer = new CultureCapturingLocalizer();
            string? text = LocalizedTextResolver.Resolve(
                resourceType: null, resourceName: "DescStart",
                literalFallback: null,
                culture: CultureInfo.GetCultureInfo("vi"),
                fallbackLocalizer: localizer);
            Assert.AreEqual("[vi]DescStart", text);
            Assert.AreEqual("vi", localizer.SeenCulture?.Name);
            // Restored after the call.
            Assert.AreEqual("en", CultureInfo.CurrentUICulture.Name);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [TestMethod]
    public void FallbackLocalizer_NoExplicitCulture_UsesCurrentUICulture()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        try
        {
            var localizer = new CultureCapturingLocalizer();
            string? text = LocalizedTextResolver.Resolve(
                resourceType: null, resourceName: "DescStart",
                literalFallback: null,
                fallbackLocalizer: localizer);
            Assert.AreEqual("[en]DescStart", text);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [TestMethod]
    public void NoName_ReturnsLiteralFallback()
    {
        string? text = LocalizedTextResolver.Resolve(
            resourceType: null, resourceName: null,
            literalFallback: "lit",
            fallbackLocalizer: new StubLocalizer(new() { ["BtnLanguage"] = "Language!" }));
        Assert.AreEqual("lit", text);
    }
}
