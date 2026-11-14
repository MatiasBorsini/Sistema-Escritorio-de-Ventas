namespace LocalInfantil.Application.Contracts;

public sealed record NuevoProveedor(string Nombre);

public sealed record ProveedorRegistrado(string Id, string Nombre, bool Activo);

public sealed record EdicionProveedor(string Nombre);

/// <summary>Línea de recepción: variante, cantidad positiva y costo histórico opcional.</summary>
public sealed record LineaRecepcion(string VarianteId, long Cantidad, long? CostoUnitarioMinor, string? Moneda);

public sealed record RecepcionRegistrada(string Id, string ProveedorId, string OperacionId, string? ComprobanteRef, string? CreadoPorUsuarioId, string CreadoEn);

/// <summary>Filtros de estado del historial. Dato confiable: existe o no reversión.</summary>
public static class FiltroRecepciones
{
    public const string Todas = "todas";
    public const string Confirmadas = "confirmadas";
    public const string Revertidas = "revertidas";
}

/// <summary>
/// Resumen por recepción para el historial en una sola lectura: proveedor y
/// responsable ya resueltos por nombre, agregados (líneas, unidades) y total
/// solo sobre líneas con costo. TotalMinor es 0 cuando no hay costos; la VM
/// lo muestra únicamente si LineasSinCosto == 0 (dato inequívoco).
/// </summary>
public sealed record ResumenRecepcion(
    string Id,
    string ProveedorId,
    string ProveedorNombre,
    string OperacionId,
    string? ComprobanteRef,
    string? CreadoPorUsuarioId,
    string? CreadoPorNombre,
    string CreadoEn,
    int CantidadLineas,
    long Unidades,
    int LineasSinCosto,
    long TotalMinor,
    bool Revertida);

/// <summary>Línea de recepción con variante resuelta (SKU/talle/color) para detalle.</summary>
public sealed record LineaRecepcionDetalle(
    string Sku,
    string? Talle,
    string? Color,
    long Cantidad,
    long? CostoUnitarioMinor,
    string? Moneda)
{
    /// <summary>Subtotal aritmético de presentación (solo si hay costo).</summary>
    public long? SubtotalMinor => CostoUnitarioMinor.HasValue ? Cantidad * CostoUnitarioMinor.Value : null;
}
