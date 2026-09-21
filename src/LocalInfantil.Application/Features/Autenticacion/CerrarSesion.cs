using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>Cierra una sesión local. Cerrar una sesión inexistente no es un error.</summary>
public sealed class CerrarSesion
{
    private readonly GestorSesiones _sesiones;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;

    public CerrarSesion(
        GestorSesiones sesiones,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids)
    {
        _sesiones = sesiones;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
    }

    public async Task<bool> EjecutarAsync(string idSesion, CancellationToken cancellationToken = default)
    {
        var sesion = _sesiones.Obtener(idSesion);
        if (sesion is null)
        {
            return false;
        }

        _sesiones.Expulsar(idSesion);

        await using var escritura = await _fabrica.IniciarAsync(cancellationToken);
        await _auditoria.RegistrarAsync(
            escritura,
            new NuevoEventoAuditoria(_ids.NuevoId(), "autenticacion.cierre", ActorAuditoria.Usuario, sesion.UsuarioId, "usuario", sesion.UsuarioId, null),
            cancellationToken);
        await escritura.ConfirmarAsync(cancellationToken);
        return true;
    }
}
