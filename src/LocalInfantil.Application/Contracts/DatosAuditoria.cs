namespace LocalInfantil.Application.Contracts;

public sealed record NuevoEventoAuditoria(
    string OperacionId,
    string TipoEvento,
    string ActorTipo,
    string? ActorId,
    string? ReferenciaTipo,
    string? ReferenciaId,
    string? Detalle);

public sealed record EventoAuditado(
    string Id,
    string OperacionId,
    string TipoEvento,
    string ActorTipo,
    string? ActorId,
    string? ReferenciaTipo,
    string? ReferenciaId,
    string? Detalle,
    string CreadoEn);
