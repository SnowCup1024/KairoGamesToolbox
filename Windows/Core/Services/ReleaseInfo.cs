namespace KairosoftGameToolbox.Services;

public static class ReleaseInfo
{
    public const string Version = "1.0.7";
    public const bool IsBeta = false;
    public static string Changelog { get; } = ReadChangelog();

    private static string ReadChangelog()
    {
        using var stream = typeof(ReleaseInfo).Assembly.GetManifestResourceStream("CHANGELOG.md")
            ?? throw new InvalidDataException("Bundled CHANGELOG.md is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string DisplayVersion => FormatDisplay(Version, IsBeta);

    public static string FormatDisplay(string version, bool beta)
    {
        var number = System.Version.Parse(version);
        if (number.Build < 1 || number.Revision >= 0) throw new ArgumentException("Version must contain three parts and z must be positive.", nameof(version));
        return $"v{number.Major}.{number.Minor}" + (beta ? $" Beta {number.Build}" : " Release");
    }
}
