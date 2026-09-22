namespace LocalInfantil.Application.Contracts;

/// <summary>Existencias derivadas de movimientos confirmados para una variante.</summary>
public sealed record ExistenciaVariante(string VarianteId, string Sku, long Existencias);

/// <summary>Un movimiento con responsable legible, en orden estable.</summary>
public sealed record MovimientoHistorial(
    string Id,
    string Tipo,
    long Cantidad,
    string? Motivo,
    string? ResponsableNombre,
    string OperacionId,
    string CreadoEn);

/// <summary>Una fila de conteo físico: variante y cantidad contada (entera, no negativa).</summary>
public sealed record FilaConteo(string VarianteId, long CantidadContada);

/// <summary>Detalle guardado con la existencia esperada y el conteo de movimientos al iniciar.</summary>
public sealed record DetalleConteo(string VarianteId, long ExistenciaEsperada, long MovimientosContados, long CantidadContada);

/// <summary>Línea de informe: esperado, contado y diferencia por SKU.</summary>
public sealed record InformeConteoLinea(string VarianteId, string Sku, long Esperado, long Contado, long Diferencia);

/// <summary>Conteo físico registrado.</summary>
public sealed record ConteoRegistrado(string Id, string OperacionId, string Estado, string? AprobadoPorUsuarioId);
