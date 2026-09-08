using System.Text;

namespace KairosoftGameToolbox.Services;

/// <summary>
/// Valve VDF（KeyValues）文本解析器，仅覆盖本启动器需要的子集：
/// 嵌套块 {"key" { ... }}、键值对 "key" "value"、// 注释、\" 与 \\ 转义。
/// 结果统一为 Dictionary&lt;string, object&gt;，值为 string 或嵌套 Dictionary。
/// </summary>
public static class VdfParser
{
    public static Dictionary<string, object> Parse(string text)
    {
        var tokens = Tokenize(text);
        var result = new Dictionary<string, object>();
        int pos = 0;
        ParseBlock(tokens, ref pos, result);
        return result;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }

            if (c == '{' || c == '}')
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }

            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        // 仅 \" 与 \\ 是转义；其余反斜杠按字面保留（Steam 路径形如 D:\SteamLibrary）
                        char n = text[i + 1];
                        if (n == '"') { sb.Append('"'); i += 2; continue; }
                        if (n == '\\') { sb.Append('\\'); i += 2; continue; }
                    }
                    sb.Append(text[i]);
                    i++;
                }
                i++; // 跳过闭合引号
                tokens.Add(sb.ToString());
                continue;
            }

            // 未加引号的裸 token（容错，理论上 VDF 中不存在）
            var raw = new StringBuilder();
            while (i < text.Length && !char.IsWhiteSpace(text[i])
                   && text[i] != '{' && text[i] != '}' && text[i] != '"')
            {
                raw.Append(text[i]);
                i++;
            }
            if (raw.Length > 0) tokens.Add(raw.ToString());
        }
        return tokens;
    }

    private static void ParseBlock(List<string> tokens, ref int pos, Dictionary<string, object> target)
    {
        while (pos < tokens.Count)
        {
            string tok = tokens[pos];
            if (tok == "}") { pos++; return; }

            pos++; // 当前 token 是 key
            if (pos >= tokens.Count) return;

            string next = tokens[pos];
            if (next == "{")
            {
                pos++;
                var child = new Dictionary<string, object>();
                ParseBlock(tokens, ref pos, child);
                target[tok] = child;
            }
            else
            {
                pos++;
                target[tok] = next;
            }
        }
    }
}
