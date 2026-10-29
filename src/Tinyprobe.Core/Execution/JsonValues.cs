using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Tinyprobe.Core.Execution;

/// <summary>Helpers on <see cref="JsonElement"/>, the runtime type of every variable and value.</summary>
internal static class JsonValues
{
    public static JsonElement FromString(string value) => Build(writer => writer.WriteStringValue(value));

    public static JsonElement Build(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            write(writer);

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    /// <summary>The value as it appears inside a text: strings unquoted, numbers as written, objects as JSON.</summary>
    public static string ToText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText()
    };

    /// <summary>The value as JSON, for reports: strings quoted.</summary>
    public static string ToDisplay(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined => "undefined",
        // As people write it: `^(a+)+$` and `<b>`, not the \u002B and \u003C of the default JSON escaping.
        JsonValueKind.String => $"\"{JsonEncodedText.Encode(value.GetString()!, JavaScriptEncoder.UnsafeRelaxedJsonEscaping)}\"",
        _ => value.GetRawText()
    };

    /// <summary>Typed equality: numbers by value, objects regardless of property order.</summary>
    public static bool AreEqual(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
            return false;

        switch (expected.ValueKind)
        {
            case JsonValueKind.Number:
                return TryGetNumber(expected, out var a) && TryGetNumber(actual, out var b) && a == b;
            case JsonValueKind.String:
                return expected.GetString() == actual.GetString();
            case JsonValueKind.Array:
                if (expected.GetArrayLength() != actual.GetArrayLength())
                    return false;
                using (var left = expected.EnumerateArray())
                using (var right = actual.EnumerateArray())
                {
                    while (left.MoveNext() && right.MoveNext())
                    {
                        if (!AreEqual(left.Current, right.Current))
                            return false;
                    }
                }
                return true;
            case JsonValueKind.Object:
                var expectedProperties = expected.EnumerateObject().ToList();
                if (expectedProperties.Count != actual.EnumerateObject().Count())
                    return false;
                foreach (var property in expectedProperties)
                {
                    if (!actual.TryGetProperty(property.Name, out var other) || !AreEqual(property.Value, other))
                        return false;
                }
                return true;
            default:
                return true; // true, false, null: same kind is enough
        }
    }

    public static bool TryGetNumber(JsonElement value, out decimal number)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetDecimal(out number))
                return true;
            if (value.TryGetDouble(out var d) && d is >= (double)decimal.MinValue and <= (double)decimal.MaxValue)
            {
                number = (decimal)d;
                return true;
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            return decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        number = 0;
        return false;
    }
}
