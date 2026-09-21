using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Migrations;

/// <summary>
/// Aplica migraciones versionadas sobre una base SQLite indicada por ruta.
/// Reglas obligatorias:
/// - Cada versión se aplica una sola vez y queda registrada en schema_migrations.
/// - Nunca se borra ni se recrea una base existente para actualizarla.
/// - Una base preexistente con contenido no recibe ninguna modificación de
///   esquema antes de generar y verificar su respaldo.
/// - Solo se admiten dos casos: base nueva/vacía, o base con historial
///   schema_migrations propio. Un esquema heredado sin historial se rechaza
///   sin tocarlo.
/// - El historial aplicado debe ser un prefijo exacto del catálogo: sin
///   versiones desconocidas ni saltos (p. ej. V2 sin V1 se rechaza).
/// - Si la base ya contiene datos y hay migraciones pendientes, antes se genera
///   un respaldo consistente (VACUUM INTO) y se verifica con integrity_check.
///   Sin directorio de respaldos, la migración con datos se niega.
/// - Cada migración corre en su propia transacción: si falla, esa versión se
///   revierte y la excepción se propaga para que la aplicación no opere con
///   una estructura incompleta. Las versiones anteriores ya confirmadas quedan
///   registradas; las pendientes siguen pendientes y se retoman en la próxima
///   ejecución. No hay atomicidad entre versiones distintas.
/// No está conectado al arranque de WPF: solo se usa con rutas explícitas
/// (las pruebas usan bases temporales aisladas).
/// </summary>
public sealed class MigradorBaseDatos
{
    private readonly string _rutaBaseDatos;

    public MigradorBaseDatos(string rutaBaseDatos)
    {
        if (string.IsNullOrWhiteSpace(rutaBaseDatos))
        {
            throw new ArgumentException("La ruta de la base de datos es obligatoria.", nameof(rutaBaseDatos));
        }

        _rutaBaseDatos = Path.GetFullPath(rutaBaseDatos);
    }

    /// <summary>
    /// Aplica las migraciones F01 pendientes y devuelve las versiones aplicadas.
    /// </summary>
    public Task<IReadOnlyList<int>> MigrarAsync(CancellationToken cancellationToken = default)
    {
        return MigrarAsync(MigracionesF01.Todas, null, cancellationToken);
    }

    /// <summary>
    /// Aplica las migraciones pendientes del catálogo indicado. El catálogo
    /// inyectable permite probar actualizaciones desde una versión anterior
    /// con registros ficticios sin inventar esquema productivo.
    /// </summary>
    public async Task<IReadOnlyList<int>> MigrarAsync(
        IReadOnlyList<Migracion> catalogo,
        string? directorioRespaldos = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        var versiones = new HashSet<int>();
        foreach (var migracion in catalogo)
        {
            if (!versiones.Add(migracion.Version))
            {
                throw new InvalidOperationException($"El catálogo contiene la versión duplicada {migracion.Version}.");
            }
        }

        var directorio = Path.GetDirectoryName(_rutaBaseDatos)
            ?? throw new InvalidOperationException("No se pudo determinar el directorio de la base de datos.");
        Directory.CreateDirectory(directorio);

        var archivoPrevio = new FileInfo(_rutaBaseDatos);
        var esNueva = !archivoPrevio.Exists || archivoPrevio.Length == 0;

        var cadenaConexion = new SqliteConnectionStringBuilder
        {
            DataSource = _rutaBaseDatos,
            Pooling = false,
        }.ToString();

        await using var conexion = new SqliteConnection(cadenaConexion);
        await conexion.OpenAsync(cancellationToken);

        using (var pragma = conexion.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
        }

        // Inspección de solo lectura: hasta aquí no se escribió nada en la base.
        var existeBitacora = await ExisteTablaAsync(conexion, "schema_migrations", cancellationToken);
        var aplicadas = new HashSet<int>();
        if (existeBitacora)
        {
            using var consulta = conexion.CreateCommand();
            consulta.CommandText = "SELECT version FROM schema_migrations;";
            await using var lector = await consulta.ExecuteReaderAsync(cancellationToken);
            while (await lector.ReadAsync(cancellationToken))
            {
                aplicadas.Add(lector.GetInt32(0));
            }
        }

        // Casos admitidos: base nueva/vacía, o base con nuestro historial.
        // Un esquema heredado sin historial se rechaza explícitamente sin tocarlo
        // (no se adopta automáticamente; tampoco la base técnica de F00).
        if (!esNueva && !existeBitacora)
        {
            throw new InvalidOperationException(
                "La base existe pero no tiene historial de migraciones reconocido. " +
                "Solo se admiten bases nuevas o con schema_migrations propio; " +
                "los esquemas heredados no se migran automáticamente.");
        }

        var ordenadas = catalogo.OrderBy(m => m.Version).Select(m => m.Version).ToList();
        var aplicadasOrdenadas = aplicadas.OrderBy(v => v).ToList();

        // Sin versiones desconocidas: la base no puede estar por delante del catálogo.
        var conocidas = new HashSet<int>(ordenadas);
        var desconocidas = aplicadasOrdenadas.Where(v => !conocidas.Contains(v)).ToList();
        if (desconocidas.Count > 0)
        {
            throw new InvalidOperationException(
                $"La base registra versiones desconocidas ({string.Join(", ", desconocidas)}). " +
                "Actualizá la aplicación antes de operar con esta base.");
        }

        // Sin saltos: lo aplicado debe ser un prefijo exacto del catálogo
        // (p. ej. V2 sin V1 se rechaza sin ejecutar migraciones).
        for (var i = 0; i < aplicadasOrdenadas.Count; i++)
        {
            if (aplicadasOrdenadas[i] != ordenadas[i])
            {
                throw new InvalidOperationException(
                    $"Historial de migraciones incoherente: la base registra la versión {aplicadasOrdenadas[i]} " +
                    $"sin haber aplicado la versión {ordenadas[i]}.");
            }
        }

        var pendientes = catalogo.OrderBy(m => m.Version).Where(m => !aplicadas.Contains(m.Version)).ToList();
        if (pendientes.Count == 0)
        {
            return Array.Empty<int>();
        }

        // Solo una base preexistente con contenido cuenta como "base con datos".
        // La tabla schema_migrations creada arriba sobre un archivo nuevo no lo es.
        if (!esNueva)
        {
            if (string.IsNullOrWhiteSpace(directorioRespaldos))
            {
                throw new InvalidOperationException(
                    "La base contiene datos y no se indicó directorio de respaldos. " +
                    "Toda migración con datos exige respaldo previo verificado.");
            }

            await RespaldarYVerificarAsync(conexion, aplicadas, directorioRespaldos, cancellationToken);
        }

        // Primera escritura admitida: recién después de validar el historial
        // y de generar el respaldo. En bases preexistentes esta tabla ya existe.
        using (var bitacora = conexion.CreateCommand())
        {
            bitacora.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    nombre TEXT NOT NULL,
                    aplicada_en TEXT NOT NULL
                );
                """;
            await bitacora.ExecuteNonQueryAsync(cancellationToken);
        }

        var resultado = new List<int>();
        foreach (var migracion in pendientes)
        {
            using var transaccion = conexion.BeginTransaction();
            try
            {
                using (var ddl = conexion.CreateCommand())
                {
                    ddl.Transaction = transaccion;
                    ddl.CommandText = migracion.Sql;
                    await ddl.ExecuteNonQueryAsync(cancellationToken);
                }

                using (var registro = conexion.CreateCommand())
                {
                    registro.Transaction = transaccion;
                    registro.CommandText = "INSERT INTO schema_migrations (version, nombre, aplicada_en) VALUES ($version, $nombre, $aplicadaEn);";
                    registro.Parameters.AddWithValue("$version", migracion.Version);
                    registro.Parameters.AddWithValue("$nombre", migracion.Nombre);
                    registro.Parameters.AddWithValue("$aplicadaEn", DateTime.UtcNow.ToString("o"));
                    await registro.ExecuteNonQueryAsync(cancellationToken);
                }

                transaccion.Commit();
                resultado.Add(migracion.Version);
            }
            catch
            {
                transaccion.Rollback();
                throw;
            }
        }

        return resultado;
    }

    private static async Task<bool> ExisteTablaAsync(
        SqliteConnection conexion,
        string tabla,
        CancellationToken cancellationToken)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tabla;";
        comando.Parameters.AddWithValue("$tabla", tabla);
        return Convert.ToInt64(await comando.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private async Task RespaldarYVerificarAsync(
        SqliteConnection conexion,
        HashSet<int> aplicadas,
        string directorioRespaldos,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directorioRespaldos);
        var versionActual = aplicadas.Count == 0 ? 0 : aplicadas.Max();
        var respaldo = Path.Combine(
            directorioRespaldos,
            $"respaldo-previo-v{versionActual}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.db");

        using (var vacuum = conexion.CreateCommand())
        {
            vacuum.CommandText = $"VACUUM INTO '{respaldo.Replace("'", "''")}';";
            await vacuum.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var conexionRespaldo = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = respaldo,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await conexionRespaldo.OpenAsync(cancellationToken);
        using var integridad = conexionRespaldo.CreateCommand();
        integridad.CommandText = "PRAGMA integrity_check;";
        var resultado = await integridad.ExecuteScalarAsync(cancellationToken) as string;
        if (!string.Equals(resultado, "ok", StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(respaldo);
            throw new InvalidOperationException("El respaldo previo no superó integrity_check. Migración abortada.");
        }
    }
}
