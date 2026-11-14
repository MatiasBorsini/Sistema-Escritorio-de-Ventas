using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioEtiquetas
{
    Task<IReadOnlyList<VarianteParaEtiqueta>> ListarPorProductoAsync(ISesionPersistencia sesion, string productoId, string fechaUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VarianteParaEtiqueta>> ListarPorIdsAsync(ISesionPersistencia sesion, IReadOnlyList<string> varianteIds, string fechaUtc, CancellationToken cancellationToken = default);
}
