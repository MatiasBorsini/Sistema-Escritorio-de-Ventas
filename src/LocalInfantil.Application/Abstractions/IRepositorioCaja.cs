using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioCaja
{
    Task<SesionCajaRegistrada?> ObtenerAbiertaAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default);
    Task<SesionCajaRegistrada?> ObtenerSesionAsync(ISesionPersistencia sesion, string sesionId, CancellationToken cancellationToken = default);
    Task<string> AbrirSesionAsync(ISesionPersistencia sesion, string usuarioId, long fondoInicialMinor, string operacionId, string abiertaEn, CancellationToken cancellationToken = default);
    Task<string> InsertarOperacionAsync(ISesionPersistencia sesion, NuevaOperacionCaja operacion, CancellationToken cancellationToken = default);
    Task<string> InsertarMovimientoAsync(ISesionPersistencia sesion, NuevoMovimientoCaja movimiento, CancellationToken cancellationToken = default);
    Task<bool> ExisteOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);
    Task<long> CalcularEfectivoEsperadoAsync(ISesionPersistencia sesion, string sesionId, CancellationToken cancellationToken = default);
    Task CerrarSesionAsync(ISesionPersistencia sesion, string sesionId, string usuarioId, string operacionId, string cerradaEn, long esperadoMinor, long contadoMinor, long diferenciaMinor, string? observacion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MovimientoCajaRegistrado>> ListarMovimientosAsync(ISesionPersistencia sesion, string sesionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImportePorMedioCaja>> ResumirMediosAsync(ISesionPersistencia sesion, string sesionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImportePorTipoCaja>> ResumirMovimientosAsync(ISesionPersistencia sesion, string sesionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SesionCajaRegistrada>> ListarSesionesAsync(ISesionPersistencia sesion, int limite, int desplazamiento, CancellationToken cancellationToken = default);
    /// <summary>Sesiones que se solapan con [desdeUtc, hastaUtc): abiertas o cerradas dentro.</summary>
    Task<IReadOnlyList<SesionCajaRegistrada>> ListarSesionesPorFechaAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, int limite, int desplazamiento, CancellationToken cancellationToken = default);
    Task<PagoCajaActual?> ObtenerPagoActualAsync(ISesionPersistencia sesion, string pagoId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PagoCajaActual>> ListarPagosActualesPorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PagoCajaActual>> BuscarPagosCorregiblesAsync(ISesionPersistencia sesion, string filtroNumeroVenta, int limite, CancellationToken cancellationToken = default);
    Task<long> SumarReintegrosPorPagoAsync(ISesionPersistencia sesion, string pagoId, CancellationToken cancellationToken = default);
    Task InsertarCorreccionPagoAsync(ISesionPersistencia sesion, string pagoId, string medioAnterior, string medioNuevo, long importeMinor, string motivo, string usuarioId, string operacionId, string cajaOperacionId, string creadoEn, CancellationToken cancellationToken = default);
}
