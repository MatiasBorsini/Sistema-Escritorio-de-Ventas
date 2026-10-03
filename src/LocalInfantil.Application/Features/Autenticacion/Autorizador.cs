using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Autorización en Application: exige sesión vigente, re-lee al usuario para
/// que desactivar revoque de inmediato (y expulsa la sesión), y resuelve el
/// permiso efectivo: la concesión individual del usuario prevalece; en su
/// ausencia vale la matriz por rol. Nunca opera como identidad inexistente.
/// </summary>
public sealed class Autorizador : IAutorizador, IAutorizadorEnSesion
{
    private readonly GestorSesiones _sesiones;
    private readonly IRepositorioUsuarios _usuarios;
    private readonly IRepositorioPermisos _permisos;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly MatrizPermisos _matriz;
    private readonly IHashContrasenas? _hash;

    public Autorizador(GestorSesiones sesiones, IRepositorioUsuarios usuarios, IRepositorioPermisos permisos, IFabricaSesionesPersistencia fabrica, MatrizPermisos matriz)
        : this(sesiones, usuarios, permisos, fabrica, matriz, null) { }

    public Autorizador(
        GestorSesiones sesiones,
        IRepositorioUsuarios usuarios,
        IRepositorioPermisos permisos,
        IFabricaSesionesPersistencia fabrica,
        MatrizPermisos matriz,
        IHashContrasenas? hash)
    {
        _sesiones = sesiones;
        _usuarios = usuarios;
        _permisos = permisos;
        _fabrica = fabrica;
        _matriz = matriz;
        _hash = hash;
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

        var individual = await _permisos.ObtenerAsync(lectura, usuario.Id, permiso, cancellationToken);
        var concedido = individual ?? _matriz.Permite(usuario.Rol, permiso);
        if (!concedido)
        {
            throw new AccesoDenegadoException(permiso);
        }

        return usuario;
    }

    public async Task<IReadOnlySet<string>> ObtenerPermisosEfectivosAsync(string idSesion, IReadOnlyCollection<string> permisos, CancellationToken cancellationToken = default)
    {
        var sesion = _sesiones.Obtener(idSesion) ?? throw new SesionInvalidaException();
        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        var usuario = await _usuarios.ObtenerPorIdAsync(lectura, sesion.UsuarioId, cancellationToken);
        if (usuario is null || !usuario.Activo) { _sesiones.Expulsar(idSesion); throw new SesionInvalidaException(); }
        var definidos = await _permisos.ObtenerDefinidosAsync(lectura, usuario.Id, permisos, cancellationToken);
        return permisos.Where(p => definidos.TryGetValue(p, out var individual) ? individual : _matriz.Permite(usuario.Rol, p)).ToHashSet();
    }

    public async Task<UsuarioRegistrado> AutorizarAsync(ISesionPersistencia sesion, string idSesion, string permiso, CancellationToken cancellationToken = default)
    {
        var sesionUsuario = _sesiones.Obtener(idSesion) ?? throw new SesionInvalidaException();
        var usuario = await _usuarios.ObtenerPorIdAsync(sesion, sesionUsuario.UsuarioId, cancellationToken);
        if (usuario is null || !usuario.Activo)
        {
            _sesiones.Expulsar(idSesion);
            throw new SesionInvalidaException();
        }
        var individual = await _permisos.ObtenerAsync(sesion, usuario.Id, permiso, cancellationToken);
        if (!(individual ?? _matriz.Permite(usuario.Rol, permiso))) throw new AccesoDenegadoException(permiso);
        return usuario;
    }

    public async Task<UsuarioRegistrado> VerificarDuenaAsync(ISesionPersistencia sesion, string nombre, string contrasena, string permiso, CancellationToken cancellationToken = default)
    {
        var usuario = await _usuarios.ObtenerPorNombreAsync(sesion, nombre, cancellationToken);
        if (usuario is null || !usuario.Activo || usuario.Rol != RolesLocal.Duena)
            throw new CredencialesInvalidasException();
        var huella = await _usuarios.ObtenerHuellaAsync(sesion, usuario.Id, cancellationToken);
        if (_hash is null || !_hash.Verificar(contrasena, huella)) throw new CredencialesInvalidasException();
        var individual = await _permisos.ObtenerAsync(sesion, usuario.Id, permiso, cancellationToken);
        if (!(individual ?? _matriz.Permite(usuario.Rol, permiso))) throw new AccesoDenegadoException(permiso);
        return usuario;
    }

}
