namespace KairosoftGameToolbox.Services;

public static class ReleaseInfo
{
    public const string Version = "1.0.3";
    public const bool IsBeta = true;
    public static string DisplayVersion => FormatDisplay(Version, IsBeta);

    public static string FormatDisplay(string version, bool beta)
    {
        var number = System.Version.Parse(version);
        if (number.Build < 1 || number.Revision >= 0) throw new ArgumentException("Version must contain three parts and z must be positive.", nameof(version));
        return $"v{number.Major}.{number.Minor}" + (beta ? $" Beta {number.Build}" : " Release");
    }
}
