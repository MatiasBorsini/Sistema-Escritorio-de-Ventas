using System.Security.Cryptography;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Inicia sesión local. El error es genérico (no revela si el nombre existe);
/// ante usuario inexistente, inactivo o sin huella se verifica un señuelo para
/// no delatar por tiempos, se espera el retardo configurado y se audita el
/// fallo sin el nombre intentado. La huella nula jamás autentica.
/// </summary>
public sealed class IniciarSesion
{
    private readonly IRepositorioUsuarios _usuarios;
    private readonly IHashContrasenas _hash;
    private readonly GestorSesiones _sesiones;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;
    private readonly OpcionesAutenticacion _opciones;
    private readonly string _huellaSenuelo;

    public IniciarSesion(
        IRepositorioUsuarios usuarios,
        IHashContrasenas hash,
        GestorSesiones sesiones,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj,
        OpcionesAutenticacion opciones)
    {
        _usuarios = usuarios;
        _hash = hash;
        _sesiones = sesiones;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
        _opciones = opciones;
        _huellaSenuelo = hash.CrearHuella(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)));
    }

    public async Task<SesionUsuario> EjecutarAsync(string nombre, string contrasena, CancellationToken cancellationToken = default)
    {
        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        var usuario = await _usuarios.ObtenerPorNombreAsync(lectura, nombre, cancellationToken);
        var huella = usuario is not null ? await _usuarios.ObtenerHuellaAsync(lectura, usuario.Id, cancellationToken) : null;

        if (usuario is null || !usuario.Activo || !_hash.Verificar(contrasena ?? string.Empty, huella))
        {
            if (huella is null)
            {
                _hash.Verificar(contrasena ?? string.Empty, _huellaSenuelo);
            }

            await Task.Delay(_opciones.RetrasoFalloMs, cancellationToken);
            await AuditarAsync(null, "autenticacion.fallo", cancellationToken);
            throw new CredencialesInvalidasException();
        }

        var sesion = new SesionUsuario(_ids.NuevoId(), usuario.Id, usuario.Nombre, usuario.Rol, _reloj.UtcNow().ToString("o"));
        _sesiones.Registrar(sesion);
        await AuditarAsync(usuario.Id, "autenticacion.inicio", cancellationToken);
        return sesion;
    }

    private async Task AuditarAsync(string? actorId, string tipo, CancellationToken cancellationToken)
    {
        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var actorTipo = actorId is null ? ActorAuditoria.Sistema : ActorAuditoria.Usuario;
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), tipo, actorTipo, actorId, "usuario", actorId, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
