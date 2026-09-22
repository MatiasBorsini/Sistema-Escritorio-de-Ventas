using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Concede o retira un permiso comercial a un usuario individual, solo por
/// sesión vigente con `usuarios.administrar` (hoy solo Dueña). El permiso
/// técnico `usuarios.administrar` no es configurable por esta vía. Concesión
/// y auditoría comparten transacción: todo o nada.
/// </summary>
public sealed class ConfigurarPermiso
{
    private readonly IRepositorioPermisos _permisos;
    private readonly IRepositorioUsuarios _usuarios;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public ConfigurarPermiso(
        IRepositorioPermisos permisos,
        IRepositorioUsuarios usuarios,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _permisos = permisos;
        _usuarios = usuarios;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task EjecutarAsync(string usuarioId, string permiso, bool conceder, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(permiso))
        {
            throw new ArgumentException("El permiso es obligatorio.", nameof(permiso));
        }

        if (permiso == PermisosAdministracion.UsuariosAdministrar)
        {
            throw new AccesoDenegadoException(permiso);
        }

        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosAdministracion.UsuariosAdministrar, cancellationToken);

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var objetivo = await _usuarios.ObtenerPorIdAsync(sesion, usuarioId, cancellationToken);
        if (objetivo is null)
        {
            throw new InvalidOperationException("El usuario no existe.");
        }

        var actual = await _permisos.ObtenerAsync(sesion, usuarioId, permiso, cancellationToken);
        if (conceder)
        {
            if (actual is true)
            {
                throw new InvalidOperationException("El permiso ya está concedido.");
            }

            await _permisos.DefinirAsync(sesion, usuarioId, permiso, true, _reloj.UtcNow().ToString("o"), actor.Id, cancellationToken);
        }
        else
        {
            await _permisos.DefinirAsync(sesion, usuarioId, permiso, false, _reloj.UtcNow().ToString("o"), actor.Id, cancellationToken);
        }

        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(
                _ids.NuevoId(),
                conceder ? "autorizacion.permiso_otorgado" : "autorizacion.permiso_retirado",
                ActorAuditoria.Usuario,
                actor.Id,
                "usuario",
                usuarioId,
                permiso),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
