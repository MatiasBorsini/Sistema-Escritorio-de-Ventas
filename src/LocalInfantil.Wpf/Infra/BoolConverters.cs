using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LocalInfantil.Wpf.Infra;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolToVisibilityInvertedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Invierte bool (para IsEnabled/IsChecked, no visibilidad).</summary>
public sealed class BoolInvertidoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null || (value is string s && string.IsNullOrWhiteSpace(s)) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CeroVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Diferencia de conteo a color: 0 neutro, + éxito, − error. Usa los
/// brushes del diccionario; el signo/texto numérico va aparte en la vista.</summary>
public sealed class DiferenciaColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var clave = value is long l && l > 0 ? "Color.SuccessTextBrush"
            : value is long n && n < 0 ? "Color.ErrorTextBrush"
            : "Color.TextSecondaryBrush";
        if (System.Windows.Application.Current?.TryFindResource(clave) is System.Windows.Media.Brush b)
            return b;
        return System.Windows.Media.Brushes.Gray;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Fecha ISO a "dd/MM · HH:mm" local; si no parsea, texto original.</summary>
public sealed class FechaCortaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && DateTimeOffset.TryParse(s, out var f))
            return f.ToLocalTime().ToString("dd/MM · HH:mm");
        return value ?? "—";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class MinorToArsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long l) return $"ARS {((decimal)l / 100m).ToString("N2", new CultureInfo("es-AR"))}";
        return "—";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
