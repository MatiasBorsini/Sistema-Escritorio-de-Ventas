using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioCategorias : IRepositorioCategorias
{
    private readonly IGeneradorIds _ids;

    public RepositorioCategorias(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> InsertarAsync(ISesionPersistencia sesion, NuevaCategoria datos, string creadoEn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO categoria (id, nombre, prefijo, activa, orden, creado_en) VALUES ($id, $nombre, $prefijo, 1, $orden, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$nombre", datos.Nombre);
            comando.Parameters.AddWithValue("$prefijo", datos.Prefijo);
            comando.Parameters.AddWithValue("$orden", datos.Orden);
            comando.Parameters.AddWithValue("$creadoEn", creadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<CategoriaRegistrada?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, nombre, prefijo, orden, activa FROM categoria WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return Leer(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<CategoriaRegistrada?> ObtenerPorNombreAsync(ISesionPersistencia sesion, string nombre, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, nombre, prefijo, orden, activa FROM categoria WHERE nombre = $nombre;";
            comando.Parameters.AddWithValue("$nombre", nombre);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return Leer(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task ActualizarAsync(ISesionPersistencia sesion, string id, NuevaCategoria datos, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        if (string.IsNullOrWhiteSpace(datos.Nombre))
        {
            throw new ArgumentException("El nombre es obligatorio.", nameof(datos));
        }

        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE categoria SET nombre = $nombre, prefijo = $prefijo, orden = $orden WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$nombre", datos.Nombre);
            comando.Parameters.AddWithValue("$prefijo", datos.Prefijo);
            comando.Parameters.AddWithValue("$orden", datos.Orden);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task DesactivarAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE categoria SET activa = 0 WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<CategoriaRegistrada>> ListarAsync(ISesionPersistencia sesion, bool soloActivas, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = soloActivas
                ? "SELECT id, nombre, prefijo, orden, activa FROM categoria WHERE activa = 1 ORDER BY orden, nombre;"
                : "SELECT id, nombre, prefijo, orden, activa FROM categoria ORDER BY orden, nombre;";
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<CategoriaRegistrada>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(Leer(lector));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    private static CategoriaRegistrada Leer(SqliteDataReader lector)
    {
        return new CategoriaRegistrada(
            lector.GetString(0),
            lector.GetString(1),
            lector.GetString(2),
            lector.GetInt32(3),
            lector.GetInt64(4) == 1);
    }

    private static ISesionSqlite Sesion(ISesionPersistencia sesion)
    {
        if (sesion is not ISesionSqlite s)
        {
            throw new ArgumentException("La sesión no pertenece a este proveedor SQLite.", nameof(sesion));
        }

        return s;
    }

    private static void ExigirEscritura(ISesionSqlite sesion)
    {
        if (sesion.SoloLectura)
        {
            throw new InvalidOperationException("La sesión es de solo lectura y no admite escrituras.");
        }
    }
}