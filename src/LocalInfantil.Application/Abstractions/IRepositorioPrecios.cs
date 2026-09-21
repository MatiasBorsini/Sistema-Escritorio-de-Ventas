using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioPrecios
{
    Task<string> FijarAsync(ISesionPersistencia sesion, NuevoPrecio datos, string creadoEn, CancellationToken cancellationToken = default);

    Task<PrecioRegistrado?> ObtenerVigenteAsync(ISesionPersistencia sesion, string varianteId, string fecha, CancellationToken cancellationToken = default);
}
