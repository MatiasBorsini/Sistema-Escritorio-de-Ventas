using System.Globalization;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.Printing;

public enum FormatoComprobanteCambio { A4, Ticket80mm }

/// <summary>Presentación de snapshots registrados; compartida por ambos formatos.</summary>
public sealed class ComprobanteCambioPresentacion(DocumentoTicketCambio documento)
{
    public DocumentoTicketCambio Datos { get; } = documento ?? throw new ArgumentNullException(nameof(documento));
    public IReadOnlyList<LineaTicketCambio> Entradas => Datos.Lineas.Where(x => x.Direccion == "entrada").ToArray();
    public IReadOnlyList<LineaTicketCambio> Salidas => Datos.Lineas.Where(x => x.Direccion == "salida").ToArray();
    public string Tipo => Datos.DiferenciaMinor == 0 ? "Sin diferencia" : Salidas.Count == 0 ? "Devolución" : "Cambio";
    public string Titulo => Tipo switch
    {
        "Devolución" => "COMPROBANTE INTERNO DE DEVOLUCIÓN",
        "Sin diferencia" => "COMPROBANTE INTERNO — SIN DIFERENCIA",
        _ => "COMPROBANTE INTERNO DE CAMBIO"
    };
    public string ResultadoFinal => Datos.DiferenciaMinor > 0 ? "A cobrar" : Datos.DiferenciaMinor == 0 ? "Sin diferencia"
        : Datos.ImporteAplicadoDeudaMinor == -Datos.DiferenciaMinor ? "Aplicado a deuda"
        : Datos.ImporteAplicadoDeudaMinor > 0 ? "Reintegro y crédito a deuda" : "A reintegrar";
    public string FechaTexto => DateTimeOffset.TryParse(Datos.Fecha, out var fecha)
        ? fecha.ToLocalTime().ToString("dd/MM/yyyy · HH:mm", CultureInfo.GetCultureInfo("es-AR")) : Datos.Fecha;
    public static string Importe(long minor) => $"ARS {(minor / 100m).ToString("N2", CultureInfo.GetCultureInfo("es-AR"))}";
    public const string Pie = "Operación no fiscal. Conservá este comprobante como referencia interna.";
}
