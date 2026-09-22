using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioUsuarios
{
    Task<string> CrearAsync(ISesionPersistencia sesion, NuevoUsuario datos, string huella, string creadoEn, CancellationToken cancellationToken = default);

    Task<UsuarioRegistrado?> ObtenerPorNombreAsync(ISesionPersistencia sesion, string nombre, CancellationToken cancellationToken = default);

    Task<UsuarioRegistrado?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<string?> ObtenerHuellaAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task CambiarHuellaAsync(ISesionPersistencia sesion, string id, string huella, CancellationToken cancellationToken = default);

    Task DesactivarAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<long> ContarTodosAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default);

    Task<long> ContarActivosPorRolAsync(ISesionPersistencia sesion, string rol, CancellationToken cancellationToken = default);
}
