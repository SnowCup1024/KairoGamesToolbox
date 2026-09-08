namespace KairosoftGameToolbox.Services;

/// <summary>集中生成由数值 AppID 构成的 Steam 链接。</summary>
public static class SteamLinkService
{
    public static string Run(uint appId) => $"steam://run/{appId}";

    public static string Install(uint appId) => $"steam://install/{appId}";

    public static string Store(uint appId) => $"https://store.steampowered.com/app/{appId}/";
}
