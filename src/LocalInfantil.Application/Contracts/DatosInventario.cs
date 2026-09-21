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
