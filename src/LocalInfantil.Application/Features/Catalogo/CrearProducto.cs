using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>Alta de producto con auditoría en la misma transacción.</summary>
public sealed class CrearProducto
{
    private readonly IRepositorioProductos _productos;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public CrearProducto(
        IRepositorioProductos productos,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _productos = productos;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task<string> EjecutarAsync(NuevoProducto datos, string? idSesion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCatalogo.ProductosGestionar, cancellationToken);

        if (string.IsNullOrWhiteSpace(datos.Nombre))
        {
            throw new ArgumentException("El nombre es obligatorio.", nameof(datos));
        }

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var id = await _productos.InsertarAsync(sesion, datos, _reloj.UtcNow().ToString("o"), cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "catalogo.producto_alta", ActorAuditoria.Usuario, actor.Id, "producto", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
