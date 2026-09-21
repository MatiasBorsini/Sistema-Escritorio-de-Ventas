namespace LocalInfantil.Application.Contracts;

public sealed record NuevoMovimiento(
    string VarianteId,
    string Tipo,
    long Cantidad,
    string? Motivo,
    string OperacionId,
    string? CreadoPorUsuarioId);
