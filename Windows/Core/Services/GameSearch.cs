using System.Text;
using KairosoftGameToolbox.Models;

namespace KairosoftGameToolbox.Services;

/// <summary>游戏库双语模糊搜索：中文名和英文名均参与匹配。</summary>
public static class GameSearch
{
    public static bool Matches(KairoGame game, string? query)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0) return true;

        var candidate = Normalize($"{game.Name} {game.EnglishName}");
        return IsSubsequence(candidate, normalizedQuery);
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
