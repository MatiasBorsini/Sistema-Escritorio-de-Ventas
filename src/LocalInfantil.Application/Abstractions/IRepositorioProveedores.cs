using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioProveedores
{
    Task<string> CrearAsync(ISesionPersistencia sesion, NuevoProveedor datos, string creadoEn, CancellationToken cancellationToken = default);

    Task<ProveedorRegistrado?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProveedorRegistrado>> ListarAsync(ISesionPersistencia sesion, bool soloActivos, CancellationToken cancellationToken = default);

    Task ActualizarAsync(ISesionPersistencia sesion, string id, EdicionProveedor edicion, CancellationToken cancellationToken = default);

    Task DesactivarAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);
}
