using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Autorización en Application: exige sesión vigente, re-lee al usuario para
/// que desactivar revoque de inmediato (y expulsa la sesión), y solo concede
/// permisos explícitos de la matriz. Nunca opera como identidad inexistente.
/// </summary>
public sealed class Autorizador : IAutorizador
{
    private readonly GestorSesiones _sesiones;
    private readonly IRepositorioUsuarios _usuarios;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly MatrizPermisos _matriz;

    public Autorizador(
        GestorSesiones sesiones,
        IRepositorioUsuarios usuarios,
        IFabricaSesionesPersistencia fabrica,
        MatrizPermisos matriz)
    {
        _sesiones = sesiones;
        _usuarios = usuarios;
        _fabrica = fabrica;
        _matriz = matriz;
    }

    public async Task<UsuarioRegistrado> AutorizarAsync(string idSesion, string permiso, CancellationToken cancellationToken = default)
    {
        var sesion = _sesiones.Obtener(idSesion);
        if (sesion is null)
        {
            throw new SesionInvalidaException();
        }

        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        var usuario = await _usuarios.ObtenerPorIdAsync(lectura, sesion.UsuarioId, cancellationToken);
        if (usuario is null || !usuario.Activo)
        {
            _sesiones.Expulsar(idSesion);
            throw new SesionInvalidaException();
        }

        if (!_matriz.Permite(usuario.Rol, permiso))
        {
            throw new AccesoDenegadoException(permiso);
        }

        return usuario;
    }
}
