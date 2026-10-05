namespace LocalInfantil.Application.Contracts;

/// <summary>
/// Comprobante interno no fiscal, reconstruible por completo desde datos
/// persistidos (snapshots de la venta). Reimprimirlo nunca muta registros.
/// </summary>
public sealed record LineaTicketVenta(
    string CodigoInterno,
    string NombreProducto,
    string? Talle,
    string? Color,
    long Cantidad,
    long PrecioUnitarioMinor,
    long SubtotalMinor);

public sealed record PagoTicketVenta(
    string Medio,
    long ImporteMinor,
    long? RecibidoMinor,
    long? VueltoMinor);

public sealed record DocumentoTicketVenta(
    string Numero,
    string CreadaEn,
    string Estado,
    string? VendidoPor,
    IReadOnlyList<LineaTicketVenta> Lineas,
    long TotalMinor,
    IReadOnlyList<PagoTicketVenta> Pagos,
    string? MotivoAnulacion,
    string? ClienteNombre = null,
    long ImportePagadoMinor = 0,
    long ImporteCuentaCorrienteMinor = 0,
    string? VencimientoCuentaCorriente = null);
