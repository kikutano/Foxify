using System.Text.Json;
using Tinyprobe.Core.Execution;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Tinyprobe.Core.Protocol;

/// <summary>
/// <c>inspect --json --source</c>: the file as written, as JSON, and where its blocks are, for editors that change
/// a file with forms (docs/specs/0018-inspect-source.md). tinyprobe stays a reader: editors write the text.
/// </summary>
public static class SourceJson
{
    /// <summary>Writes <c>source</c> and <c>blocks</c>, or nothing when the file's YAML cannot be read.</summary>
    public static void Write(Utf8JsonWriter w, string file)
    {
        string text;
        YamlMappingNode root;
        try
        {
            text = File.ReadAllText(file);
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode mapping) return;
            if (!HasScalarKeys(mapping)) return;
            root = mapping;
        }
        catch (Exception e) when (e is YamlException or IOException)
        {
            return;
        }

        w.WritePropertyName("source");
        TemplateRenderer.ToLiteral(root).WriteTo(w);

        var positions = new Positions(text);
        w.WriteStartArray("blocks");
        foreach (var (keyNode, value) in root.Children)
        {
            var key = ((YamlScalarNode)keyNode).Value ?? "";
            Block(w, positions, "key", key, null, keyNode.Start, positions.End(value));

            if (key is "auth" or "endpoints" && value is YamlMappingNode entries)
            {
                foreach (var (childKey, childValue) in entries.Children)
                    Block(w, positions, key == "auth" ? "auth" : "endpoint", ((YamlScalarNode)childKey).Value ?? "", null, childKey.Start, positions.End(childValue));
            }
            else if (key is "steps" or "sequence" && value is YamlSequenceNode steps)
            {
                for (var index = 0; index < steps.Children.Count; index++)
                {
                    var item = steps.Children[index];
                    Block(w, positions, "step", null, index, positions.Dash(item.Start), positions.End(item));
                }
            }
        }
        w.WriteEndArray();
    }

    private static void Block(Utf8JsonWriter w, Positions positions, string kind, string? name, int? index, Mark start, Mark end)
    {
        w.WriteStartObject();
        w.WriteString("kind", kind);
        if (name is not null) w.WriteString("name", name);
        if (index is not null) w.WriteNumber("index", index.Value);
        Position(w, "start", start);
        Position(w, "end", end);
        w.WriteEndObject();
    }

    private static void Position(Utf8JsonWriter w, string name, Mark mark)
    {
        w.WriteStartObject(name);
        w.WriteNumber("line", mark.Line);
        w.WriteNumber("column", mark.Column);
        w.WriteEndObject();
    }

    // Complex keys ({a: 1}: x) are valid YAML but never tinyprobe: the file is left without source.
    private static bool HasScalarKeys(YamlNode node) => node switch
    {
        YamlMappingNode mapping => mapping.Children.All(c => c.Key is YamlScalarNode && HasScalarKeys(c.Value)),
        YamlSequenceNode sequence => sequence.Children.All(HasScalarKeys),
        _ => true
    };

    /// <summary>Positions YamlDotNet does not mark: the <c>-</c> of a list item, and the end of a value.</summary>
    private sealed class Positions(string text)
    {
        /// <summary>
        /// Just after the last character of a value. A block mapping or list ends where the next token starts, after
        /// the comments and blank lines that follow it, which belong to what comes next: the end of its last value
        /// is used instead. A flow one (<c>{ a: 1 }</c>) is marked at its start only: its end is its closing bracket.
        /// </summary>
        public Mark End(YamlNode node) => node switch
        {
            YamlMappingNode { Style: MappingStyle.Flow } mapping =>
                Close(mapping.Children.Count > 0 ? End(mapping.Children.Last().Value) : After(mapping.Start), '}'),
            YamlSequenceNode { Style: SequenceStyle.Flow } sequence =>
                Close(sequence.Children.Count > 0 ? End(sequence.Children[^1]) : After(sequence.Start), ']'),
            YamlMappingNode { Children.Count: > 0 } mapping => End(mapping.Children.Last().Value),
            YamlSequenceNode { Children.Count: > 0 } sequence => End(sequence.Children[^1]),
            _ => node.End
        };

        /// <summary>The position after the opening bracket of an empty flow collection.</summary>
        private Mark After(Mark start) => At((int)start.Index + 1);

        /// <summary>The position just after the bracket that closes a flow collection, from its last value.</summary>
        private Mark Close(Mark from, char bracket)
        {
            var i = (int)from.Index;
            while (i < text.Length && text[i] != bracket) i++;
            return At(Math.Min(i + 1, text.Length));
        }

        // Where each line starts: a position is found by binary search, so a file of thousands of steps stays fast.
        private readonly List<int> _lines = LineStarts(text);

        private static List<int> LineStarts(string text)
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < text.Length; i++)
                if (text[i] == '\n') starts.Add(i + 1);
            return starts;
        }

        private Mark At(int index)
        {
            var line = _lines.BinarySearch(index);
            if (line < 0) line = ~line - 1;
            return new Mark(index, line + 1, index - _lines[line] + 1);
        }

        /// <summary>The dash before a list item: its node starts after <c>- </c>, on the same line or a later one.</summary>
        public Mark Dash(Mark item)
        {
            var i = (int)item.Index - 1;
            while (i >= 0 && char.IsWhiteSpace(text[i])) i--;
            return i >= 0 && text[i] == '-' ? At(i) : item;
        }
    }
}
