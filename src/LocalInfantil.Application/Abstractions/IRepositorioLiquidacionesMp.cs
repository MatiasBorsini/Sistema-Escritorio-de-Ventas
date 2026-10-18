using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Persistencia de la conciliación financiera de Mercado Pago (V18):
/// importaciones de reportes, filas de liquidación y vínculos de conciliación.
/// No interpreta contabilidad: solo guarda el reporte y su relación con las
/// ventas/pagos/cobros ya registrados.
/// </summary>
public interface IRepositorioLiquidacionesMp
{
    Task<bool> ExisteImportacionPorHashAsync(ISesionPersistencia sesion, string archivoHash, CancellationToken cancellationToken = default);

    Task<string> InsertarImportacionAsync(ISesionPersistencia sesion, ImportacionMpRegistrada importacion, CancellationToken cancellationToken = default);

    Task ActualizarResultadoImportacionAsync(ISesionPersistencia sesion, string id, string estado, long filasTotales, long filasAplicadas, long filasError, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserta una fila de liquidación sin deduplicar por contenido: la
    /// identidad es (importación, ordinal), así dos filas idénticas del mismo
    /// archivo se conservan por separado. La deduplicación entre reportes
    /// solapados se resuelve por <c>identidad_remota</c> en la capa de caso de
    /// uso, nunca descartando filas legítimas por coincidencia de importe,
    /// fecha o descripción.
    /// </summary>
    Task<string> InsertarLiquidacionAsync(ISesionPersistencia sesion, string importacionId, FilaLiquidacionMp fila, string creadoEn, CancellationToken cancellationToken = default);

    Task<LiquidacionMpRegistrada?> ObtenerLiquidacionAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LiquidacionMpRegistrada>> ListarLiquidacionesAsync(ISesionPersistencia sesion, string? estadoConciliacion, int limite, int desplazamiento, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LiquidacionMpRegistrada>> ListarPendientesAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListarHashesExistentesAsync(ISesionPersistencia sesion, IReadOnlyCollection<string> hashes, CancellationToken cancellationToken = default);

    /// <summary>Identidades remotas ya importadas (cualquier importación) dentro del conjunto consultado.</summary>
    Task<IReadOnlyList<string>> ListarIdentidadesRemotasExistentesAsync(ISesionPersistencia sesion, IReadOnlyCollection<string> identidades, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportacionMpRegistrada>> ListarImportacionesAsync(ISesionPersistencia sesion, int limite, CancellationToken cancellationToken = default);

    Task ActualizarConciliacionLiquidacionAsync(ISesionPersistencia sesion, string liquidacionId, string estadoConciliacion, string? ventaId, string? pagoId, string? operacionMpId, string? cobroManualId, string? observacion, CancellationToken cancellationToken = default);

    Task<string> InsertarConciliacionAsync(ISesionPersistencia sesion, string liquidacionId, string? ventaId, string? pagoId, string? operacionMpId, string? cobroManualId, string tipoMatch, string? diferenciaDetalle, string? usuarioId, string creadoEn, CancellationToken cancellationToken = default);

    Task<bool> ExisteConciliacionDeLiquidacionAsync(ISesionPersistencia sesion, string liquidacionId, CancellationToken cancellationToken = default);

    /// <summary>True si alguna liquidación ya quedó vinculada a esa venta (evita doble reconocimiento).</summary>
    Task<bool> ExisteConciliacionPorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default);

    /// <summary>True si alguna liquidación ya quedó vinculada a esa operación MP (evita doble reconocimiento).</summary>
    Task<bool> ExisteConciliacionPorOperacionAsync(ISesionPersistencia sesion, string operacionMpId, CancellationToken cancellationToken = default);
}
