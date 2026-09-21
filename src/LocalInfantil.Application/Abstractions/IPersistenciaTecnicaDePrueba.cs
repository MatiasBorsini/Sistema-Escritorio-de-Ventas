namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Define la persistencia mínima necesaria para comprobar el acceso local durante el prototipo.
/// </summary>
public interface IPersistenciaTecnicaDePrueba
{
    Task InicializarAsync(CancellationToken cancellationToken = default);

    Task GuardarAsync(string clave, string valor, CancellationToken cancellationToken = default);

    Task<string?> LeerAsync(string clave, CancellationToken cancellationToken = default);
}
