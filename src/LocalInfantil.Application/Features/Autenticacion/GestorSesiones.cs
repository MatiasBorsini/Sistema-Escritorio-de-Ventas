using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>Registro en memoria de sesiones iniciadas. Sin expiración automática:
/// la política de caducidad no está decidida y un cierre arbitrario podría
/// paralizar el local; documentado como pendiente.</summary>
public sealed class GestorSesiones
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SesionUsuario> _activas = new();

    public void Registrar(SesionUsuario sesion)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        if (!_activas.TryAdd(sesion.IdSesion, sesion))
        {
            throw new InvalidOperationException("La sesión ya estaba registrada.");
        }
    }

    public SesionUsuario? Obtener(string idSesion)
    {
        return _activas.TryGetValue(idSesion, out var sesion) ? sesion : null;
    }

    public bool Expulsar(string idSesion)
    {
        return _activas.TryRemove(idSesion, out _);
    }
}
