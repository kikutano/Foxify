using System.Text.Json;
using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Models;

namespace Tinyprobe.Core.Execution;

/// <summary>Evaluates the conditions of <c>expect.body</c>, <c>expect.headers</c>, <c>expect.cookies</c> and <c>expect.duration</c>.</summary>
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
            Passed = Evaluate(path, condition, scope, () => Passes(condition, actual, scope, textual: false))
        };

    /// <summary>A header condition: header values are text, so values are compared as text.</summary>
    public static AssertionResult EvaluateHeader(string name, Condition condition, string? actual, IVariableScope scope) =>
        EvaluateText($"header {name}", condition, actual, scope);

    /// <summary>A cookie condition: <paramref name="actual"/> is the cookie's value, null when the jar has no such cookie.</summary>
    public static AssertionResult EvaluateCookie(string name, Condition condition, string? actual, IVariableScope scope) =>
        EvaluateText($"cookie {name}", condition, actual, scope);

    private static AssertionResult EvaluateText(string subject, Condition condition, string? actual, IVariableScope scope)
    {
        JsonElement? value = actual is null ? null : JsonValues.FromString(actual);
        return new AssertionResult
        {
            Subject = subject,
            Condition = Describe(condition, scope),
            ActualValue = value is { } v ? JsonValues.ToDisplay(v) : Missing,
            Passed = Evaluate(subject, condition, scope, () => Passes(condition, value, scope, textual: true))
        };
    }

    /// <summary>A pattern that cannot be used fails the step with a message that names the check.</summary>
    private static bool Evaluate(string subject, Condition condition, IVariableScope scope, Func<bool> passes)
    {
        try
        {
            return passes();
        }
        catch (PatternException ex)
        {
            throw new InvalidOperationException($"{subject} {Describe(condition, scope)}: {ex.Message}");
        }
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
            Operator.Matches => Matches(condition, expected, value),
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

    /// <summary>A string as it is, a number or a boolean as its JSON text; null, objects and arrays never match.</summary>
    private static bool Matches(Condition condition, JsonElement expected, JsonElement actual)
    {
        var text = actual.ValueKind switch
        {
            JsonValueKind.String => actual.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => actual.GetRawText(),
            _ => null
        };
        if (text is null)
            return false;

        var pattern = JsonValues.ToText(expected);
        var regex = condition.Pattern;
        if (regex is null && !Patterns.TryCreate(pattern, out regex, out var reason))
            throw new PatternException($"invalid regular expression '{pattern}': {reason}");

        try
        {
            return regex!.IsMatch(text);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            throw new PatternException($"the regular expression took more than {Durations.Format(Patterns.Timeout)} on this value");
        }
    }

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

    /// <summary>
    /// The condition as shown in the report, e.g. <c>&gt;= 3</c>, <c>in ["a","b"]</c>, <c>&lt; 0 or &gt; 100</c>.
    /// Without a scope placeholders are shown as written (<c>== "${email}"</c>).
    /// </summary>
    internal static string Describe(Condition condition, IVariableScope? scope) => condition.Operator switch
    {
        Operator.Exists => condition.Exists ? "exists" : "does not exist",
        Operator.Any => string.Join(" or ", condition.Alternatives.Select(alternative => alternative.Count == 1
            ? Describe(alternative[0], scope)
            : $"({string.Join(" and ", alternative.Select(c => Describe(c, scope)))})")),
        _ => $"{Symbol(condition.Operator)} {DescribeValue(condition, scope)}"
    };

    private static string DescribeValue(Condition condition, IVariableScope? scope)
    {
        if (condition.DurationValue is { } limit)
            return Durations.Format(limit);
        if (scope is null)
            return JsonValues.ToDisplay(TemplateRenderer.ToLiteral(condition.Value!.Node));

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
        Operator.Matches => "matches",
        _ => "exists"
    };
}
