using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tinyprobe.Core.Execution;

/// <summary>
/// Minimal JSONPath subset used by checks and <c>save</c>:
/// <c>$</c>, <c>$.a.b</c>, <c>$.items[0].name</c>, <c>$[1]</c>, <c>$.matrix[0][1]</c>.
/// </summary>
internal static partial class JsonPath
{
    // ".property" (any char except '.', '[' and ']') or "[index]"
    [GeneratedRegex(@"\.([^.\[\]]+)|\[(\d+)\]")]
    private static partial Regex Segment();

    public static bool IsValid(string path) => TryParse(path, out _);

    /// <summary>Finds the value at <paramref name="path"/>; false when the path does not match.</summary>
    public static bool TrySelect(JsonElement root, string path, out JsonElement value)
    {
        value = default;
        if (!TryParse(path, out var segments))
            return false;

        var current = root;
        foreach (var segment in segments)
        {
            if (segment.Property is { } property)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(property, out current))
                    return false;
            }
            else
            {
                if (current.ValueKind != JsonValueKind.Array || segment.Index >= current.GetArrayLength())
                    return false;
                current = current[segment.Index];
            }
        }

        value = current;
        return true;
    }

    private readonly record struct PathSegment(string? Property, int Index);

    private static bool TryParse(string path, out List<PathSegment> segments)
    {
        segments = new List<PathSegment>();
        if (string.IsNullOrWhiteSpace(path) || path[0] != '$')
            return false;

        var rest = path[1..];
        int consumed = 0;
        foreach (Match match in Segment().Matches(rest))
        {
            // Reject paths with gaps between segments, e.g. "$.a..b" or "$.a[x]".
            if (match.Index != consumed)
                return false;
            consumed += match.Length;

            if (match.Groups[1].Success)
                segments.Add(new PathSegment(match.Groups[1].Value, 0));
            else if (int.TryParse(match.Groups[2].Value, out var index))
                segments.Add(new PathSegment(null, index));
            else
                return false;
        }

        return consumed == rest.Length;
    }
}
