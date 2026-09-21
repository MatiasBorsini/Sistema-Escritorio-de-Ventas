namespace LocalInfantil.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Opciones de conexión SQLite. La ruta es explícita (nunca se deduce la
/// producción: D-13 sigue pendiente). El tiempo de espera ante bloqueos evita
/// fallos inmediatos cuando otra conexión escribe, sin congelar al usuario:
/// el trabajo de E/S corre fuera del hilo UI y 5 segundos cubren bloqueos
/// breves de lectores/checkpoints en una PC de un solo puesto.
/// </summary>
public sealed class OpcionesConexionSqlite
{
    /// <summary>Espera máxima ante SQLITE_BUSY/SQLITE_LOCKED, en milisegundos. Por defecto 5000.</summary>
    public const int EsperaBloqueoMsPorDefecto = 5000;

    public OpcionesConexionSqlite(string rutaBaseDatos, int esperaBloqueoMs = EsperaBloqueoMsPorDefecto)
    {
        if (string.IsNullOrWhiteSpace(rutaBaseDatos))
        {
            throw new ArgumentException("La ruta de la base de datos es obligatoria.", nameof(rutaBaseDatos));
        }

        if (esperaBloqueoMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(esperaBloqueoMs), "El tiempo de espera debe ser mayor a cero.");
        }

        RutaBaseDatos = Path.GetFullPath(rutaBaseDatos);
        EsperaBloqueoMs = esperaBloqueoMs;
    }

    public string RutaBaseDatos { get; }

    public int EsperaBloqueoMs { get; }
}
