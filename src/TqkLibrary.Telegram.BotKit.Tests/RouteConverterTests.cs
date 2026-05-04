using TqkLibrary.Telegram.BotKit.Routing;

namespace TqkLibrary.Telegram.BotKit.Tests;

[TestClass]
public class RouteConverterTests
{
    // Converter is internal — verified via RouteTemplate.Format/TryMatch end-to-end.

    [TestMethod]
    public void Int_RoundTrip()
    {
        var t = RouteTemplate.Parse("k|{n:int}");
        string data = t.Format(new Dictionary<string, object?> { ["n"] = 42 });
        Assert.AreEqual("k|42", data);
        Assert.IsTrue(t.TryMatch("k|42", out var v));
        Assert.AreEqual("42", v["n"]);
    }

    [TestMethod]
    public void Long_Negative_RoundTrip()
    {
        var t = RouteTemplate.Parse("k|{n:long}");
        string data = t.Format(new Dictionary<string, object?> { ["n"] = -123456789L });
        Assert.AreEqual("k|-123456789", data);
    }

    [TestMethod]
    public void Bool_RoundTrip()
    {
        var t = RouteTemplate.Parse("k|{b:bool}");
        string data = t.Format(new Dictionary<string, object?> { ["b"] = true });
        Assert.AreEqual("k|true", data);
        Assert.IsTrue(t.TryMatch("k|false", out _));
    }

    [TestMethod]
    public void Guid_UsesDashedFormat()
    {
        var t = RouteTemplate.Parse("k|{id:guid}");
        Guid g = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.AreEqual($"k|{g:D}", t.Format(new Dictionary<string, object?> { ["id"] = g }));
    }

    [TestMethod]
    public void String_AllowsAnyTextExceptSeparator()
    {
        var t = RouteTemplate.Parse("k|{x}");
        Assert.AreEqual("k|hello_world 123", t.Format(new Dictionary<string, object?> { ["x"] = "hello_world 123" }));
    }

    [TestMethod]
    public void IntConstraint_RejectsNonNumeric()
    {
        var t = RouteTemplate.Parse("k|{n:int}");
        Assert.IsFalse(t.TryMatch("k|abc", out _));
    }
}
