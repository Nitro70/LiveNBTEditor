using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LiveNBT.Protocol;

namespace LiveNBT.App;

/// <summary>Tree-row type chip background: numeric=blue, string=green, container=amber, array=purple.</summary>
public sealed class TypeChipBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is NbtType type ? type switch
        {
            NbtType.Byte or NbtType.Short or NbtType.Int or NbtType.Long
                or NbtType.Float or NbtType.Double => "ChipNumericBrush",
            NbtType.String => "ChipStringBrush",
            NbtType.List or NbtType.Compound => "ChipContainerBrush",
            _ => "ChipArrayBrush",
        } : "Bg3Brush";
        return Application.Current.TryFindResource(key) ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
