namespace LocalInfantil.Application.Contracts;

public sealed record NuevoProveedor(string Nombre);

public sealed record ProveedorRegistrado(string Id, string Nombre, bool Activo);

public sealed record EdicionProveedor(string Nombre);

/// <summary>Línea de recepción: variante, cantidad positiva y costo histórico opcional.</summary>
public sealed record LineaRecepcion(string VarianteId, long Cantidad, long? CostoUnitarioMinor, string? Moneda);

public sealed record RecepcionRegistrada(string Id, string ProveedorId, string OperacionId, string? ComprobanteRef, string? CreadoPorUsuarioId, string CreadoEn);
