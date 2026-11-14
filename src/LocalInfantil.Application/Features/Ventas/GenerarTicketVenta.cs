using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Ventas;

/// <summary>
/// Arma el comprobante interno desde una ficha ya consultada (solo lectura
/// agregada, sin reglas nuevas). La reimpresión usa este mismo camino.
/// </summary>
public sealed class GenerarTicketVenta
{
    private readonly ConsultasVentas _consultas;

    public GenerarTicketVenta(ConsultasVentas consultas)
    {
        _consultas = consultas;
    }

    public async Task<DocumentoTicketVenta?> EjecutarAsync(string ventaId, string? idSesion, CancellationToken cancellationToken = default)
    {
        var ficha = await _consultas.ObtenerFichaAsync(ventaId, idSesion, cancellationToken);
        if (ficha is null)
        {
            return null;
        }

        return new DocumentoTicketVenta(
            ficha.Venta.Numero,
            ficha.Venta.CreadaEn,
            ficha.Venta.Estado,
            ficha.Venta.CreadoPorNombre,
            ficha.Lineas.Select(l => new LineaTicketVenta(
                l.CodigoInterno,
                l.NombreProducto,
                l.Talle,
                l.Color,
                l.Cantidad,
                l.PrecioUnitarioMinor,
                l.SubtotalMinor)).ToList(),
            ficha.Venta.TotalMinor,
            ficha.Pagos.Select(p => new PagoTicketVenta(
                p.Medio,
                p.ImporteMinor,
                p.RecibidoMinor,
                p.VueltoMinor)).ToList(),
            ficha.Anulacion?.Motivo,
            ficha.Venta.ClienteNombreSnapshot,
            ficha.Pagos.Sum(p => p.ImporteMinor),
            ficha.Venta.ImporteCuentaCorrienteMinor ?? 0,
            ficha.Venta.VencimientoCuentaCorriente);
    }
}
