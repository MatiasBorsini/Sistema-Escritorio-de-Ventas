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

    public async Task<VarianteRegistrada?> BuscarPorSkuAsync(string sku, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _variantes.ObtenerPorSkuAsync(sesion, sku, cancellationToken);
    }

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
