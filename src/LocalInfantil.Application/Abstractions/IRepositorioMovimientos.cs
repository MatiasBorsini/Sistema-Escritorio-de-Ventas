using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioMovimientos
{
    Task<string> RegistrarAsync(ISesionPersistencia sesion, NuevoMovimiento datos, string creadoEn, CancellationToken cancellationToken = default);

    Task<long> SumarPorVarianteAsync(ISesionPersistencia sesion, string varianteId, CancellationToken cancellationToken = default);

    Task<long> ContarPorVarianteAsync(ISesionPersistencia sesion, string varianteId, CancellationToken cancellationToken = default);

    Task<bool> ExistePorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MovimientoHistorial>> ListarPorVarianteAsync(ISesionPersistencia sesion, string varianteId, int limite, int desplazamiento, CancellationToken cancellationToken = default);
}
