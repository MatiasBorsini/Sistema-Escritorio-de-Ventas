namespace LocalInfantil.Application.Contracts;

/// <summary>Medios de pago admitidos por el esquema (registros manuales).</summary>
public static class MediosPago
{
    public const string Efectivo = "efectivo";
    public const string Transferencia = "transferencia";
    public const string Debito = "debito";
    public const string Credito = "credito";
    public const string Billetera = "billetera";

    public static readonly IReadOnlyList<string> Todos = new[]
    {
        Efectivo, Transferencia, Debito, Credito, Billetera,
    };
}

/// <summary>Línea pedida por la UI: el precio se revalida en la confirmación.</summary>
public sealed record NuevaLineaVenta(string VarianteId, long Cantidad, long PrecioEsperadoMinor);

/// <summary>Pago declarado: importe imputado; recibido solo para efectivo.</summary>
public sealed record NuevoPago(string Medio, long ImporteMinor, long? RecibidoMinor);

/// <summary>Variante cuyo precio vigente cambió desde que se agregó al carrito.</summary>
public sealed record DetallePrecioCambiado(
    string VarianteId,
    string CodigoInterno,
    string NombreProducto,
    long PrecioEsperadoMinor,
    long PrecioVigenteMinor);

/// <summary>Resultado de ConfirmarVenta: confirmada o abortada por precio.</summary>
public abstract record ResultadoConfirmacionVenta
{
    public sealed record Confirmada(string VentaId, string Numero, long TotalMinor) : ResultadoConfirmacionVenta;

    public sealed record PrecioCambiado(IReadOnlyList<DetallePrecioCambiado> Cambios) : ResultadoConfirmacionVenta;
}

public sealed record VentaRegistrada(
    string Id,
    string Numero,
    string OperacionId,
    string Estado,
    long TotalMinor,
    string Moneda,
    string? ReferenciaFiscal,
    string CreadaEn,
    string? ConfirmadaEn,
    string? CreadoPorNombre,
    string? ClienteId = null,
    string? ClienteNombreSnapshot = null,
    long? ImporteCuentaCorrienteMinor = null,
    string? VencimientoCuentaCorriente = null);

public sealed record LineaVentaRegistrada(
    string Id,
    string VarianteId,
    string CodigoInterno,
    string NombreProducto,
    string? Talle,
    string? Color,
    long Cantidad,
    long PrecioUnitarioMinor,
    long SubtotalMinor);

public sealed record PagoRegistrado(
    string Id,
    string Medio,
    long ImporteMinor,
    long? RecibidoMinor)
{
    /// <summary>Vuelto derivable (solo efectivo con recibido).</summary>
    public long? VueltoMinor => RecibidoMinor.HasValue ? RecibidoMinor.Value - ImporteMinor : null;
}

public sealed record ReversionVentaRegistrada(
    string Id,
    string VentaId,
    string OperacionId,
    string Motivo,
    string? CreadoPorNombre,
    string CreadoEn);

public sealed record FichaVenta(
    VentaRegistrada Venta,
    IReadOnlyList<LineaVentaRegistrada> Lineas,
    IReadOnlyList<PagoRegistrado> Pagos,
    ReversionVentaRegistrada? Anulacion);

public sealed record ResumenVenta(
    string Id,
    string Numero,
    string CreadaEn,
    long TotalMinor,
    string Estado,
    string? CreadoPorNombre,
    bool Anulada,
    int CantidadLineas);
