using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioConteos
{
    Task<string> CrearAsync(ISesionPersistencia sesion, string operacionId, string creadoPorUsuarioId, string creadoEn, CancellationToken cancellationToken = default);

    Task<ConteoRegistrado?> ObtenerAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<bool> ExistePorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);

    Task AgregarDetalleAsync(ISesionPersistencia sesion, string conteoId, string varianteId, long existenciaEsperada, long movimientosContados, long cantidadContada, CancellationToken cancellationToken = default);

    Task ActualizarDetalleAsync(ISesionPersistencia sesion, string conteoId, string varianteId, long cantidadContada, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DetalleConteo>> ListarDetallesAsync(ISesionPersistencia sesion, string conteoId, CancellationToken cancellationToken = default);

    Task AprobarAsync(ISesionPersistencia sesion, string id, string aprobadoPorUsuarioId, string aprobadoEn, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InformeConteoLinea>> InformeAsync(ISesionPersistencia sesion, string conteoId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConteoRegistrado>> ListarAsync(ISesionPersistencia sesion, int limite, int desplazamiento, CancellationToken cancellationToken = default);
}
