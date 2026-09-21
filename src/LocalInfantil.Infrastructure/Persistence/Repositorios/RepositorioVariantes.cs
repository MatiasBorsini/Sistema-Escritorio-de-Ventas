using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioVariantes : IRepositorioVariantes
{
    public async Task<string> InsertarAsync(ISesionPersistencia sesion, NuevaVariante datos, string creadoEn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = Guid.NewGuid().ToString("N");
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO variante (id, producto_id, sku, talle, color, creado_en) VALUES ($id, $producto, $sku, $talle, $color, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$producto", datos.ProductoId);
            comando.Parameters.AddWithValue("$sku", datos.Sku);
            comando.Parameters.AddWithValue("$talle", (object?)datos.Talle ?? DBNull.Value);
            comando.Parameters.AddWithValue("$color", (object?)datos.Color ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creadoEn", creadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<VarianteRegistrada?> ObtenerPorSkuAsync(ISesionPersistencia sesion, string sku, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, producto_id, sku, talle, color, activa FROM variante WHERE sku = $sku;";
            comando.Parameters.AddWithValue("$sku", sku);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new VarianteRegistrada(
                lector.GetString(0),
                lector.GetString(1),
                lector.GetString(2),
                lector.IsDBNull(3) ? null : lector.GetString(3),
                lector.IsDBNull(4) ? null : lector.GetString(4),
                lector.GetInt64(5) == 1);
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
