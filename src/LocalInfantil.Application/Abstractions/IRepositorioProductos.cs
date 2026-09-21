using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioProductos
{
    Task<string> InsertarAsync(ISesionPersistencia sesion, NuevoProducto datos, string creadoEn, CancellationToken cancellationToken = default);

    Task<ProductoRegistrado?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task ActualizarAsync(ISesionPersistencia sesion, string id, EdicionProducto edicion, CancellationToken cancellationToken = default);

    Task DesactivarAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductoRegistrado>> ListarAsync(ISesionPersistencia sesion, bool soloActivos, CancellationToken cancellationToken = default);
}
