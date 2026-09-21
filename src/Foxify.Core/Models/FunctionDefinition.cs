namespace Foxify.Core.Models;

public class FunctionDefinition
{
    public string Type { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Baseurl { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> Extract { get; set; } = new();
    public Dictionary<string, string> Expected { get; set; } = new();
    public Dictionary<string, string> Asserts { get; set; } = new();
}

public static class AssertEvaluator
{
    public static bool Evaluate(string actualValue, string assertionExpression)
    {
        if (string.IsNullOrWhiteSpace(assertionExpression))
            return true;

        // Gestione dell'operatore logico OR (||)
        if (assertionExpression.Contains("||"))
        {
            var orParts = assertionExpression.Split("||");
            foreach (var part in orParts)
            {
                if (EvaluateSingleOrAnd(actualValue, part.Trim()))
                    return true;
            }
            return false;
        }

        return EvaluateSingleOrAnd(actualValue, assertionExpression);
    }

    private static bool EvaluateSingleOrAnd(string actualValue, string assertionExpression)
    {
        if (assertionExpression.Contains("&&"))
        {
            var andParts = assertionExpression.Split("&&");
            foreach (var part in andParts)
            {
                if (!EvaluateSingleComparison(actualValue, part.Trim()))
                    return false;
            }
            return true;
        }

        return EvaluateSingleComparison(actualValue, assertionExpression);
    }

    private static bool EvaluateSingleComparison(string actualValue, string expression)
    {
        expression = expression.Trim();

        if (expression.StartsWith("!="))
        {
            var expected = expression[2..].Trim(' ', '\'', '"');
            return actualValue != expected;
        }
        if (expression.StartsWith(">="))
        {
            return double.TryParse(actualValue, out var act) &&
                   double.TryParse(expression[2..].Trim(), out var exp) && act >= exp;
        }
        if (expression.StartsWith(">"))
        {
            return double.TryParse(actualValue, out var act) &&
                   double.TryParse(expression[1..].Trim(), out var exp) && act > exp;
        }
        if (expression.StartsWith("<="))
        {
            return double.TryParse(actualValue, out var act) &&
                   double.TryParse(expression[2..].Trim(), out var exp) && act <= exp;
        }
        if (expression.StartsWith("<"))
        {
            return double.TryParse(actualValue, out var act) &&
                   double.TryParse(expression[1..].Trim(), out var exp) && act < exp;
        }
        if (expression.StartsWith("=="))
        {
            var expected = expression[2..].Trim(' ', '\'', '"');
            return actualValue == expected;
        }

        return actualValue == expression.Trim(' ', '\'', '"');
    }
}