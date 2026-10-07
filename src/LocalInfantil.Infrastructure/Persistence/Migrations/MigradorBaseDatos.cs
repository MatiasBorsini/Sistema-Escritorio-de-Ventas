using LocalInfantil.Application.Abstractions;
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
    /// <summary>
    /// Tiempo máximo por comando, en segundos. Las migraciones corren con la
    /// aplicación detenida y VACUUM INTO sobre bases en crecimiento necesita
    /// holgura; finito para no colgar jamás ante un bloqueo ajeno.
    /// </summary>
    private const int TimeoutSegundos = 60;

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

        using (var pragma = CrearComando(conexion, "PRAGMA foreign_keys = ON;"))
        {
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

            await RespaldarYVerificarAsync(aplicadas, directorioRespaldos, cancellationToken);
        }

        // Primera escritura admitida: recién después de validar el historial
        // y de generar el respaldo. En bases preexistentes esta tabla ya existe.
        using (var bitacora = CrearComando(conexion, """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                nombre TEXT NOT NULL,
                aplicada_en TEXT NOT NULL
            );
            """))
        {
            await bitacora.ExecuteNonQueryAsync(cancellationToken);
        }

        var resultado = new List<int>();
        foreach (var migracion in pendientes)
        {
            if (migracion.Sql.Contains("-- REQUIERE_FK_OFF", StringComparison.Ordinal))
                await AplicarSinFkAsync(conexion, migracion, cancellationToken);
            else
                await AplicarEnTransaccionAsync(conexion, migracion, cancellationToken);
            resultado.Add(migracion.Version);
        }

        return resultado;
    }

    /// <summary>
    /// Ejecuta statement por statement (respetando cuerpos BEGIN...END de
    /// triggers): un batch multi-statement puede detenerse silenciosamente
    /// ante un statement fallido intermedio sin propagar el error,
    /// registrando la versión sin haber aplicado todo (V13 sucia + V14).
    /// Así cada statement corre y cualquier error aborta con rollback.
    /// </summary>
    private static async Task EjecutarPorPartesAsync(SqliteConnection conexion, SqliteTransaction transaccion, string sql, CancellationToken cancellationToken)
    {
        foreach (var sentencia in DividirSentencias(sql))
        {
            using var comando = CrearComando(conexion, sentencia);
            comando.Transaction = transaccion;
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static List<string> DividirSentencias(string sql)
    {
        var resultado = new List<string>();
        var actual = new System.Text.StringBuilder();
        var enTrigger = false;
        foreach (var linea in sql.Split('\n'))
        {
            actual.AppendLine(linea);
            var recortada = linea.TrimStart();
            if (!enTrigger && recortada.StartsWith("CREATE TRIGGER", StringComparison.OrdinalIgnoreCase))
            {
                var unaLinea = linea.Trim();
                if (unaLinea.Contains("BEGIN", StringComparison.OrdinalIgnoreCase)
                    && unaLinea.EndsWith("END;", StringComparison.OrdinalIgnoreCase))
                {
                    resultado.Add(actual.ToString());
                    actual.Clear();
                    continue;
                }
                enTrigger = true;
            }
            if (enTrigger && linea.Trim().Equals("END;", StringComparison.OrdinalIgnoreCase))
            {
                resultado.Add(actual.ToString());
                actual.Clear();
                enTrigger = false;
            }
            else if (!enTrigger && linea.TrimEnd().EndsWith(';'))
            {
                resultado.Add(actual.ToString());
                actual.Clear();
            }
        }
        var resto = actual.ToString().Trim();
        if (!string.IsNullOrEmpty(resto))
            resultado.Add(resto);
        return resultado;
    }

    private static async Task AplicarEnTransaccionAsync(SqliteConnection conexion, Migracion migracion, CancellationToken cancellationToken)
    {
        using var transaccion = conexion.BeginTransaction();
        try
        {
            await EjecutarPorPartesAsync(conexion, transaccion, migracion.Sql, cancellationToken);
            await RegistrarVersionAsync(conexion, transaccion, migracion, cancellationToken);
            transaccion.Commit();
        }
        catch
        {
            transaccion.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Rebuilds que tocan tablas padre con hijas con datos, en dos fases:
    /// las FKs del esquema son NOT DEFERRABLE, así que DROP TABLE del padre
    /// falla con enforcement inmediato. Fase 1 (FK ON, verificada): DROPs de
    /// triggers, CREATEs temporales (con NOMBRES FINALES en todas sus FKs,
    /// que resuelven contra las tablas originales con mismos IDs) y copia
    /// 1:1. Fase 2 (FK OFF, fuera de transacción el cambio de pragma):
    /// DROPs de viejas, RENAMEs (no reescriben nada porque todo ya usa
    /// nombres finales), índices, triggers y tablas nuevas. Se verifica
    /// integridad antes y después. El marcador "-- FASE_FK_OFF" divide ambas
    /// partes dentro del mismo SQL versionado.
    /// </summary>
    private static async Task AplicarSinFkAsync(SqliteConnection conexion, Migracion migracion, CancellationToken cancellationToken)
    {
        if (await ContarViolacionesFkAsync(conexion, null, cancellationToken) > 0)
            throw new InvalidOperationException("La base tiene violaciones de integridad previas. No se puede migrar.");

        var partes = migracion.Sql.Split(new[] { "-- FASE_FK_OFF" }, StringSplitOptions.None);
        if (partes.Length != 2)
            throw new InvalidOperationException("La migración con FK OFF debe tener exactamente dos fases.");

        using (var fase1 = conexion.BeginTransaction())
        {
            try
            {
                await EjecutarPorPartesAsync(conexion, fase1, partes[0], cancellationToken);
                fase1.Commit();
            }
            catch
            {
                fase1.Rollback();
                throw;
            }
        }

        using (var off = CrearComando(conexion, "PRAGMA foreign_keys = OFF;"))
            await off.ExecuteNonQueryAsync(cancellationToken);
        try
        {
            using var fase2 = conexion.BeginTransaction();
            try
            {
                await EjecutarPorPartesAsync(conexion, fase2, partes[1], cancellationToken);
                await RegistrarVersionAsync(conexion, fase2, migracion, cancellationToken);
                fase2.Commit();
            }
            catch
            {
                fase2.Rollback();
                throw;
            }
        }
        finally
        {
            using (var on = CrearComando(conexion, "PRAGMA foreign_keys = ON;"))
                await on.ExecuteNonQueryAsync(cancellationToken);
        }

        if (await ContarViolacionesFkAsync(conexion, null, cancellationToken) > 0)
            throw new InvalidOperationException("La migración dejó violaciones de integridad. Restaurá el respaldo previo.");
    }

    private static async Task<long> ContarViolacionesFkAsync(SqliteConnection conexion, SqliteTransaction? transaccion, CancellationToken cancellationToken)
    {
        using var comando = CrearComando(conexion, "PRAGMA foreign_key_check;");
        if (transaccion is not null) comando.Transaction = transaccion;
        var filas = 0L;
        await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
        while (await lector.ReadAsync(cancellationToken)) filas++;
        return filas;
    }

    private static async Task RegistrarVersionAsync(SqliteConnection conexion, SqliteTransaction transaccion, Migracion migracion, CancellationToken cancellationToken)
    {
        using var registro = CrearComando(conexion, "INSERT INTO schema_migrations (version, nombre, aplicada_en) VALUES ($version, $nombre, $aplicadaEn);");
        registro.Transaction = transaccion;
        registro.Parameters.AddWithValue("$version", migracion.Version);
        registro.Parameters.AddWithValue("$nombre", migracion.Nombre);
        registro.Parameters.AddWithValue("$aplicadaEn", DateTime.UtcNow.ToString("o"));
        await registro.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand CrearComando(SqliteConnection conexion, string sql)
    {
        var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        comando.CommandTimeout = TimeoutSegundos;
        return comando;
    }

    private static async Task<bool> ExisteTablaAsync(
        SqliteConnection conexion,
        string tabla,
        CancellationToken cancellationToken)
    {
        using var comando = CrearComando(conexion, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tabla;");
        comando.Parameters.AddWithValue("$tabla", tabla);
        return Convert.ToInt64(await comando.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private async Task RespaldarYVerificarAsync(
        HashSet<int> aplicadas,
        string directorioRespaldos,
        CancellationToken cancellationToken)
    {
        var versionActual = aplicadas.Count == 0 ? 0 : aplicadas.Max();
        var servicio = new Respaldos.ServicioRespaldos(new Tiempo.RelojSistema());
        try
        {
            await servicio.CrearRespaldoAsync(_rutaBaseDatos, directorioRespaldos, null, $"respaldo-previo-v{versionActual}", cancellationToken);
        }
        catch (RespaldoInvalidoException ex)
        {
            throw new InvalidOperationException("El respaldo previo no superó la verificación. Migración abortada.", ex);
        }
    }
}
