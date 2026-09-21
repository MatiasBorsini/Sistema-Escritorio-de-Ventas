using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Compras;

/// <summary>Edición del nombre de proveedor (reemplazo total). El id nunca cambia.</summary>
public sealed class EditarProveedor
{
    private readonly IRepositorioProveedores _proveedores;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;

    public EditarProveedor(
        IRepositorioProveedores proveedores,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids)
    {
        _proveedores = proveedores;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
    }

    public async Task EjecutarAsync(string id, EdicionProveedor edicion, string? idSesion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edicion);
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCompras.ProveedoresGestionar, cancellationToken);

        if (string.IsNullOrWhiteSpace(edicion.Nombre))
        {
            throw new ArgumentException("El nombre es obligatorio.", nameof(edicion));
        }

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var existente = await _proveedores.ObtenerPorIdAsync(sesion, id, cancellationToken);
        if (existente is null)
        {
            throw new InvalidOperationException("El proveedor no existe.");
        }

        await _proveedores.ActualizarAsync(sesion, id, edicion, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "compra.proveedor_edicion", ActorAuditoria.Usuario, actor.Id, "proveedor", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
