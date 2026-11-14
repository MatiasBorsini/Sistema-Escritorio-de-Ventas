using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioCambios
{
    Task<bool> ExisteOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);
    Task<bool> ExistePorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default);
    Task<long> CantidadDisponibleAsync(ISesionPersistencia sesion, string ventaLineaId, CancellationToken cancellationToken = default);
    Task<int> ObtenerSiguienteNumeroAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default);
    Task<string> InsertarCambioAsync(ISesionPersistencia sesion, string numero, DatosCambio datos, string usuarioId, string? autorizadoPorId, long importeEntradas, long importeSalidas, string fecha, CancellationToken cancellationToken = default);
    Task InsertarEntradaAsync(ISesionPersistencia sesion, string cambioId, LineaVentaRegistrada linea, LineaCambioEntrada entrada, CancellationToken cancellationToken = default);
    Task InsertarSalidaAsync(ISesionPersistencia sesion, string cambioId, string varianteId, string codigo, string nombre, string? talle, string? color, long cantidad, long precio, CancellationToken cancellationToken = default);
    Task<string> InsertarPagoAsync(ISesionPersistencia sesion, string cambioId, string direccion, string medio, long importe, long? recibido, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResumenCambio>> ListarPorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default);
    Task<DocumentoTicketCambio?> ObtenerTicketAsync(ISesionPersistencia sesion, string cambioId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResumenVenta>> BuscarVentasAsync(ISesionPersistencia sesion, string filtro, string? desde, string? hasta, int limite, CancellationToken cancellationToken = default);
}
