using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Registro mínimo de auditoría: operación, actor, fecha UTC, tipo de evento
/// y referencia al elemento afectado cuando corresponda. Sin permisos (F01-T04)
/// y sin secretos: el detalle nunca debe llevar contraseñas, tokens ni datos
/// personales innecesarios.
/// </summary>
public interface IRepositorioAuditoria
{
    Task<string> RegistrarAsync(ISesionPersistencia sesion, NuevoEventoAuditoria evento, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventoAuditado>> ObtenerPorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);
}
