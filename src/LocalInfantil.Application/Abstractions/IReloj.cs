namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Fecha y hora actual en UTC. La versión real lee el reloj del sistema;
/// las pruebas inyectan un reloj fijo determinista. Se persiste en el
/// formato UTC ISO-8601 del proyecto.
/// </summary>
public interface IReloj
{
    DateTimeOffset UtcNow();
}
