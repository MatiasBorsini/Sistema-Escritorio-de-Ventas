namespace LocalInfantil.Application.Contracts;

/// <summary>
/// Existencias con clasificación: físico = disponible + apartado.
/// Nombres inequívocos para la pantalla: Físico (prendas en el local),
/// Disponible (vendible) y Apartado (no vendible: dañado/en revisión).
/// </summary>
public sealed record ExistenciaClasificada(
    string VarianteId,
    string Sku,
    long Fisico,
    long Disponible,
    long Apartado);

/// <summary>
/// Una fila del historial de clasificación, con responsable legible.
/// Tipos: 'apartar', 'reincorporar', 'baja_definitiva'. La baja definitiva
/// deja de existir físicamente; el resto no altera el total físico.
/// </summary>
public sealed record ApartadoHistorial(
    string Id,
    string Tipo,
    long Cantidad,
    string Motivo,
    string? ResponsableNombre,
    string OperacionId,
    string CreadoEn);

/// <summary>
/// Foto de clasificación capturada al crear un conteo: apartado esperado y
/// cantidad de movimientos de clasificación contados hasta ese momento.
/// </summary>
public sealed record FotoClasificacion(
    string VarianteId,
    long ApartadoEsperado,
    long ClasificacionesContadas);

/// <summary>
/// Contrato preparado para el futuro POS (F04): la venta solo puede consumir
/// unidades disponibles, nunca el físico total ni las apartadas. Sin
/// implementar ventas en este bloque.
/// </summary>
public sealed record StockVendible(
    string VarianteId,
    string Sku,
    long Disponible);
