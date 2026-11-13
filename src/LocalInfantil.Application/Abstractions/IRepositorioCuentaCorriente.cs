using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioCuentaCorriente
{
    Task<IReadOnlyList<ClienteCuentaCorrienteListado>> BuscarClientesAsync(ISesionPersistencia sesion, string texto,
        FiltroClientesCuentaCorriente filtro, int limite, int desplazamiento, CancellationToken ct = default);
    Task<long> ObtenerSaldoAsync(ISesionPersistencia sesion, string clienteId, CancellationToken ct = default);
    Task<IReadOnlyList<CargoCuentaCorriente>> ListarCargosPendientesAsync(ISesionPersistencia sesion, string clienteId, CancellationToken ct = default);
    Task<IReadOnlyList<CargoCuentaCorriente>> ListarCargosPendientesVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken ct = default);
    Task<string> InsertarMovimientoAsync(ISesionPersistencia sesion, NuevoMovimientoCuentaCorriente movimiento, CancellationToken ct = default);
    Task InsertarAplicacionAsync(ISesionPersistencia sesion, string reductorId, AplicacionCuentaCorriente aplicacion, string fecha, CancellationToken ct = default);
    Task<int> ObtenerSiguienteNumeroAbonoAsync(ISesionPersistencia sesion, CancellationToken ct = default);
    Task<string> InsertarAbonoAsync(ISesionPersistencia sesion, string numero, string clienteId, long importe, long saldoAnterior, string operacionId, string usuarioId, string fecha, CancellationToken ct = default);
    Task<string> InsertarComponenteAbonoAsync(ISesionPersistencia sesion, string abonoId, NuevoPago pago, CancellationToken ct = default);
    Task<DocumentoTicketAbono?> ObtenerTicketAbonoAsync(ISesionPersistencia sesion, string abonoId, CancellationToken ct = default);
    Task<IReadOnlyList<(CargoCuentaCorriente Cargo, string ComponenteId, string Medio, long Importe)>> ListarAplicacionesPagadasVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken ct = default);
    Task<long> SumarReintegrosPorComponenteAsync(ISesionPersistencia sesion, string componenteId, CancellationToken ct = default);
    Task<long> SumarReintegrosPorComponenteVentaAsync(ISesionPersistencia sesion, string componenteId, string ventaId, CancellationToken ct = default);
    Task<IReadOnlyList<MovimientoCuentaCorriente>> ListarMovimientosAsync(ISesionPersistencia sesion, string clienteId, int limite, int desplazamiento, CancellationToken ct = default);
}
