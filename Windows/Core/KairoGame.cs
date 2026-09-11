namespace KairosoftGameToolbox.Models;

/// <summary>Steam Web API 返回的单款游戏游玩记录（分钟）。</summary>
public sealed record GamePlayStats(
    uint AppId,
    long PlaytimeForeverMinutes,
    long? PlaytimeTwoWeeksMinutes,
    long? LastPlayedUnix);

public enum OwnershipStatus
{
    Unknown,
    Owned,
    NotOwned,
}

/// <summary>库中展示的一款开罗游戏（可能已安装或仅存在于 AppID 表）。</summary>
public sealed class KairoGame
{
    public uint AppId { get; init; }

    /// <summary>启动器界面显示名：简体中文名；没有目录翻译时回退为英文名。</summary>
    private readonly string name = "";
    public string Name { get => Services.L.GameName(AppId, name); init => name = value; }

    /// <summary>Steam 英文名，仅用于双语搜索、稳定排序和内部标识。</summary>
    public string EnglishName { get; init; } = "";

    /// <summary>目录提供的无声调拼音，音节用空格分隔，保留数字及英文后缀。</summary>
    public string PinyinName { get; init; } = "";

    /// <summary>标准全名格式，界面当前不直接显示。</summary>
    public string FullName
        => string.IsNullOrWhiteSpace(EnglishName) || EnglishName == Name
            ? Name
            : $"{Name} ({EnglishName})";

    /// <summary>无游玩时间时沿用英文名排序；测试或未收录游戏没有英文名时回退显示名。</summary>
    public string SortName => string.IsNullOrWhiteSpace(EnglishName) ? Name : EnglishName;

    /// <summary>是否已安装（文件检测或 AppID 表命中且存在 appmanifest）。</summary>
    public bool IsInstalled { get; set; }

    /// <summary>当前 Steam 账号是否拥有该游戏；无法通过 API 确认时为 Unknown。</summary>
    public OwnershipStatus Ownership { get; set; } = OwnershipStatus.Unknown;

    /// <summary>安装目录（未安装为 null）。</summary>
    public string? InstallDir { get; set; }
    public string? NonSteamDirectory { get; set; }
    public bool IsNonSteam => string.IsNullOrWhiteSpace(LibraryPath) && !string.IsNullOrWhiteSpace(NonSteamDirectory);

    /// <summary>所在 Steam 库根目录（含 steamapps 的目录）。</summary>
    public string? LibraryPath { get; set; }

    /// <summary>存档目录（saves/&lt;SteamID&gt;，未安装或没有合法目录为 null）。</summary>
    public string? SaveDir { get; set; }

    /// <summary>本地 appmanifest 中的最近游玩时间（Unix 秒），作为 API 不可用时的排序依据。</summary>
    public long? LastPlayedUnix { get; set; }

    /// <summary>可选游玩记录（设置开启且拉取成功时填充）。</summary>
    public GamePlayStats? Stats { get; set; }

    /// <summary>面向界面的状态文案；无法确认拥有状态时不得显示为“未拥有”。</summary>
    public string LibraryStatusText
        => Services.L.T(IsInstalled ? "已安装"
            : Ownership switch
            {
                OwnershipStatus.Owned => "已拥有",
                OwnershipStatus.NotOwned => "未拥有",
                _ => "状态未知",
            });

    /// <summary>卡片封面饱和度：已安装原色，已拥有但未安装低饱和，未拥有（含未知）灰度。</summary>
    public double CoverSaturation
        => IsInstalled ? 1.0
            : Ownership == OwnershipStatus.Owned ? 0.25
            : 0.0;
}
