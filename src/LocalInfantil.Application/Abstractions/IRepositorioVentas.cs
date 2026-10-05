using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioVentas
{
    Task<string> InsertarAsync(
        ISesionPersistencia sesion,
        string numero,
        string operacionId,
        long totalMinor,
        string moneda,
        string? creadoPorUsuarioId,
        string creadoEn,
        string confirmadaEn,
        string? clienteId,
        string? clienteNombreSnapshot,
        CancellationToken cancellationToken = default);

    Task InsertarLineaAsync(
        ISesionPersistencia sesion,
        string ventaId,
        string varianteId,
        string codigoInterno,
        string nombreProducto,
        string? talle,
        string? color,
        long cantidad,
        long precioUnitarioMinor,
        CancellationToken cancellationToken = default);

    Task<bool> ExistePorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);

    Task<VentaRegistrada?> ObtenerPorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);

    Task<VentaRegistrada?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<VentaRegistrada?> ObtenerPorNumeroAsync(ISesionPersistencia sesion, string numero, CancellationToken cancellationToken = default);

    Task MarcarAnuladaAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LineaVentaRegistrada>> ListarLineasAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Historial con agregados en una sola lectura. El parámetro
    /// <paramref name="limite"/> es el tamaño de página (1–100); devuelve
    /// hasta limite+1 filas para detectar HaySiguiente sin COUNT (*).
    /// <paramref name="estado"/>: "todas", "confirmadas" o "anuladas".
    /// </summary>
    Task<IReadOnlyList<ResumenVenta>> BuscarResumenAsync(ISesionPersistencia sesion, string texto, string estado, int limite, int desplazamiento, CancellationToken cancellationToken = default);

    /// <summary>Correlativo visible T-000001 (patrón secuencias V9).</summary>
    Task<int> ObtenerSiguienteNumeroAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default);

    Task<bool> ExisteReversionAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default);

    Task InsertarReversionAsync(
        ISesionPersistencia sesion,
        string ventaId,
        string operacionId,
        string motivo,
        string? creadoPorUsuarioId,
        string creadoEn,
        CancellationToken cancellationToken = default);

    Task<ReversionVentaRegistrada?> ObtenerReversionAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default);
}
