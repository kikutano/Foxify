using System.Globalization;
using System.Text.RegularExpressions;

namespace Foxify.Core.Execution;

/// <summary>Durations written as <c>500ms</c>, <c>2s</c>, <c>1.5s</c>, <c>1m</c>, <c>2h</c> or <c>1d</c>.</summary>
internal static partial class Durations
{
    [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)\s*(ms|s|m|h|d)\s*$")]
    private static partial Regex Pattern();

    public static bool TryParse(string? text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var match = Pattern().Match(text ?? string.Empty);
        if (!match.Success)
            return false;

        var amount = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        duration = match.Groups[2].Value switch
        {
            "ms" => TimeSpan.FromMilliseconds(amount),
            "s" => TimeSpan.FromSeconds(amount),
            "h" => TimeSpan.FromHours(amount),
            "d" => TimeSpan.FromDays(amount),
            _ => TimeSpan.FromMinutes(amount)
        };
        return true;
    }

    public static string Format(TimeSpan duration) => duration.TotalMilliseconds switch
    {
        < 1000 => string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMilliseconds:0.##}ms"),
        < 60_000 => string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.##}s"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMinutes:0.##}m")
    };
}
