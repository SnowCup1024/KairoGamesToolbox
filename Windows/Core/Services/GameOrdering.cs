using KairosoftGameToolbox.Models;

namespace KairosoftGameToolbox.Services;

public enum GameSectionKind
{
    Installed,
    NotInstalled,
    NotOwnedOrUnknown,
}

public sealed record GameSection(
    GameSectionKind Kind,
    string Title,
    IReadOnlyList<KairoGame> Games);

/// <summary>
/// 游戏库分区与排序：已安装 → 未安装但已拥有 → 未拥有或状态未知。
/// 每个分区都把有最近游玩时间的游戏排在前面，再按最近时间倒序；
/// 没有最近游玩时间的游戏按英文名排序。
/// </summary>
public static class GameOrdering
{
    public static List<GameSection> GroupAndOrder(IEnumerable<KairoGame> games)
    {
        var source = games.ToList();

        return new List<GameSection>
        {
            new(
                GameSectionKind.Installed,
                "已安装",
                OrderSection(source.Where(g => g.IsInstalled))),
            new(
                GameSectionKind.NotInstalled,
                "未安装",
                OrderSection(source.Where(g => !g.IsInstalled && g.Ownership == OwnershipStatus.Owned))),
            new(
                GameSectionKind.NotOwnedOrUnknown,
                "未拥有或状态未知",
                OrderSection(source.Where(g => !g.IsInstalled && g.Ownership != OwnershipStatus.Owned))),
        }
        .Where(section => section.Games.Count > 0)
        .ToList();
    }

    /// <summary>保留扁平结果供非 UI 调用和旧测试使用。</summary>
    public static List<KairoGame> Order(IEnumerable<KairoGame> games)
        => GroupAndOrder(games).SelectMany(section => section.Games).ToList();

    private static List<KairoGame> OrderSection(IEnumerable<KairoGame> games)
        => games
            .OrderBy(g => HasRecentPlayTime(g) ? 0 : 1)
            .ThenByDescending(LastPlayedOrUnknown)
            .ThenBy(g => g.SortName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.AppId)
            .ToList();

    private static bool HasRecentPlayTime(KairoGame game)
        => LastPlayedOrUnknown(game) > 0;

    private static long LastPlayedOrUnknown(KairoGame game)
        => game.Stats?.LastPlayedUnix ?? game.LastPlayedUnix ?? -1;
}
