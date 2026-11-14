using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioCategorias
{
    Task<string> InsertarAsync(ISesionPersistencia sesion, NuevaCategoria datos, string creadoEn, CancellationToken cancellationToken = default);

    Task<CategoriaRegistrada?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<CategoriaRegistrada?> ObtenerPorNombreAsync(ISesionPersistencia sesion, string nombre, CancellationToken cancellationToken = default);

    Task ActualizarAsync(ISesionPersistencia sesion, string id, NuevaCategoria datos, CancellationToken cancellationToken = default);

    Task DesactivarAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategoriaRegistrada>> ListarAsync(ISesionPersistencia sesion, bool soloActivas, CancellationToken cancellationToken = default);
}