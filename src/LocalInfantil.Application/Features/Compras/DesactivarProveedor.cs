using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Compras;

/// <summary>Desactivación lógica de proveedor. Nunca borra: el historial de recepciones se conserva.</summary>
public sealed class DesactivarProveedor
{
    private readonly IRepositorioProveedores _proveedores;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;

    public DesactivarProveedor(
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

    public async Task EjecutarAsync(string id, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCompras.ProveedoresGestionar, cancellationToken);

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var existente = await _proveedores.ObtenerPorIdAsync(sesion, id, cancellationToken);
        if (existente is null)
        {
            throw new InvalidOperationException("El proveedor no existe.");
        }

        await _proveedores.DesactivarAsync(sesion, id, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "compra.proveedor_desactivacion", ActorAuditoria.Usuario, actor.Id, "proveedor", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
