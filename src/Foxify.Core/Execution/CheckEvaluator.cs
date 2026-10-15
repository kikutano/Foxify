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
    public static AssertionResult EvaluateBody(string path, Condition condition, JsonElement? actual, IVariableScope scope) =>
        new()
        {
            Subject = path,
            Condition = Describe(condition, scope),
            ActualValue = actual is { } a ? JsonValues.ToDisplay(a) : Missing,
            Passed = Passes(condition, actual, scope, textual: false)
        };

    /// <summary>A header condition: header values are text, so values are compared as text.</summary>
    public static AssertionResult EvaluateHeader(string name, Condition condition, string? actual, IVariableScope scope)
    {
        JsonElement? value = actual is null ? null : JsonValues.FromString(actual);
        return new AssertionResult
        {
            Subject = $"header {name}",
            Condition = Describe(condition, scope),
            ActualValue = value is { } v ? JsonValues.ToDisplay(v) : Missing,
            Passed = Passes(condition, value, scope, textual: true)
        };
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

    private static bool Passes(Condition condition, JsonElement? actual, IVariableScope scope, bool textual)
    {
        switch (condition.Operator)
        {
            case Operator.Exists:
                return actual.HasValue == condition.Exists;
            case Operator.Any:
                return condition.Alternatives.Any(alternative => alternative.All(c => Passes(c, actual, scope, textual)));
        }

        var expected = TemplateRenderer.RenderValue(condition.Value!, scope);
        if (actual is not { } value)
            return condition.Operator == Operator.NotEqual;

        return condition.Operator switch
        {
            Operator.Equal => Same(expected, value, textual),
            Operator.NotEqual => !Same(expected, value, textual),
            Operator.In => expected.ValueKind == JsonValueKind.Array && expected.EnumerateArray().Any(e => Same(e, value, textual)),
            Operator.Contains => Contains(value, expected, textual),
            _ => Compare(condition.Operator, value, expected)
        };
    }

    /// <summary>Equality: typed for JSON values, as text for header values.</summary>
    private static bool Same(JsonElement expected, JsonElement actual, bool textual) =>
        textual ? JsonValues.ToText(expected) == JsonValues.ToText(actual) : JsonValues.AreEqual(expected, actual);

    private static bool Contains(JsonElement actual, JsonElement expected, bool textual) => actual.ValueKind switch
    {
        JsonValueKind.Array => actual.EnumerateArray().Any(item => Same(expected, item, textual)),
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

    /// <summary>The condition as shown in the report, e.g. <c>&gt;= 3</c>, <c>in ["a","b"]</c>, <c>&lt; 0 or &gt; 100</c>.</summary>
    private static string Describe(Condition condition, IVariableScope scope) => condition.Operator switch
    {
        Operator.Exists => condition.Exists ? "exists" : "does not exist",
        Operator.Any => string.Join(" or ", condition.Alternatives.Select(alternative => alternative.Count == 1
            ? Describe(alternative[0], scope)
            : $"({string.Join(" and ", alternative.Select(c => Describe(c, scope)))})")),
        _ => $"{Symbol(condition.Operator)} {DescribeValue(condition, scope)}"
    };

    private static string DescribeValue(Condition condition, IVariableScope scope)
    {
        try
        {
            return JsonValues.ToDisplay(TemplateRenderer.RenderValue(condition.Value!, scope));
        }
        catch (InvalidOperationException)
        {
            return condition.Value!.ToString(); // undefined variable: show the template as written
        }
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
        Operator.In => "in",
        _ => "exists"
    };
}
