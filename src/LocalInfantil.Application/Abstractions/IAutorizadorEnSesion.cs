using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

/// <summary>Reutiliza las mismas reglas de autorización leyendo dentro de la sesión de escritura existente.</summary>
public interface IAutorizadorEnSesion
{
    Task<UsuarioRegistrado> AutorizarAsync(ISesionPersistencia sesion, string idSesion, string permiso, CancellationToken cancellationToken = default);
    Task<UsuarioRegistrado> VerificarDuenaAsync(ISesionPersistencia sesion, string nombre, string contrasena, string permiso, CancellationToken cancellationToken = default);
}
