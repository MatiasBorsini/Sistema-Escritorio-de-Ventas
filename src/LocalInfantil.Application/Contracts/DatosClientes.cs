namespace LocalInfantil.Application.Contracts;

public sealed record NuevoCliente(string NombreCompleto, string? Telefono, string? Observacion);
public sealed record EdicionCliente(string NombreCompleto, string? Telefono, string? Observacion);

public sealed record ClienteRegistrado(
    string Id,
    string NombreCompleto,
    string? Telefono,
    string? Observacion,
    bool Activo,
    string CreadoEn,
    string ActualizadoEn,
    long? LimiteCreditoMinor = null);

public sealed record SugerenciaCliente(string Id, string NombreCompleto, string? Telefono);

/// <summary>Movimiento visible del historial comercial del cliente; nunca representa deuda.</summary>
public sealed record MovimientoHistorialCliente(
    string Tipo,
    string Fecha,
    string Referencia,
    string? VentaReferencia,
    string Estado,
    long? TotalMinor,
    long? DiferenciaMinor);
