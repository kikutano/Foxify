using System.Text.Json;
using Foxify.Core.Definitions;
using Foxify.Core.Models;

namespace Foxify.Core.Execution;

/// <summary>Evaluates the conditions of <c>expect.body</c>, <c>expect.headers</c> and <c>expect.duration</c>.</summary>
internal static class CheckEvaluator
{
    private const string Missing = "(missing)";

    /// <summary>
    /// A body condition: typed comparison of JSON values.
    /// <paramref name="actual"/> is null when the JSONPath matched nothing.
    /// </summary>
    public static AssertionResult EvaluateBody(string path, Condition condition, JsonElement? actual, IVariableScope scope)
    {
        var expected = condition.Value is null ? (JsonElement?)null : TemplateRenderer.RenderValue(condition.Value, scope);
        var passed = condition.Operator switch
        {
            Operator.Exists => actual.HasValue == condition.Exists,
            Operator.Equal => actual.HasValue && JsonValues.AreEqual(expected!.Value, actual.Value),
            Operator.NotEqual => !actual.HasValue || !JsonValues.AreEqual(expected!.Value, actual.Value),
            Operator.Contains => actual.HasValue && Contains(actual.Value, expected!.Value),
            _ => actual.HasValue && Compare(condition.Operator, actual.Value, expected!.Value)
        };

        return Result(path, condition, expected, actual is { } a ? JsonValues.ToDisplay(a) : Missing, passed);
    }

    /// <summary>A header condition: header values are text, so values are compared as text.</summary>
    public static AssertionResult EvaluateHeader(string name, Condition condition, string? actual, IVariableScope scope)
    {
        var expected = condition.Value is null ? (JsonElement?)null : TemplateRenderer.RenderValue(condition.Value, scope);
        var expectedText = expected is { } e ? JsonValues.ToText(e) : string.Empty;

        var passed = condition.Operator switch
        {
            Operator.Exists => (actual is not null) == condition.Exists,
            Operator.Equal => actual == expectedText,
            Operator.NotEqual => actual != expectedText,
            Operator.Contains => actual?.Contains(expectedText, StringComparison.Ordinal) == true,
            _ => actual is not null && Compare(condition.Operator, JsonValues.FromString(actual), JsonValues.FromString(expectedText))
        };

        var display = expected is null ? (JsonElement?)null : JsonValues.FromString(expectedText);
        return Result($"header {name}", condition, display, actual is null ? Missing : JsonValues.ToDisplay(JsonValues.FromString(actual)), passed);
    }

    public static AssertionResult EvaluateDuration(Condition condition, TimeSpan actual)
    {
        var limit = condition.DurationValue!.Value;
        var passed = condition.Operator switch
        {
            Operator.LessThan => actual < limit,
            Operator.LessOrEqual => actual <= limit,
            Operator.GreaterThan => actual > limit,
            Operator.GreaterOrEqual => actual >= limit,
            _ => false
        };

        return new AssertionResult
        {
            Subject = "duration",
            Condition = $"{Symbol(condition.Operator)} {Durations.Format(limit)}",
            ActualValue = Durations.Format(actual),
            Passed = passed
        };
    }

    private static bool Contains(JsonElement actual, JsonElement expected) => actual.ValueKind switch
    {
        JsonValueKind.Array => actual.EnumerateArray().Any(item => JsonValues.AreEqual(expected, item)),
        JsonValueKind.String => actual.GetString()!.Contains(JsonValues.ToText(expected), StringComparison.Ordinal),
        _ => false
    };

    private static bool Compare(Operator op, JsonElement actual, JsonElement expected)
    {
        if (!JsonValues.TryGetNumber(actual, out var a) || !JsonValues.TryGetNumber(expected, out var b))
            return false;

        return op switch
        {
            Operator.GreaterThan => a > b,
            Operator.GreaterOrEqual => a >= b,
            Operator.LessThan => a < b,
            Operator.LessOrEqual => a <= b,
            _ => false
        };
    }

    private static AssertionResult Result(string subject, Condition condition, JsonElement? expected, string actual, bool passed)
    {
        var description = condition.Operator == Operator.Exists
            ? (condition.Exists ? "exists" : "does not exist")
            : $"{Symbol(condition.Operator)} {(expected is { } e ? JsonValues.ToDisplay(e) : string.Empty)}";

        return new AssertionResult { Subject = subject, Condition = description, ActualValue = actual, Passed = passed };
    }

    private static string Symbol(Operator op) => op switch
    {
        Operator.Equal => "==",
        Operator.NotEqual => "!=",
        Operator.GreaterThan => ">",
        Operator.GreaterOrEqual => ">=",
        Operator.LessThan => "<",
        Operator.LessOrEqual => "<=",
        Operator.Contains => "contains",
        _ => "exists"
    };
}
