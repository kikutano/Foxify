using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tinyprobe.Core.Execution;

/// <summary>
/// Generated values: placeholders whose name starts with <c>$</c>, e.g. <c>${$uuid}</c> or
/// <c>${$now+2h}</c>. Every use produces a new value; define one in <c>variables</c> to reuse it.
/// </summary>
internal static partial class Generators
{
    /// <summary>The generators, as written in a workflow (offsets shown as an example).</summary>
    public static readonly string[] Names = ["$uuid", "$timestamp", "$randomInt", "$randomString", "$now", "$today"];

    // $now / $today with an optional offset: $now+2h, $now-30m, $today+1d
    [GeneratedRegex(@"^\$(now|today)(?:([+-])(.+))?$")]
    private static partial Regex DateExpression();

    public static bool IsGenerated(string name) => name.StartsWith('$');

    /// <summary>Checks a generator expression without evaluating it; returns the problem, or null.</summary>
    public static string? Validate(string name)
    {
        if (Names.Contains(name))
            return null;

        var date = DateExpression().Match(name);
        if (date.Success)
        {
            return !date.Groups[3].Success || Durations.TryParse(date.Groups[3].Value, out _)
                ? null
                : $"invalid offset '{date.Groups[3].Value}' in '{name}': use e.g. ${{$now+2h}} or ${{$today-1d}}";
        }

        return $"unknown generated value '{name}'";
    }

    public static JsonElement Generate(string name)
    {
        switch (name)
        {
            case "$uuid":
                return JsonValues.FromString(Guid.NewGuid().ToString());
            case "$timestamp":
                return JsonValues.Build(w => w.WriteNumberValue(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            case "$randomInt":
                return JsonValues.Build(w => w.WriteNumberValue(RandomNumberGenerator.GetInt32(0, 1_000_000)));
            case "$randomString":
                return JsonValues.FromString(RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", 8));
        }

        var date = DateExpression().Match(name);
        if (!date.Success)
            throw new InvalidOperationException($"Unknown generated value '{name}'.");

        var offset = TimeSpan.Zero;
        if (date.Groups[3].Success)
        {
            if (!Durations.TryParse(date.Groups[3].Value, out offset))
                throw new InvalidOperationException($"Invalid offset in '{name}'.");
            if (date.Groups[2].Value == "-")
                offset = -offset;
        }

        var instant = DateTimeOffset.UtcNow.Add(offset);
        return JsonValues.FromString(date.Groups[1].Value == "now"
            ? instant.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            : instant.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
