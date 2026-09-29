using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioProductos
{
    Task<string> InsertarAsync(ISesionPersistencia sesion, NuevoProducto datos, string codigoInterno, string creadoEn, CancellationToken cancellationToken = default);

    Task<ProductoRegistrado?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<ProductoRegistrado?> ObtenerPorCodigoInternoAsync(ISesionPersistencia sesion, string codigoInterno, CancellationToken cancellationToken = default);

    Task ActualizarAsync(ISesionPersistencia sesion, string id, EdicionProducto edicion, CancellationToken cancellationToken = default);

    Task DesactivarAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductoRegistrado>> ListarAsync(ISesionPersistencia sesion, bool soloActivos, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductoRegistrado>> BuscarAsync(ISesionPersistencia sesion, string texto, int limite, int desplazamiento, CancellationToken cancellationToken = default);

    /// <summary>
    /// Búsqueda con agregados por página en una sola lectura. El parámetro
    /// <paramref name="limite"/> es el tamaño de página (1–100); devuelve
    /// hasta limite+1 filas para detectar HaySiguiente sin COUNT (*).
    /// </summary>
    Task<IReadOnlyList<ResumenProducto>> BuscarResumenAsync(ISesionPersistencia sesion, string texto, int limite, int desplazamiento, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategoriaRegistrada>> ListarCategoriasAsync(ISesionPersistencia sesion, bool soloActivas, CancellationToken cancellationToken = default);

    Task<int> ObtenerSiguienteNumeroProductoAsync(ISesionPersistencia sesion, string categoriaId, CancellationToken cancellationToken = default);
}
