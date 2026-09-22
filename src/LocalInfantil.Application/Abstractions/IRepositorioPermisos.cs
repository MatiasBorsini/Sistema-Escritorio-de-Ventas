namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Concesiones individuales de permisos por usuario. La fila explícita
/// prevalece siempre: concedido bloquea o habilita sin importar la matriz
/// por rol, que solo aplica en ausencia de fila. El permiso técnico
/// `usuarios.administrar` no es configurable por esta vía. Revocar escribe
/// denegación explícita (no borra) para que ninguna concesión heredada de la
/// matriz pueda saltársela.
/// </summary>
public interface IRepositorioPermisos
{
    Task DefinirAsync(ISesionPersistencia sesion, string usuarioId, string permiso, bool concedido, string actualizadoEn, string? actualizadoPorUsuarioId, CancellationToken cancellationToken = default);

    Task<bool?> ObtenerAsync(ISesionPersistencia sesion, string usuarioId, string permiso, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListarConcedidosAsync(ISesionPersistencia sesion, string usuarioId, CancellationToken cancellationToken = default);
}
