using LocalInfantil.Application.Abstractions;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Abre sesiones SQLite sobre una ruta explícita. Cada conexión habilita
/// claves foráneas y el tiempo de espera ante bloqueos configurado.
/// Las sesiones de escritura toman el bloqueo con BEGIN IMMEDIATE al iniciar;
/// las de lectura no abren transacción. Todos los comandos (incluido el BEGIN)
/// usan un tiempo máximo derivado de la espera configurada, de modo que una
/// contienda falla rápido con <see cref="AlmacenamientoOcupadoException"/> en
/// lugar de colgar. El modo de journaling NO se toca en cada apertura: se
/// establece una sola vez con <see cref="EstablecerJournalAsync"/> (ver docs/ESQUEMA.md).
/// </summary>
public sealed class FabricaConexionesSqlite : IFabricaSesionesPersistencia
{
    private static readonly string[] ModosJournalAdmitidos = { "delete", "wal" };

    private readonly OpcionesConexionSqlite _opciones;

    public FabricaConexionesSqlite(OpcionesConexionSqlite opciones)
    {
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
    }

    /// <summary>Tiempo máximo por comando, en segundos, derivado de la espera configurada.</summary>
    public int TimeoutSegundos => Math.Max(1, (_opciones.EsperaBloqueoMs + 999) / 1000);

    public Task<ISesionPersistencia> IniciarAsync(CancellationToken cancellationToken = default)
    {
        return IniciarSesionAsync(escribir: true, cancellationToken);
    }

    public Task<ISesionPersistencia> IniciarLecturaAsync(CancellationToken cancellationToken = default)
    {
        return IniciarSesionAsync(escribir: false, cancellationToken);
    }

    /// <summary>
    /// Establece el modo de journaling una sola vez y devuelve el modo efectivo.
    /// Solo admite "delete" o "wal": cualquier otro valor se rechaza.
    /// </summary>
    public async Task<string> EstablecerJournalAsync(string modo, CancellationToken cancellationToken = default)
    {
        var normalizado = (modo ?? string.Empty).Trim().ToLowerInvariant();
        if (!ModosJournalAdmitidos.Contains(normalizado))
        {
            throw new ArgumentException("Modo de journaling no admitido. Valores admitidos: delete, wal.", nameof(modo));
        }

        await using var conexion = await AbrirConexionAsync(cancellationToken);
        using var comando = conexion.CreateCommand();
        comando.CommandText = $"PRAGMA journal_mode = {normalizado};";
        comando.CommandTimeout = TimeoutSegundos;
        var efectivo = await comando.ExecuteScalarAsync(cancellationToken) as string;
        return (efectivo ?? string.Empty).ToLowerInvariant();
    }

    private async Task<ISesionPersistencia> IniciarSesionAsync(bool escribir, CancellationToken cancellationToken)
    {
        var conexion = await AbrirConexionAsync(cancellationToken);
        try
        {
            if (escribir)
            {
                try
                {
                    await EjecutarConTimeoutAsync(conexion, "BEGIN IMMEDIATE;", cancellationToken);
                }
                catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
                {
                    throw ErroresSqlite.OcupadoException(ex);
                }
            }

            return new SesionPersistenciaSqlite(conexion, TimeoutSegundos, soloLectura: !escribir, transaccionAbierta: escribir);
        }
        catch
        {
            await conexion.DisposeAsync();
            throw;
        }
    }

    private async Task<SqliteConnection> AbrirConexionAsync(CancellationToken cancellationToken)
    {
        var directorio = Path.GetDirectoryName(_opciones.RutaBaseDatos)
            ?? throw new InvalidOperationException("No se pudo determinar el directorio de la base de datos.");
        Directory.CreateDirectory(directorio);

        var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _opciones.RutaBaseDatos,
            Pooling = false,
        }.ToString());

        try
        {
            await conexion.OpenAsync(cancellationToken);
            await EjecutarPragmaAsync(conexion, "PRAGMA foreign_keys = ON;", cancellationToken);
            await EjecutarPragmaAsync(conexion, $"PRAGMA busy_timeout = {_opciones.EsperaBloqueoMs};", cancellationToken);
            return conexion;
        }
        catch
        {
            await conexion.DisposeAsync();
            throw;
        }
    }

    private async Task EjecutarPragmaAsync(SqliteConnection conexion, string pragma, CancellationToken cancellationToken)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = pragma;
        comando.CommandTimeout = TimeoutSegundos;
        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EjecutarConTimeoutAsync(SqliteConnection conexion, string sql, CancellationToken cancellationToken)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        comando.CommandTimeout = TimeoutSegundos;
        await comando.ExecuteNonQueryAsync(cancellationToken);
    }
}
