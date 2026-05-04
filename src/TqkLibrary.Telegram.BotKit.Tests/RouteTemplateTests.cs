using TqkLibrary.Telegram.BotKit.Routing;

namespace TqkLibrary.Telegram.BotKit.Tests;

[TestClass]
public class RouteTemplateTests
{
    [TestMethod]
    public void Parse_LiteralOnly_SetsPrefix()
    {
        var t = RouteTemplate.Parse("direct_deposit");
        Assert.AreEqual("direct_deposit", t.Prefix);
        Assert.AreEqual(1, t.SegmentCount);
        Assert.AreEqual(0, t.Parameters.Count);
    }

    [TestMethod]
    public void Parse_WithPlaceholder_ExtractsParameter()
    {
        var t = RouteTemplate.Parse("check_source|{id:guid}|apv");
        Assert.AreEqual("check_source", t.Prefix);
        Assert.AreEqual(3, t.SegmentCount);
        Assert.AreEqual(1, t.Parameters.Count);
        Assert.AreEqual("id", t.Parameters[0].Name);
        Assert.AreEqual(typeof(Guid), t.Parameters[0].ClrType);
        Assert.AreEqual(1, t.Parameters[0].SegmentIndex);
    }

    [TestMethod]
    public void Parse_PlaceholderWithoutConstraint_DefaultsToString()
    {
        var t = RouteTemplate.Parse("x|{name}");
        Assert.AreEqual(typeof(string), t.Parameters[0].ClrType);
    }

    [TestMethod]
    public void Parse_MissingLiteralPrefix_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => RouteTemplate.Parse("{id:guid}"));

    [TestMethod]
    public void Parse_EmptyPlaceholderName_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => RouteTemplate.Parse("x|{}"));

    [TestMethod]
    public void Parse_UnknownConstraint_Throws()
        => Assert.ThrowsExactly<InvalidOperationException>(() => RouteTemplate.Parse("x|{id:decimal}"));

    [TestMethod]
    public void TryMatch_CorrectData_ReturnsValues()
    {
        var t = RouteTemplate.Parse("check_source|{id:guid}|apv");
        Guid id = Guid.NewGuid();
        bool ok = t.TryMatch($"check_source|{id:D}|apv", out var values);
        Assert.IsTrue(ok);
        Assert.AreEqual(1, values.Count);
        Assert.AreEqual(id.ToString("D"), values["id"]);
    }

    [TestMethod]
    public void TryMatch_WrongSegmentCount_ReturnsFalse()
    {
        var t = RouteTemplate.Parse("check_source|{id:guid}");
        Assert.IsFalse(t.TryMatch("check_source", out _));
        Assert.IsFalse(t.TryMatch($"check_source|{Guid.NewGuid():D}|extra", out _));
    }

    [TestMethod]
    public void TryMatch_LiteralMismatch_ReturnsFalse()
    {
        var t = RouteTemplate.Parse("check_source|{id:guid}|apv");
        Assert.IsFalse(t.TryMatch($"check_source|{Guid.NewGuid():D}|rjt", out _));
    }

    [TestMethod]
    public void TryMatch_InvalidGuidConstraint_ReturnsFalse()
    {
        var t = RouteTemplate.Parse("check_source|{id:guid}");
        Assert.IsFalse(t.TryMatch("check_source|not-a-guid", out _));
    }

    [TestMethod]
    public void TryMatch_LiteralSegmentIsCaseInsensitive()
    {
        var t = RouteTemplate.Parse("Check_Source");
        Assert.IsTrue(t.TryMatch("check_source", out _));
    }

    [TestMethod]
    public void Format_FillsPlaceholders()
    {
        var t = RouteTemplate.Parse("check_source|{id:guid}|apv");
        Guid id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        string data = t.Format(new Dictionary<string, object?> { ["id"] = id });
        Assert.AreEqual($"check_source|{id:D}|apv", data);
    }

    [TestMethod]
    public void Format_MissingValue_Throws()
    {
        var t = RouteTemplate.Parse("x|{id:guid}");
        Assert.ThrowsExactly<ArgumentException>(() => t.Format(new Dictionary<string, object?>()));
    }

    [TestMethod]
    public void Format_ValueWithSeparator_Throws()
    {
        var t = RouteTemplate.Parse("x|{name}");
        Assert.ThrowsExactly<ArgumentException>(() =>
            t.Format(new Dictionary<string, object?> { ["name"] = "ab|cd" }));
    }

    [TestMethod]
    public void Format_ExceedingCallbackDataLimit_Throws()
    {
        // prefix 40 + '|' + 40 = 81 > 64
        var t = RouteTemplate.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|{s}");
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            t.Format(new Dictionary<string, object?> { ["s"] = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }));
    }

    [TestMethod]
    public void Format_RoundTripWithMatch()
    {
        var t = RouteTemplate.Parse("k|{id:long}|{x}");
        string data = t.Format(new Dictionary<string, object?> { ["id"] = 123L, ["x"] = "abc" });
        Assert.IsTrue(t.TryMatch(data, out var values));
        Assert.AreEqual("123", values["id"]);
        Assert.AreEqual("abc", values["x"]);
    }

    // ── Multi-type constraint ─────────────────────────────────────────────

    [TestMethod]
    public void Parse_MultiTypeConstraint_ExtractsAllTypes()
    {
        var t = RouteTemplate.Parse("k|{a:guid|int}");
        RouteParameter p = t.Parameters[0];
        CollectionAssert.AreEqual(new[] { typeof(Guid), typeof(int) }, p.ClrTypes.ToArray());
        Assert.AreEqual(typeof(Guid), p.ClrType); // primary = first
    }

    [TestMethod]
    public void Parse_MultiTypeConstraint_SegmentSeparatorsUnaffected()
    {
        // Pipe inside {...} must be treated as a constraint separator; outside it remains a segment separator.
        var t = RouteTemplate.Parse("k|{a:guid|int}|x");
        Assert.AreEqual(3, t.SegmentCount);
        Assert.AreEqual(1, t.Parameters.Count);
        Assert.AreEqual("a", t.Parameters[0].Name);
    }

    [TestMethod]
    public void TryMatch_MultiType_AcceptsAnyCandidate()
    {
        var t = RouteTemplate.Parse("k|{a:guid|int}");
        Guid g = Guid.NewGuid();
        Assert.IsTrue(t.TryMatch($"k|{g:D}", out var v1));
        Assert.AreEqual(g.ToString("D"), v1["a"]);
        Assert.IsTrue(t.TryMatch("k|42", out var v2));
        Assert.AreEqual("42", v2["a"]);
    }

    [TestMethod]
    public void TryMatch_MultiType_RejectsWhenNoneMatch()
    {
        var t = RouteTemplate.Parse("k|{a:guid|int}");
        Assert.IsFalse(t.TryMatch("k|not-a-guid-or-int", out _));
    }

    [TestMethod]
    public void Parse_MultiTypeUnknownMember_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(
            () => RouteTemplate.Parse("k|{a:guid|decimal}"));
    }

    // ── Parse-time worst-case bytes validation ───────────────────────────

    [TestMethod]
    public void Parse_LiteralOnlyExceeds64Bytes_Throws()
    {
        // 65-byte literal segment
        string longLiteral = new string('a', 65);
        Assert.ThrowsExactly<InvalidOperationException>(() => RouteTemplate.Parse(longLiteral));
    }

    [TestMethod]
    public void Parse_TwoGuidsExceedWorstCase_Throws()
    {
        // prefix(1) + sep(1) + guid(36) + sep(1) + guid(36) = 75 > 64
        Assert.ThrowsExactly<InvalidOperationException>(
            () => RouteTemplate.Parse("k|{a:guid}|{b:guid}"));
    }

    [TestMethod]
    public void Parse_StringPlaceholder_SkipsParseTimeCheck()
    {
        // Literal 50 bytes + |{s} (string = unknown upper bound) → does not throw at Parse;
        // the runtime check in Format still applies.
        string literal = new string('a', 50);
        var t = RouteTemplate.Parse($"{literal}|{{s}}");
        Assert.IsNotNull(t);
    }

    [TestMethod]
    public void Parse_FixedTypesUnderLimit_DoesNotThrow()
    {
        // prefix(2) + sep(1) + guid(36) + sep(1) + int(11) = 51 ≤ 64
        var t = RouteTemplate.Parse("xy|{a:guid}|{b:int}");
        Assert.AreEqual(3, t.SegmentCount);
    }
}
