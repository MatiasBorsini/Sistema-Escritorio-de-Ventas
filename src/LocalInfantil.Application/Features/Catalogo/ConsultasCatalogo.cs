using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>
/// Consultas de catálogo. Requieren sesión vigente pero ningún permiso
/// específico (interino hasta D-03; una capacidad de lectura pública, si se
/// necesita, es propuesta pendiente y no se habilita por suposición).
/// </summary>
public sealed class ConsultasCatalogo
{
    private readonly IRepositorioProductos _productos;
    private readonly IRepositorioVariantes _variantes;
    private readonly GestorSesiones _sesiones;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultasCatalogo(
        IRepositorioProductos productos,
        IRepositorioVariantes variantes,
        GestorSesiones sesiones,
        IFabricaSesionesPersistencia fabrica)
    {
        _productos = productos;
        _variantes = variantes;
        _sesiones = sesiones;
        _fabrica = fabrica;
    }

    public async Task<ProductoRegistrado?> ObtenerProductoAsync(string id, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _productos.ObtenerPorIdAsync(sesion, id, cancellationToken);
    }

    public async Task<IReadOnlyList<ProductoRegistrado>> ListarProductosAsync(string? idSesion, bool soloActivos, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _productos.ListarAsync(sesion, soloActivos, cancellationToken);
    }

    public async Task<VarianteRegistrada?> ObtenerVarianteAsync(string id, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _variantes.ObtenerPorIdAsync(sesion, id, cancellationToken);
    }

    /// <summary>
    /// Búsqueda exacta por código interno (ingreso rápido por teclado o lector
    /// de código de barras). No obliga a seleccionar una sugerencia.
    /// </summary>
    public async Task<VarianteRegistrada?> BuscarPorCodigoInternoAsync(string codigo, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _variantes.ObtenerPorCodigoInternoAsync(sesion, codigo.Trim(), cancellationToken);
    }

    /// <summary>Compat: SKU histórico = código interno actual.</summary>
    public Task<VarianteRegistrada?> BuscarPorSkuAsync(string sku, string? idSesion, CancellationToken cancellationToken = default)
        => BuscarPorCodigoInternoAsync(sku, idSesion, cancellationToken);

    public async Task<IReadOnlyList<VarianteRegistrada>> ListarPorProductoAsync(string productoId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _variantes.ListarPorProductoAsync(sesion, productoId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProductoRegistrado>> BuscarProductosAsync(string texto, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _productos.BuscarAsync(sesion, texto ?? string.Empty, limite, desplazamiento, cancellationToken);
    }

    /// <summary>
    /// Resumen por página en una sola lectura (hasta limite+1 filas para el
    /// peek de HaySiguiente). Solo lectura agregada; sin reglas nuevas.
    /// </summary>
    public async Task<IReadOnlyList<ResumenProducto>> BuscarResumenAsync(string texto, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _productos.BuscarResumenAsync(sesion, texto ?? string.Empty, limite, desplazamiento, cancellationToken);
    }

    /// <summary>
    /// Sugerencias de variantes para typeahead (una sola lectura JOIN).
    /// Busca por código interno o nombre de producto; misma convención que
    /// la generación automática (ej REM-001-03). Solo lectura agregada.
    /// </summary>
    public async Task<IReadOnlyList<SugerenciaVariante>> BuscarSugerenciasVariantesAsync(string texto, int limite, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _variantes.BuscarSugerenciasAsync(sesion, texto ?? string.Empty, limite, cancellationToken);
    }

    public async Task<IReadOnlyList<CategoriaRegistrada>> ListarCategoriasAsync(string? idSesion, bool soloActivas, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _productos.ListarCategoriasAsync(sesion, soloActivas, cancellationToken);
    }

    public async Task<FichaProducto?> ObtenerFichaAsync(string productoId, string fecha, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        var producto = await _productos.ObtenerPorIdAsync(sesion, productoId, cancellationToken);
        if (producto is null)
        {
            return null;
        }

        var variantes = await _variantes.ObtenerFichaPorProductoAsync(sesion, productoId, fecha, cancellationToken);
        return new FichaProducto(producto, variantes);
    }

    private async Task<ISesionPersistencia> SesionValidadaAsync(string? idSesion, CancellationToken cancellationToken)
    {
        if (idSesion is null || _sesiones.Obtener(idSesion) is null)
        {
            throw new SesionInvalidaException();
        }

        return await _fabrica.IniciarLecturaAsync(cancellationToken);
    }
}
