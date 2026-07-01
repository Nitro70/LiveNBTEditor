using System.Globalization;
using System.Windows.Data;

namespace LiveNBT.App;

/// <summary>"minecraft:diamond_sword" -> "diamond sword" for the small slot label.</summary>
public sealed class ShortIdConverter : IValueConverter
{
    public static readonly ShortIdConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        string id = value as string ?? "";
        int colon = id.IndexOf(':');
        return (colon >= 0 ? id[(colon + 1)..] : id).Replace('_', ' ');
    }
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}
