using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Respaldos;

/// <summary>
/// Respaldo local consistente de SQLite mediante VACUUM INTO: instantánea
/// atómica válida con la base en uso y en cualquier modo de journaling (el
/// contenido WAL se consolida en la copia). Nunca copia el .db en caliente
/// ni arrastra archivos -wal/-shm/-journal: solo se crea el .db destino.
/// Publicación segura: VACUUM INTO escribe un temporal exclusivo, que se
/// verifica y recién entonces se publica con un movimiento atómico que nunca
/// sobrescribe. Un respaldo cuenta como verificado únicamente tras
/// integrity_check ok, foreign_key_check vacío, esquema reconocido y
/// versiones esperadas (si se indican). Ante cualquier fallo no queda éxito
/// falso: solo se limpia el temporal propio y los respaldos anteriores no se
/// tocan. Sin reintentos.
/// </summary>
public sealed class ServicioRespaldos : IServicioRespaldos
{
    private const int TimeoutSegundos = 60;

    private readonly IReloj _reloj;

    public ServicioRespaldos(IReloj reloj)
    {
        _reloj = reloj ?? throw new ArgumentNullException(nameof(reloj));
    }

    public async Task<RespaldoVerificado> CrearRespaldoAsync(
        string rutaOrigen,
        string directorioDestino,
        IReadOnlyList<int>? versionesEsperadas,
        string prefijo = "respaldo",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rutaOrigen))
        {
            throw new ArgumentException("La ruta de origen es obligatoria.", nameof(rutaOrigen));
        }

        if (string.IsNullOrWhiteSpace(directorioDestino))
        {
            throw new ArgumentException("El directorio de destino es obligatorio.", nameof(directorioDestino));
        }

        if (!File.Exists(rutaOrigen))
        {
            throw new RespaldoFallidoException("La base de origen no existe; no se crea un respaldo vacío.");
        }

        string destino;
        try
        {
            Directory.CreateDirectory(directorioDestino);
        }
        catch (Exception ex)
        {
            throw new RespaldoFallidoException("No se pudo preparar el directorio de destino.", ex);
        }

        var nombre = $"{prefijo}-{_reloj.UtcNow():yyyyMMddHHmmss}-{Guid.NewGuid():N}.db";
        destino = Path.Combine(directorioDestino, nombre);
        if (File.Exists(destino))
        {
            throw new RespaldoFallidoException("El archivo de respaldo ya existe; no se sobrescribe.");
        }

        // Temporal exclusivo en el mismo directorio (mismo volumen: el
        // movimiento final es atómico). El nombre definitivo solo aparece
        // cuando el respaldo ya está verificado.
        var temporal = destino + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using var origen = await AbrirAsync(rutaOrigen, readOnly: false, cancellationToken);
            using var vacuum = CrearComando(origen, $"VACUUM INTO '{temporal.Replace("'", "''")}';");
            await vacuum.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            BorrarParcial(temporal);
            throw new RespaldoFallidoException("Falló la generación del respaldo.", ex);
        }

        try
        {
            var verificacion = await VerificarAsync(temporal, versionesEsperadas, cancellationToken);
            File.Move(temporal, destino);
            return new RespaldoVerificado(destino, verificacion.Versiones, verificacion.TamanoBytes, _reloj.UtcNow().ToString("o"));
        }
        catch (Exception)
        {
            BorrarParcial(temporal);
            throw;
        }
    }

    public async Task<VerificacionRespaldo> VerificarAsync(
        string rutaRespaldo,
        IReadOnlyList<int>? versionesEsperadas,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rutaRespaldo) || !File.Exists(rutaRespaldo))
        {
            throw new RespaldoInvalidoException("El archivo de respaldo no existe.");
        }

        try
        {
            await using var conexion = await AbrirAsync(rutaRespaldo, readOnly: true, cancellationToken);

            using (var integridad = CrearComando(conexion, "PRAGMA integrity_check;"))
            {
                var resultado = await integridad.ExecuteScalarAsync(cancellationToken) as string;
                if (!string.Equals(resultado, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new RespaldoInvalidoException($"El respaldo no superó integrity_check: {resultado}.");
                }
            }

            using (var claves = CrearComando(conexion, "PRAGMA foreign_key_check;"))
            {
                await using var lector = await claves.ExecuteReaderAsync(cancellationToken);
                if (await lector.ReadAsync(cancellationToken))
                {
                    throw new RespaldoInvalidoException("El respaldo tiene claves foráneas rotas.");
                }
            }

            List<int> versiones;
            using (var bitacora = CrearComando(conexion, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_migrations';"))
            {
                if (Convert.ToInt64(await bitacora.ExecuteScalarAsync(cancellationToken)) == 0)
                {
                    throw new RespaldoInvalidoException("Esquema no reconocido: sin historial de migraciones.");
                }
            }

            versiones = new List<int>();
            using (var consulta = CrearComando(conexion, "SELECT version FROM schema_migrations ORDER BY version;"))
            {
                await using var lector = await consulta.ExecuteReaderAsync(cancellationToken);
                while (await lector.ReadAsync(cancellationToken))
                {
                    versiones.Add(lector.GetInt32(0));
                }
            }

            if (versionesEsperadas is not null
                && (versiones.Count != versionesEsperadas.Count || !versiones.OrderBy(v => v).SequenceEqual(versionesEsperadas.OrderBy(v => v))))
            {
                throw new RespaldoInvalidoException(
                    $"Versiones inesperadas: [{string.Join(", ", versiones)}], esperadas: [{string.Join(", ", versionesEsperadas)}].");
            }

            return new VerificacionRespaldo(versiones, new FileInfo(rutaRespaldo).Length);
        }
        catch (RespaldoInvalidoException)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            throw new RespaldoInvalidoException("El archivo no es una base SQLite legible.", ex);
        }
    }

    public async Task<string> RestaurarEnsayoAsync(
        string rutaRespaldo,
        string directorioTemporal,
        IReadOnlyList<int>? versionesEsperadas,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directorioTemporal))
        {
            throw new ArgumentException("El directorio temporal es obligatorio.", nameof(directorioTemporal));
        }

        await VerificarAsync(rutaRespaldo, versionesEsperadas, cancellationToken);

        Directory.CreateDirectory(directorioTemporal);
        var destino = Path.Combine(directorioTemporal, Path.GetFileName(rutaRespaldo));
        if (File.Exists(destino))
        {
            throw new RespaldoFallidoException("El destino del ensayo ya existe; no se sobrescribe.");
        }

        // Solo el .db verificado: jamás viajan -wal/-shm/-journal obsoletos.
        try
        {
            File.Copy(rutaRespaldo, destino);
        }
        catch (Exception ex)
        {
            throw new RespaldoFallidoException("No se pudo copiar el respaldo al directorio temporal.", ex);
        }

        try
        {
            await using var conexion = await AbrirAsync(destino, readOnly: false, cancellationToken);
            using var integridad = CrearComando(conexion, "PRAGMA integrity_check;");
            var resultado = await integridad.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.Equals(resultado, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new RespaldoInvalidoException("La copia de ensayo no superó integrity_check.");
            }
        }
        catch (RespaldoInvalidoException)
        {
            BorrarParcial(destino);
            throw;
        }
        catch (Exception ex)
        {
            BorrarParcial(destino);
            throw new RespaldoFallidoException("No se pudo abrir la copia de ensayo.", ex);
        }

        return destino;
    }

    private static async Task<SqliteConnection> AbrirAsync(string ruta, bool readOnly, CancellationToken cancellationToken)
    {
        var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
        }.ToString());
        await conexion.OpenAsync(cancellationToken);
        return conexion;
    }

    private static SqliteCommand CrearComando(SqliteConnection conexion, string sql)
    {
        var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        comando.CommandTimeout = TimeoutSegundos;
        return comando;
    }

    private static void BorrarParcial(string ruta)
    {
        try
        {
            if (File.Exists(ruta))
            {
                File.Delete(ruta);
            }
        }
        catch (IOException)
        {
            // Mejor esfuerzo: si no se puede limpiar, el error original manda.
        }
    }
}
