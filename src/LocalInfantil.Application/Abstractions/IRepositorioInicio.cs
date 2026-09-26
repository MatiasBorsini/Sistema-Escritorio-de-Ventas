using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioInicio
{
    Task<ResumenInventarioInicio> ObtenerInventarioAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default);
    Task<int> ContarConteosBorradorAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventoInicioOrigen>> ListarActividadAsync(ISesionPersistencia sesion, int limite, CancellationToken cancellationToken = default);
}
