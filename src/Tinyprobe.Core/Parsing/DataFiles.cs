using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Tinyprobe.Core.Parsing;

/// <summary>The data files of <c>for_each: { file: … }</c>: rows of named values, as YAML nodes like a written item.</summary>
internal static class DataFiles
{
    /// <summary>A JSON list of objects (JSON is YAML, so the YAML parser reads it); null when it is not one.</summary>
    public static List<List<KeyValuePair<string, YamlNode>>>? ReadJson(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlSequenceNode sequence)
            return null;

        var rows = new List<List<KeyValuePair<string, YamlNode>>>();
        foreach (var item in sequence.Children)
        {
            if (item is not YamlMappingNode mapping || mapping.Children.Keys.Any(k => k is not YamlScalarNode))
                return null;
            rows.Add(mapping.Children.Select(c => KeyValuePair.Create(((YamlScalarNode)c.Key).Value ?? "", c.Value)).ToList());
        }
        return rows;
    }

    /// <summary>
    /// A CSV file (RFC 4180: commas, double quotes, "" for a quote): the first line names the columns.
    /// Values are text, as quoted YAML strings. Empty lines are skipped; a missing value is empty.
    /// </summary>
    public static List<List<KeyValuePair<string, YamlNode>>>? ReadCsv(string text)
    {
        var lines = Records(text).Where(r => r.Count > 1 || r[0].Length > 0).ToList();
        if (lines.Count == 0)
            return null;

        var header = lines[0];
        return lines.Skip(1)
            .Select(record => header.Select((name, column) => KeyValuePair.Create(
                name.Trim(), (YamlNode)new YamlScalarNode(column < record.Count ? record[column] : "") { Style = ScalarStyle.DoubleQuoted })).ToList())
            .ToList();
    }

    private static IEnumerable<List<string>> Records(string text)
    {
        var record = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                    quoted = false;
                else
                    field.Append(c);
            }
            else if (c == '"')
                quoted = true;
            else if (c == ',')
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                record.Add(field.ToString());
                field.Clear();
                yield return record;
                record = new List<string>();
            }
            else
                field.Append(c);
        }
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            yield return record;
        }
    }
}
