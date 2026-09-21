namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Inicia sesiones de trabajo sobre la base sin exponer el proveedor SQLite.
/// </summary>
public interface IFabricaSesionesPersistencia
{
    /// <summary>Sesión de escritura: toma el bloqueo al iniciar.</summary>
    Task<ISesionPersistencia> IniciarAsync(CancellationToken cancellationToken = default);

    /// <summary>Sesión de lectura: sin transacción, nunca bloquea escritores. Confirmarla es un error.</summary>
    Task<ISesionPersistencia> IniciarLecturaAsync(CancellationToken cancellationToken = default);
}
