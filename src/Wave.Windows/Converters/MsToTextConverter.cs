using System.Globalization;
using System.Windows.Data;

namespace Wave.Windows.Converters;

/// <summary>
/// Convierte milisegundos (long) a string de tiempo "m:ss" o "h:mm:ss" si supera la hora.
///
/// Ejemplos:
///   0       → "0:00"
///   1000    → "0:01"
///   60000   → "1:00"
///   3661000 → "1:01:01"
/// </summary>
public class MsToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long ms)
        {
            return FormatMs(ms);
        }
        if (value is int intMs)
        {
            return FormatMs(intMs);
        }
        if (value is double doubleMs)
        {
            return FormatMs((long)doubleMs);
        }
        return "0:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>Formatea milisegundos a "m:ss" o "h:mm:ss".</summary>
    private static string FormatMs(long ms)
    {
        if (ms < 0) ms = 0;
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
