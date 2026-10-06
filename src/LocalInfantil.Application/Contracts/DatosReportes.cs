namespace LocalInfantil.Application.Contracts;

/// <summary>
/// Read models de reportes: agregados de solo lectura derivados de los
/// registros transaccionales. Sin tablas resumen persistidas.
/// Importes en minor (centavos). Sin costos ni márgenes (D-07 pendiente).
/// </summary>

/// <summary>Resumen del período con semántica por fecha del evento.</summary>
public sealed record ResumenVentasPeriodo(
    long BrutasMinor,
    int CantidadVentas,
    long AnulacionesMinor,
    int CantidadAnulaciones,
    long CreditoDevolucionesMinor,
    long SalidasCambioMinor,
    long NetasMinor,
    long CobradoMinor,
    long ReintegrosMinor,
    long FlujoNetoMinor);

/// <summary>Una fila por día local dentro del período consultado.</summary>
public sealed record FilaDiaria(
    string Fecha,
    long BrutasMinor,
    long AjustesMinor,
    long NetasMinor,
    long CobrosMinor,
    long ReintegrosMinor);

/// <summary>
/// Fila de detalle: ventas creadas en el rango más ajustes ocurridos en el
/// rango (anulaciones y cambios). Cobrado solo cuando es atribuible sin
/// inventar (pagos iniciales de la venta; nunca abonos compartidos).
/// </summary>
public sealed record DetalleVentaReporte(
    string Fecha,
    string Tipo,
    string Referencia,
    string? ClienteNombre,
    long BrutaMinor,
    long AjustesMinor,
    long NetoMinor,
    long? CobradoMinor,
    string Estado);

/// <summary>Desglose por medio con medio vigente (corrección aplicada).</summary>
public sealed record DesgloseMedio(
    string Medio,
    long VentasMinor,
    long AbonosMinor,
    long CambiosCobrosMinor,
    long ReintegrosMinor,
    long NetoMinor,
    bool ConCorreccion);

/// <summary>Stock vivo por variante (nunca snapshot).</summary>
public sealed record FilaStockActual(
    string ProductoId,
    string ProductoNombre,
    string CodigoInterno,
    string VarianteId,
    string? Talle,
    string? Color,
    string? CategoriaNombre,
    long Disponible,
    long Apartado,
    long Fisico)
{
    public string TalleColor => (Talle, Color) switch
    {
        (null, null) => "—",
        (null, var c) => c!,
        (var t, null) => t!,
        (var t, var c) => $"{t} · {c}",
    };
}

/// <summary>Ranking por variante con columnas brutas/devueltas/salidas/netas.</summary>
public sealed record RankingVenta(
    string? ProductoId,
    string CodigoInterno,
    string NombreProducto,
    string? VarianteId,
    string? Talle,
    string? Color,
    long BrutasUnidades,
    long DevueltasUnidades,
    long SalidasCambioUnidades,
    long NetasUnidades);

/// <summary>Variantes sin ventas en los últimos N días (o nunca vendidas).</summary>
public sealed record SinVentas(
    string VarianteId,
    string ProductoNombre,
    string CodigoInterno,
    string? Talle,
    string? Color,
    string? UltimaVentaEn,
    string? UltimaRecepcionEn,
    string? UltimoMovimientoEn,
    bool NuncaVendido);
