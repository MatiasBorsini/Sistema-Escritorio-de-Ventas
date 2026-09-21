using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Compras;

/// <summary>Alta de proveedor con auditoría. Sin datos de contacto/CUIT: propuestas no aprobadas.</summary>
public sealed class CrearProveedor
{
    private readonly IRepositorioProveedores _proveedores;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public CrearProveedor(
        IRepositorioProveedores proveedores,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _proveedores = proveedores;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task<string> EjecutarAsync(NuevoProveedor datos, string? idSesion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCompras.ProveedoresGestionar, cancellationToken);

        if (string.IsNullOrWhiteSpace(datos.Nombre))
        {
            throw new ArgumentException("El nombre es obligatorio.", nameof(datos));
        }

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var id = await _proveedores.CrearAsync(sesion, datos, _reloj.UtcNow().ToString("o"), cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "compra.proveedor_alta", ActorAuditoria.Usuario, actor.Id, "proveedor", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
