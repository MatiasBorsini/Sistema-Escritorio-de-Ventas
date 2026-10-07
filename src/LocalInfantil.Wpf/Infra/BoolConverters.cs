using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LocalInfantil.Application.Contracts;

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
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
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

/// <summary>
/// Igual que <see cref="MinorToArsConverter"/> pero con signo explícito
/// para magnitudes que pueden ser positivas o negativas (ajustes).
/// </summary>
public sealed class MinorConSignoToArsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long l)
        {
            var texto = ((decimal)Math.Abs(l) / 100m).ToString("N2", new CultureInfo("es-AR"));
            return l == 0 ? $"ARS {texto}" : l > 0 ? $"+ARS {texto}" : $"-ARS {texto}";
        }

        return "—";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CajaTextoHumanoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string texto) return value ?? string.Empty;
        return texto switch
        {
            MediosCaja.Efectivo => "Efectivo",
            MediosCaja.Transferencia => "Transferencia",
            MediosCaja.Debito => "Débito",
            MediosCaja.Credito => "Crédito",
            MediosCaja.Billetera => "Billetera virtual",
            MediosPago.MercadoPago => "Mercado Pago",
            "abierta" => "Abierta",
            "cerrada" => "Cerrada",
            "venta" => "Venta",
            "ingreso" => "Ingreso",
            "egreso" => "Egreso",
            "reintegro" => "Reintegro",
            "correccion" => "Corrección de medio",
            _ => texto,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
