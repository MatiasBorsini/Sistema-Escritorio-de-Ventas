using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioVariantes
{
    Task<string> InsertarAsync(ISesionPersistencia sesion, NuevaVariante datos, string creadoEn, CancellationToken cancellationToken = default);

    Task<VarianteRegistrada?> ObtenerPorSkuAsync(ISesionPersistencia sesion, string sku, CancellationToken cancellationToken = default);

    Task<VarianteRegistrada?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task ActualizarAsync(ISesionPersistencia sesion, string id, EdicionVariante edicion, CancellationToken cancellationToken = default);

    Task DesactivarAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VarianteRegistrada>> ListarPorProductoAsync(ISesionPersistencia sesion, string productoId, CancellationToken cancellationToken = default);

    Task<long> ContarActivasPorProductoAsync(ISesionPersistencia sesion, string productoId, CancellationToken cancellationToken = default);
}
