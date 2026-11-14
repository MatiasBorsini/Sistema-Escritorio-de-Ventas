using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>
/// Edición descriptiva de producto (reemplazo total). El id y el código
/// interno nunca cambian. La categoría sigue siendo obligatoria (igual que
/// en el alta): debe ser una categoría activa válida.
/// </summary>
public sealed class EditarProducto
{
    private readonly IRepositorioProductos _productos;
    private readonly IRepositorioCategorias _categorias;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;

    public EditarProducto(
        IRepositorioProductos productos,
        IRepositorioCategorias categorias,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids)
    {
        _productos = productos;
        _categorias = categorias;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
    }

    public async Task EjecutarAsync(string id, EdicionProducto edicion, string? idSesion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edicion);
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCatalogo.ProductosGestionar, cancellationToken);

        if (string.IsNullOrWhiteSpace(edicion.CategoriaId))
        {
            throw new InvalidOperationException("Seleccioná una categoría.");
        }

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var existente = await _productos.ObtenerPorIdAsync(sesion, id, cancellationToken);
        if (existente is null)
        {
            throw new InvalidOperationException("El producto no existe.");
        }

        var categoria = await _categorias.ObtenerPorIdAsync(sesion, edicion.CategoriaId, cancellationToken);
        if (categoria is null || !categoria.Activa)
        {
            throw new InvalidOperationException("Seleccioná una categoría.");
        }

        await _productos.ActualizarAsync(sesion, id, edicion, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "catalogo.producto_edicion", ActorAuditoria.Usuario, actor.Id, "producto", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
