using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Alta de cuenta local con su huella.
/// - Alta inicial: solo en base que nunca tuvo usuarios (ni desactivados),
///   sin sesión y en la misma transacción que la creación. La primera cuenta
///   es obligatoriamente Administrador, sin selección libre de rol.
/// - Alta posterior: exige sesión vigente con el permiso explícito
///   `usuarios.administrar` (hoy solo Administrador). El rol Administrador
///   no se asigna por esta vía: sin política explícita se deniega.
/// </summary>
public sealed class CrearUsuario
{
    private readonly IRepositorioUsuarios _usuarios;
    private readonly IHashContrasenas _hash;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public CrearUsuario(
        IRepositorioUsuarios usuarios,
        IHashContrasenas hash,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _usuarios = usuarios;
        _hash = hash;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task<string> EjecutarAsync(NuevoUsuario datos, string contrasena, string? idSesionActor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var huella = _hash.CrearHuella(contrasena);
        var ahora = _reloj.UtcNow().ToString("o");

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        string? actorId = null;
        string rol;
        if (await _usuarios.ContarTodosAsync(sesion, cancellationToken) == 0)
        {
            if (idSesionActor is not null)
            {
                throw new SesionInvalidaException();
            }

            rol = RolesLocal.Administrador;
        }
        else
        {
            if (idSesionActor is null)
            {
                throw new SesionInvalidaException();
            }

            var autorizado = await _autorizador.AutorizarAsync(idSesionActor, PermisosAdministracion.UsuariosAdministrar, cancellationToken);
            if (datos.Rol == RolesLocal.Administrador)
            {
                throw new AccesoDenegadoException("asignación del rol Administrador (sin política explícita)");
            }

            actorId = autorizado.Id;
            rol = datos.Rol;
        }

        var id = await _usuarios.CrearAsync(sesion, new NuevoUsuario(datos.Nombre, rol), huella, ahora, cancellationToken);
        var actorTipo = actorId is null ? ActorAuditoria.Sistema : ActorAuditoria.Usuario;
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "usuario.alta", actorTipo, actorId, "usuario", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
