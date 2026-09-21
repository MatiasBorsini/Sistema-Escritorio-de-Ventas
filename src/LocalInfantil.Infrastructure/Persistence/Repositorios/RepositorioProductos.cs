using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioProductos : IRepositorioProductos
{
    private readonly IGeneradorIds _ids;

    public RepositorioProductos(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> InsertarAsync(ISesionPersistencia sesion, NuevoProducto datos, string creadoEn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO producto (id, nombre, marca, categoria, creado_en) VALUES ($id, $nombre, $marca, $categoria, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$nombre", datos.Nombre);
            comando.Parameters.AddWithValue("$marca", (object?)datos.Marca ?? DBNull.Value);
            comando.Parameters.AddWithValue("$categoria", (object?)datos.Categoria ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creadoEn", creadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<ProductoRegistrado?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, nombre, marca, categoria, activo FROM producto WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new ProductoRegistrado(
                lector.GetString(0),
                lector.GetString(1),
                lector.IsDBNull(2) ? null : lector.GetString(2),
                lector.IsDBNull(3) ? null : lector.GetString(3),
                lector.GetInt64(4) == 1);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
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
