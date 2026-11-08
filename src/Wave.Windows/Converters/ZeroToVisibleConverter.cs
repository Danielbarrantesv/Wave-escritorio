using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wave.Windows.Converters;

/// <summary>
/// Convierte un entero a Visibility.
/// - Si el valor es 0 → Visible (empty state)
/// - Si el valor > 0 → Collapsed (hay canciones, mostrar la lista)
///
/// Útil para mostrar el "empty state" de la biblioteca cuando no hay canciones.
/// </summary>
public class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (value is long longCount)
        {
            return longCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // No implementado: la conversión inversa no tiene sentido para Visibility
        throw new NotSupportedException();
    }
}

/// <summary>
/// Convierte bool → Visibility.
/// - true → Visible
/// - false → Collapsed
///
/// Útil para mostrar/ocultar elementos basados en flags booleanos (IsLoading, HasLyrics, etc.).
/// </summary>
public class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

