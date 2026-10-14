using System.Text.Json;
using System.Text.RegularExpressions;

namespace Foxify.Core.Execution;

/// <summary>
/// Minimal JSONPath subset used by <c>extract</c>:
/// <c>$</c>, <c>$.a.b</c>, <c>$.items[0].name</c>, <c>$[1]</c>, <c>$.matrix[0][1]</c>.
/// </summary>
internal static partial class JsonPath
{
    // ".property" (any char except '.', '[' and ']') or "[index]"
    [GeneratedRegex(@"\.([^.\[\]]+)|\[(\d+)\]")]
    private static partial Regex Segment();

    /// <summary>
    /// Returns the value at <paramref name="path"/> as a string (numbers keep their raw
    /// representation, booleans are "true"/"false", objects and arrays are raw JSON),
    /// or null when the path is invalid, does not match, or points to a JSON null.
    /// </summary>
    public static string? Select(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path[0] != '$')
            return null;

        var segments = path[1..];
        var current = root;
        int consumed = 0;

        foreach (Match segment in Segment().Matches(segments))
        {
            // Reject paths with gaps between segments, e.g. "$.a..b" or "$.a[x]".
            if (segment.Index != consumed)
                return null;
            consumed += segment.Length;

            if (segment.Groups[1].Success)
            {
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(segment.Groups[1].Value, out current))
                    return null;
            }
            else
            {
                if (current.ValueKind != JsonValueKind.Array ||
                    !int.TryParse(segment.Groups[2].Value, out var index) ||
                    index >= current.GetArrayLength())
                    return null;
                current = current[index];
            }
        }

        if (consumed != segments.Length)
            return null;

        return current.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.String => current.GetString(),
            _ => current.GetRawText() // numbers keep their exact representation; objects/arrays as JSON
        };
    }
}
