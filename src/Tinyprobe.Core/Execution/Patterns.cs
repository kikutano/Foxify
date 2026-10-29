using System.Text.RegularExpressions;

namespace Tinyprobe.Core.Execution;

/// <summary>The regular expressions of <c>matches</c> (docs/specs/0015-matches.md).</summary>
internal static class Patterns
{
    /// <summary>A pattern that backtracks without end fails its check instead of hanging the run.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Built at run time, so Native AOT runs it with the interpreter (there is no code generation).
    /// Flags are inline in the pattern, as <c>(?i)</c>.
    /// </summary>
    public static bool TryCreate(string pattern, out Regex? regex, out string reason)
    {
        try
        {
            regex = new Regex(pattern, RegexOptions.CultureInvariant, Timeout);
            reason = string.Empty;
            return true;
        }
        catch (RegexParseException ex)
        {
            regex = null;
            reason = Reason(ex.Message);
            return false;
        }
    }

    // "Invalid pattern '^a[' at offset 3. Unterminated [] set." -> "Unterminated [] set": the pattern is already in our message.
    private static string Reason(string message)
    {
        var offset = message.IndexOf(" at offset ", StringComparison.Ordinal);
        var start = offset < 0 ? -1 : message.IndexOf(". ", offset, StringComparison.Ordinal);
        var reason = start < 0 ? message : message[(start + 2)..];
        return reason.TrimEnd('.');
    }
}

/// <summary>A <c>matches</c> that cannot be evaluated: the step fails with this reason, as with an undefined variable.</summary>
internal sealed class PatternException(string message) : InvalidOperationException(message);
