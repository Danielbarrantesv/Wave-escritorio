using System.Globalization;
using System.Windows.Data;

namespace Wave.Windows.Converters;

/// <summary>
/// Convierte bool → string "ON" / "OFF".
/// Útil para botones de toggle.
/// </summary>
public class BoolToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? "ON" : "OFF";
        }
        return "OFF";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
