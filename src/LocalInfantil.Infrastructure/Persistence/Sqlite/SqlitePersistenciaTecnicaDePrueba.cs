using LocalInfantil.Application.Abstractions;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Implementación SQLite de la comprobación técnica. Cada operación crea y libera su conexión.
/// </summary>
public sealed class SqlitePersistenciaTecnicaDePrueba : IPersistenciaTecnicaDePrueba
{
    private readonly string _cadenaConexion;
    private readonly string _rutaBaseDatos;

    public SqlitePersistenciaTecnicaDePrueba(string rutaBaseDatos)
    {
        if (string.IsNullOrWhiteSpace(rutaBaseDatos))
        {
            throw new ArgumentException("La ruta de la base de datos es obligatoria.", nameof(rutaBaseDatos));
        }

        _rutaBaseDatos = Path.GetFullPath(rutaBaseDatos);
        _cadenaConexion = new SqliteConnectionStringBuilder
        {
            DataSource = _rutaBaseDatos,
            Pooling = false
        }.ToString();
    }

    public async Task InicializarAsync(CancellationToken cancellationToken = default)
    {
        var directorio = Path.GetDirectoryName(_rutaBaseDatos)
            ?? throw new InvalidOperationException("No se pudo determinar el directorio de la base de datos.");
        Directory.CreateDirectory(directorio);

        await using var conexion = new SqliteConnection(_cadenaConexion);
        await conexion.OpenAsync(cancellationToken);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = """
            CREATE TABLE IF NOT EXISTS technical_persistence_probe (
                probe_key TEXT PRIMARY KEY NOT NULL,
                probe_value TEXT NOT NULL
            );
            """;
        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task GuardarAsync(string clave, string valor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clave);
        ArgumentNullException.ThrowIfNull(valor);

        await using var conexion = new SqliteConnection(_cadenaConexion);
        await conexion.OpenAsync(cancellationToken);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = """
            INSERT INTO technical_persistence_probe (probe_key, probe_value)
            VALUES ($clave, $valor)
            ON CONFLICT(probe_key) DO UPDATE SET probe_value = excluded.probe_value;
            """;
        comando.Parameters.AddWithValue("$clave", clave);
        comando.Parameters.AddWithValue("$valor", valor);
        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> LeerAsync(string clave, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clave);

        await using var conexion = new SqliteConnection(_cadenaConexion);
        await conexion.OpenAsync(cancellationToken);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT probe_value FROM technical_persistence_probe WHERE probe_key = $clave;";
        comando.Parameters.AddWithValue("$clave", clave);

        var valor = await comando.ExecuteScalarAsync(cancellationToken);
        return valor as string;
    }
}
