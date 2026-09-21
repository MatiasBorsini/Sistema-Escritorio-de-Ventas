using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>
/// Desactivación lógica de producto: preserva el producto y su historial.
/// Se niega mientras conserve variantes activas (primero se desactivan ellas).
/// </summary>
public sealed class DesactivarProducto
{
    private readonly IRepositorioProductos _productos;
    private readonly IRepositorioVariantes _variantes;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;

    public DesactivarProducto(
        IRepositorioProductos productos,
        IRepositorioVariantes variantes,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids)
    {
        _productos = productos;
        _variantes = variantes;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
    }

    public async Task EjecutarAsync(string id, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCatalogo.ProductosGestionar, cancellationToken);

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var existente = await _productos.ObtenerPorIdAsync(sesion, id, cancellationToken);
        if (existente is null)
        {
            throw new InvalidOperationException("El producto no existe.");
        }

        if (await _variantes.ContarActivasPorProductoAsync(sesion, id, cancellationToken) > 0)
        {
            throw new InvalidOperationException("El producto conserva variantes activas; desactívelas primero.");
        }

        await _productos.DesactivarAsync(sesion, id, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "catalogo.producto_desactivacion", ActorAuditoria.Usuario, actor.Id, "producto", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
