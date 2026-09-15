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
}
