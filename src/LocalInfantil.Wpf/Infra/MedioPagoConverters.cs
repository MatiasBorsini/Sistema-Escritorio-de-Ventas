using System.Globalization;
using System.Windows.Data;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.Infra;

/// <summary>Etiqueta amigable del medio (sin exponer IDs técnicos).</summary>
public sealed class MedioPagoAEtiquetaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        MediosPago.Efectivo => "Efectivo",
        MediosPago.Transferencia => "Transferencia",
        MediosPago.Debito => "Débito",
        MediosPago.Credito => "Crédito",
        MediosPago.Billetera => "Billetera",
        MediosPago.MercadoPago => "Mercado Pago Point",
        string s => s,
        _ => string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
