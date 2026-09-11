namespace KairosoftGameToolbox.Services;

public static class ReleaseInfo
{
    public const string Version = "1.0.1";
    public const bool IsBeta = true;
    public static string DisplayVersion => "v" + Version + (IsBeta ? " (Beta)" : "");
}
