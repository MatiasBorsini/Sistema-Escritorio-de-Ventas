using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Verifica que una sesión válida tenga un permiso. Separa autenticación de
/// autorización y se ejecuta en Application, no en la vista.
/// </summary>
public interface IAutorizador
{
    Task<UsuarioRegistrado> AutorizarAsync(string idSesion, string permiso, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> ObtenerPermisosEfectivosAsync(string idSesion, IReadOnlyCollection<string> permisos, CancellationToken cancellationToken = default);
}
