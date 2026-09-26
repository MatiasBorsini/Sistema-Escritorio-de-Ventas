using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioRecepciones
{
    Task<string> InsertarAsync(
        ISesionPersistencia sesion,
        string proveedorId,
        string operacionId,
        string? comprobanteRef,
        string? creadoPorUsuarioId,
        string creadoEn,
        CancellationToken cancellationToken = default);

    Task InsertarLineaAsync(
        ISesionPersistencia sesion,
        string recepcionId,
        string varianteId,
        long cantidad,
        long? costoUnitarioMinor,
        string? moneda,
        CancellationToken cancellationToken = default);

    Task<bool> ExistePorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);

    Task<bool> ExisteReversionAsync(ISesionPersistencia sesion, string recepcionId, CancellationToken cancellationToken = default);

    Task InsertarReversionAsync(
        ISesionPersistencia sesion,
        string recepcionId,
        string operacionId,
        string motivo,
        string? creadoPorUsuarioId,
        string creadoEn,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LineaRecepcion>> ListarLineasAsync(ISesionPersistencia sesion, string recepcionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Historial con agregados en una sola lectura. <paramref name="limite"/>
    /// es tamaño de página (1–100); devuelve hasta limite+1 para el peek de
    /// HaySiguiente. <paramref name="filtro"/>: todas/confirmadas/revertidas.
    /// </summary>
    Task<IReadOnlyList<ResumenRecepcion>> BuscarResumenAsync(ISesionPersistencia sesion, string texto, string filtro, int limite, int desplazamiento, CancellationToken cancellationToken = default);

    /// <summary>Líneas con variante resuelta (SKU/talle/color) en una lectura.</summary>
    Task<IReadOnlyList<LineaRecepcionDetalle>> ListarLineasDetalleAsync(ISesionPersistencia sesion, string recepcionId, CancellationToken cancellationToken = default);

    Task<RecepcionRegistrada?> ObtenerAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecepcionRegistrada>> ListarAsync(ISesionPersistencia sesion, int limite, int desplazamiento, CancellationToken cancellationToken = default);

    Task<bool> ExisteReversionPorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);
}
