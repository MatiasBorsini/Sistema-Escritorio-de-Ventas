using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioInicio
{
    Task<ResumenInventarioInicio> ObtenerInventarioAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default);
    Task<int> ContarConteosBorradorAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventoInicioOrigen>> ListarActividadAsync(ISesionPersistencia sesion, int limite, CancellationToken cancellationToken = default);

    /// <summary>Importe neto y cantidad de ventas del período [desdeUtc, hastaUtc).</summary>
    Task<VentasDelDiaInicio> ObtenerVentasDelDiaAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, CancellationToken cancellationToken = default);

    /// <summary>Última recepción registrada, o null si todavía no hay ninguna.</summary>
    Task<RecepcionRecienteInicio?> ObtenerUltimaRecepcionAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default);
}
