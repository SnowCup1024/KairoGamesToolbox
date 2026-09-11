using System.Text;
using KairosoftGameToolbox.Models;

namespace KairosoftGameToolbox.Services;

/// <summary>游戏库模糊搜索：中文、英文、全拼和拼音首字母分别参与匹配。</summary>
public static class GameSearch
{
    public static bool Matches(KairoGame game, string? query)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0) return true;

        if (IsSubsequence(Normalize($"{game.Name} {game.EnglishName}"), normalizedQuery)) return true;
        if (L.GameSearchNames(game.AppId).Any(name => IsSubsequence(Normalize(name), normalizedQuery))) return true;
        if (string.IsNullOrWhiteSpace(game.PinyinName)) return false;

        // 顺序子序列匹配同时支持全拼、首字母和省略音节，无需重复构造首字母索引。
        return IsSubsequence(Normalize(game.PinyinName), normalizedQuery);
    }

    private static bool IsSubsequence(string candidate, string query)
    {
        var queryIndex = 0;
        foreach (var character in candidate)
        {
            if (character != query[queryIndex]) continue;
            queryIndex++;
            if (queryIndex == query.Length) return true;
        }
        return false;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }
}
