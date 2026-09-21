using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioMovimientos
{
    Task<string> RegistrarAsync(ISesionPersistencia sesion, NuevoMovimiento datos, string creadoEn, CancellationToken cancellationToken = default);

    Task<long> SumarPorVarianteAsync(ISesionPersistencia sesion, string varianteId, CancellationToken cancellationToken = default);
}
