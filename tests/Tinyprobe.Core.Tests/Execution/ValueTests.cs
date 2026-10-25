using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Tests.Fakes;

namespace Tinyprobe.Core.Tests.Execution;

public class JsonPathTests
{
    private static readonly System.Text.Json.JsonElement Root = Run.Json("""
        { "a": { "b": [ { "c": 1 }, { "c": 2 } ] }, "m": [[1, 2], [3, 4]], "x-y": "dash", "n": null }
        """);

    [Theory]
    [InlineData("$", "{")]
    [InlineData("$.a.b[1].c", "2")]
    [InlineData("$.m[1][0]", "3")]
    [InlineData("$.x-y", "\"dash\"")]
    [InlineData("$.n", "null")]
    public void TrySelect_FindsValues(string path, string startsWith)
    {
        Assert.True(JsonPath.TrySelect(Root, path, out var value));
        Assert.StartsWith(startsWith, value.GetRawText());
    }

    [Theory]
    [InlineData("$.missing")]
    [InlineData("$.a.b[5]")]
    [InlineData("$.a.b.c")]
    public void TrySelect_IsFalseWhenNothingMatches(string path)
    {
        Assert.False(JsonPath.TrySelect(Root, path, out _));
    }

    [Theory]
    [InlineData("$", true)]
    [InlineData("$.a[0].b", true)]
    [InlineData("a.b", false)]
    [InlineData("$.a..b", false)]
    [InlineData("$.a[x]", false)]
    [InlineData("", false)]
    public void IsValid(string path, bool valid)
    {
        Assert.Equal(valid, JsonPath.IsValid(path));
    }
}

public class JsonValuesTests
{
    [Theory]
    [InlineData("1", "1.0", true)]
    [InlineData("1", "\"1\"", false)]
    [InlineData("{\"a\":1,\"b\":2}", "{\"b\":2,\"a\":1}", true)]
    [InlineData("[1,2]", "[2,1]", false)]
    [InlineData("null", "null", true)]
    [InlineData("true", "false", false)]
    public void AreEqual_IsTyped(string expected, string actual, bool equal)
    {
        Assert.Equal(equal, JsonValues.AreEqual(Run.Json(expected), Run.Json(actual)));
    }

    [Theory]
    [InlineData("\"text\"", "text")]
    [InlineData("42", "42")]
    [InlineData("true", "true")]
    [InlineData("null", "")]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    public void ToText(string json, string text)
    {
        Assert.Equal(text, JsonValues.ToText(Run.Json(json)));
    }
}

public class DurationsTests
{
    [Theory]
    [InlineData("500ms", 500)]
    [InlineData("2s", 2000)]
    [InlineData("1.5s", 1500)]
    [InlineData("1m", 60000)]
    [InlineData("2h", 7200000)]
    [InlineData("1d", 86400000)]
    [InlineData(" 20 ms ", 20)]
    public void TryParse_AcceptsUnits(string text, double milliseconds)
    {
        Assert.True(Durations.TryParse(text, out var duration));
        Assert.Equal(milliseconds, duration.TotalMilliseconds);
    }

    [Theory]
    [InlineData("500")]
    [InlineData("soon")]
    [InlineData("-1s")]
    [InlineData("")]
    public void TryParse_RejectsValuesWithoutUnit(string text)
    {
        Assert.False(Durations.TryParse(text, out _));
    }
}
