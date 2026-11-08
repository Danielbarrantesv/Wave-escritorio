using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Wave.Windows.Converters;

/// <summary>
/// Convierte bool → Brush.
/// - true (línea activa) → AccentBrush (color de acento)
/// - false (línea inactiva) → OnSurfaceVariantBrush (gris)
///
/// Se usa en LyricsView para resaltar la línea que se está cantando.
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b
                ? Application.Current.FindResource("AccentBrush")
                : Application.Current.FindResource("OnSurfaceVariantBrush");
        }
        return Application.Current.FindResource("OnSurfaceVariantBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convierte bool → double (font size).
/// - true (línea activa) → 18 (más grande)
/// - false (línea inactiva) → 14 (más pequeño)
/// </summary>
public class BoolToFontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? 18.0 : 14.0;
        }
        return 14.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convierte bool → FontWeight.
/// - true (línea activa) → Bold
/// - false (línea inactiva) → Normal
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? FontWeights.Bold : FontWeights.Normal;
        }
        return FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
