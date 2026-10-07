using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioVariantes
{
    Task<string> InsertarAsync(ISesionPersistencia sesion, NuevaVariante datos, string codigoInterno, string creadoEn, CancellationToken cancellationToken = default);

    Task<VarianteRegistrada?> ObtenerPorCodigoInternoAsync(ISesionPersistencia sesion, string codigoInterno, CancellationToken cancellationToken = default);

    Task<VarianteRegistrada?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task ActualizarAsync(ISesionPersistencia sesion, string id, EdicionVariante edicion, CancellationToken cancellationToken = default);

    Task DesactivarAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VarianteRegistrada>> ListarPorProductoAsync(ISesionPersistencia sesion, string productoId, CancellationToken cancellationToken = default);

    Task<long> ContarActivasPorProductoAsync(ISesionPersistencia sesion, string productoId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FichaVariante>> ObtenerFichaPorProductoAsync(ISesionPersistencia sesion, string productoId, string fecha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sugerencias para typeahead en una sola lectura (JOIN variante/producto).
    /// Busca por código interno o nombre de producto; orden de relevancia predecible:
    /// código exacto, código prefijo, producto prefijo, resto. Límite 1–100.
    /// </summary>
    Task<IReadOnlyList<SugerenciaVariante>> BuscarSugerenciasAsync(ISesionPersistencia sesion, string texto, int limite, CancellationToken cancellationToken = default);

    Task<int> ObtenerSiguienteNumeroVarianteAsync(ISesionPersistencia sesion, string productoId, CancellationToken cancellationToken = default);

    Task<int> ObtenerUltimoNumeroVarianteAsync(ISesionPersistencia sesion, string productoId, CancellationToken cancellationToken = default);
}
