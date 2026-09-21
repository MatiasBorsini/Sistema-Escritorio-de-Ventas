using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Desactiva una cuenta local. Exige sesión vigente con el permiso explícito
/// `usuarios.administrar`. Nunca desactiva la última cuenta administradora
/// activa: el local no debe quedarse sin gestión. Auditoría en la misma
/// transacción.
/// </summary>
public sealed class DesactivarUsuario
{
    private readonly IRepositorioUsuarios _usuarios;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;

    public DesactivarUsuario(
        IRepositorioUsuarios usuarios,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids)
    {
        _usuarios = usuarios;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
    }

    public async Task EjecutarAsync(string usuarioId, string? idSesionActor, CancellationToken cancellationToken = default)
    {
        if (idSesionActor is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesionActor, PermisosAdministracion.UsuariosAdministrar, cancellationToken);

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var objetivo = await _usuarios.ObtenerPorIdAsync(sesion, usuarioId, cancellationToken);
        if (objetivo is null)
        {
            throw new InvalidOperationException("El usuario no existe.");
        }

        if (objetivo.Activo
            && objetivo.Rol == RolesLocal.Administrador
            && await _usuarios.ContarAdministradoresActivosAsync(sesion, cancellationToken) <= 1)
        {
            throw new InvalidOperationException("No se puede desactivar la última cuenta administradora activa.");
        }

        await _usuarios.DesactivarAsync(sesion, usuarioId, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "usuario.desactivacion", ActorAuditoria.Usuario, actor.Id, "usuario", usuarioId, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
