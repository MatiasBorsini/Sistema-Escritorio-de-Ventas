using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Restablece la huella de una cuenta. Exige sesión vigente con el permiso
/// explícito `usuarios.administrar` (hoy solo Administrador): Dueña y
/// empleada no cambian ni su propia contraseña. Si el actor restablece su
/// propia contraseña, debe proporcionar la actual. El procedimiento para
/// recuperar el acceso si el Administrador olvida la suya sigue pendiente:
/// sin contraseñas maestras, puertas traseras ni recuperaciones anónimas.
/// </summary>
public sealed class RestablecerContrasena
{
    private readonly IRepositorioUsuarios _usuarios;
    private readonly IHashContrasenas _hash;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;

    public RestablecerContrasena(
        IRepositorioUsuarios usuarios,
        IHashContrasenas hash,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids)
    {
        _usuarios = usuarios;
        _hash = hash;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
    }

    public async Task EjecutarAsync(
        string usuarioId,
        string nuevaContrasena,
        string? idSesionActor,
        string? contrasenaActual,
        CancellationToken cancellationToken = default)
    {
        if (idSesionActor is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesionActor, PermisosAdministracion.UsuariosAdministrar, cancellationToken);
        var huellaNueva = _hash.CrearHuella(nuevaContrasena);

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var usuario = await _usuarios.ObtenerPorIdAsync(sesion, usuarioId, cancellationToken);
        if (usuario is null)
        {
            throw new InvalidOperationException("El usuario no existe.");
        }

        if (actor.Id == usuarioId)
        {
            var huellaActual = await _usuarios.ObtenerHuellaAsync(sesion, usuarioId, cancellationToken);
            if (!_hash.Verificar(contrasenaActual ?? string.Empty, huellaActual))
            {
                throw new CredencialesInvalidasException();
            }
        }

        await _usuarios.CambiarHuellaAsync(sesion, usuarioId, huellaNueva, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "usuario.contrasena_restablecida", ActorAuditoria.Usuario, actor.Id, "usuario", usuarioId, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
