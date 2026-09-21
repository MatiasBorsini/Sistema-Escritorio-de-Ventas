using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioProductos
{
    Task<string> InsertarAsync(ISesionPersistencia sesion, NuevoProducto datos, string creadoEn, CancellationToken cancellationToken = default);

    Task<ProductoRegistrado?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);
}
