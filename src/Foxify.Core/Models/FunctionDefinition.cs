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
    public Dictionary<string, string> Excepted { get; set; } = new();
    public List<ResponseAssert> Asserts { get; set; } = new();
}

public class ResponseAssert
{
    /* We supporting only basic operators, we need to expand 
     * this in the future to support complex jsonpath expressions */
    public string VariableName { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string ExceptedValue { get; set; } = string.Empty;

    public bool Evaluate(string actualValue)
    {
        return Operator switch
        {
            "==" => actualValue == ExceptedValue,
            "!=" => actualValue != ExceptedValue,
            ">" => double.TryParse(actualValue, out var actualNum) &&
                   double.TryParse(ExceptedValue, out var expectedNum) &&
                   actualNum > expectedNum,
            "<" => double.TryParse(actualValue, out var actualNum) &&
                   double.TryParse(ExceptedValue, out var expectedNum) &&
                   actualNum < expectedNum,
            ">=" => double.TryParse(actualValue, out var actualNum) &&
                    double.TryParse(ExceptedValue, out var expectedNum) &&
                    actualNum >= expectedNum,
            "<=" => double.TryParse(actualValue, out var actualNum) &&
                    double.TryParse(ExceptedValue, out var expectedNum) &&
                    actualNum <= expectedNum,
            _ => throw new InvalidOperationException($"Unsupported operator: {Operator}")
        };
    }
}
