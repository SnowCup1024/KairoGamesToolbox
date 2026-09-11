using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KairosoftGameToolbox.Services;

public sealed class ModLogReader(string path) : IDisposable
{
    private long offset;
    private DateTime creation;
    private Decoder decoder = Encoding.UTF8.GetDecoder();
    private string pending = "";
    public IReadOnlyList<string> Read()
    {
        if (!File.Exists(path)) return Array.Empty<string>();
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var created = File.GetCreationTimeUtc(path);
        if (created != creation || input.Length < offset) { offset = Math.Max(0, input.Length - 65536); pending = ""; decoder.Reset(); creation = created; }
        input.Position = offset;
        var bytes = new byte[65536]; int count = input.Read(bytes); offset += count;
        var chars = new char[Encoding.UTF8.GetMaxCharCount(count)]; int length = decoder.GetChars(bytes, 0, count, chars, 0);
        var lines = (pending + new string(chars, 0, length)).Split('\n'); pending = lines[^1];
        if (pending.Length > 16384) pending = "";
        return lines.Take(lines.Length - 1).Select(l => l.TrimEnd('\r')).ToArray();
    }
    public static (string Text, bool Error, bool Resource) Format(string line, GameModDefinition definition, string language)
    {
        bool error = line.Contains("Error", StringComparison.OrdinalIgnoreCase) || line.Contains("failed", StringComparison.OrdinalIgnoreCase);
        if (definition.Development || error || !line.Contains("ResourceResult |")) return (line, error, false);
        var fields = new Dictionary<string, string>();
        foreach (Match match in Regex.Matches(line, @"(?:^|\|)\s*(\w+)=([^|]*)"))
            if (!fields.TryAdd(match.Groups[1].Value, match.Groups[2].Value.Trim())) return (line, error, false);
        if (!fields.TryGetValue("resource", out var id) || !fields.TryGetValue("delta", out var delta)
            || !fields.TryGetValue("time", out var time) || !DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)
            || !decimal.TryParse(delta, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) return (line, error, false);
        var feature = definition.Features.SingleOrDefault(f => f.Id == id);
        if (feature == null) return (line, error, false);
        string template = BundledModService.Text(definition.LogTemplates, language);
        var timeFormat = language.StartsWith("zh") ? "yyyy 年 M 月 d 日 HH:mm:ss" : "yyyy-MM-dd HH:mm:ss";
        string action = definition.LogTemplates.GetValueOrDefault(language + (amount < 0 ? ".decrease" : ".increase")) ?? (amount < 0 ? "-" : "+");
        if (amount == decimal.MinValue) return (line, error, false);
        string text;
        try { text = string.Format(CultureInfo.InvariantCulture, template, timestamp.LocalDateTime.ToString(timeFormat),
            action, BundledModService.Text(feature.LogNames ?? feature.Names, language), Math.Abs(amount)); }
        catch (FormatException) { return (line, error, false); }
        return (text, false, true);
    }
    public void Dispose() { }
}
