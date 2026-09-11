using System.Globalization;

namespace KairoMods.Observer;

internal sealed class StateDiff
{
    private readonly Dictionary<string, decimal> values = new();

    public string? Observe(string name, decimal value)
    {
        var known = values.TryGetValue(name, out var before);
        values[name] = value;
        if (known && before == value) return null;
        return known
            ? $"field={name} | before={Format(before)} | after={Format(value)} | delta={Format(value - before)}"
            : $"field={name} | baseline={Format(value)}";
    }

    public void Reset() => values.Clear();
    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
