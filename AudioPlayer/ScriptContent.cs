using System.IO;
using System.Text.Json;

namespace AudioPlayer;

// Script is a companion view, not another playable article or a Passage SID source.
public sealed record ScriptArticle(string Title, IReadOnlyList<ScriptParagraph> Paragraphs);
public sealed record ScriptParagraph(IReadOnlyList<ScriptSentence> Sentences);
public sealed record ScriptSentence(string Text, IReadOnlyList<ScriptTimedUnit> TimedUnits);

// Offset is a best-effort rendering position within the original sentence.
// Failed alignment does not invalidate an otherwise well-formed Script JSON.
public sealed record ScriptTimedUnit(string Text, double? Start, double? End, int Offset)
{
    public int Length => Text.Length;
    public bool CanColor => Offset >= 0 && Start.HasValue && End.HasValue;
}

internal static class ScriptContent
{
    internal static ScriptArticle? Find(string audioPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(audioPath))!;
        foreach (string path in Directory.EnumerateFiles(directory, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal))
        {
            JsonDocument document;
            try { document = JsonDocument.Parse(File.ReadAllText(path)); }
            catch (JsonException) { continue; } // Cannot identify malformed unrelated JSON.
            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("filetype", out var type)
                    && type.ValueKind == JsonValueKind.String && type.GetString() == "script")
                    return Parse(root);
            }
        }
        return null;
    }

    internal static ScriptArticle Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Script 根节点必须为对象。");

        var missing = new List<string>();
        if (!root.TryGetProperty("filetype", out var type)) missing.Add("filetype");
        if (!root.TryGetProperty("title", out var title)) missing.Add("title");
        if (missing.Count > 0)
            throw new InvalidDataException("Script 缺少必需字段：" + string.Join("、", missing) + "。");
        if (type.ValueKind != JsonValueKind.String || type.GetString() != "script")
            throw new InvalidDataException("Script 的 filetype 必须为 \"script\"。");
        if (title.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Script 的 title 必须为字符串（可以为空）。");

        if (!root.TryGetProperty("paragraphs", out var source) ||
            source.ValueKind != JsonValueKind.Array || source.GetArrayLength() == 0)
            throw new InvalidDataException("Script 的 paragraphs 必须为非空二维数组。");

        var paragraphs = new List<ScriptParagraph>();
        foreach (var paragraph in source.EnumerateArray())
        {
            if (paragraph.ValueKind != JsonValueKind.Array || paragraph.GetArrayLength() == 0)
                throw new InvalidDataException("Script 每个段落必须包含句子数组。");
            var sentences = new List<ScriptSentence>();
            foreach (var sentence in paragraph.EnumerateArray())
            {
                if (sentence.ValueKind != JsonValueKind.Object ||
                    !sentence.TryGetProperty("text", out var textValue) ||
                    textValue.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(textValue.GetString()))
                    throw new InvalidDataException("Script 句子缺少有效的 text 字符串。");
                if (!sentence.TryGetProperty("timed_units", out var unitsValue) ||
                    unitsValue.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Script 句子缺少 timed_units 数组。");

                string text = textValue.GetString()!;
                int cursor = 0;
                var units = new List<ScriptTimedUnit>();
                foreach (var unit in unitsValue.EnumerateArray())
                {
                    if (unit.ValueKind != JsonValueKind.Array || unit.GetArrayLength() != 3)
                        throw new InvalidDataException("Script 的 timed_units 项必须为三元数组。");
                    JsonElement[] fields = unit.EnumerateArray().ToArray();
                    if (fields[0].ValueKind != JsonValueKind.String ||
                        string.IsNullOrEmpty(fields[0].GetString()))
                        throw new InvalidDataException("Script 时间单元的显示文本必须为非空字符串。");

                    double? start = ReadTime(fields[1]);
                    double? end = ReadTime(fields[2]);
                    if (start.HasValue != end.HasValue ||
                        (start.HasValue && end!.Value <= start.Value))
                        throw new InvalidDataException("Script 时间区间必须同时为 null，或满足 0 <= start < end。");

                    string display = fields[0].GetString()!;
                    int offset = text.IndexOf(display, cursor, StringComparison.Ordinal);
                    // The producer guarantees alignment. A mismatch must not
                    // reject the JSON or invent a replacement substring.
                    if (offset >= 0) cursor = offset + display.Length;
                    units.Add(new ScriptTimedUnit(display, start, end, offset));
                }
                sentences.Add(new ScriptSentence(text, units));
            }
            paragraphs.Add(new ScriptParagraph(sentences));
        }
        return new ScriptArticle(title.GetString()!, paragraphs);
    }

    private static double? ReadTime(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out double time) || !double.IsFinite(time) || time < 0)
            throw new InvalidDataException("Script 时间必须是有限非负数字或 null。");
        return time;
    }

}
