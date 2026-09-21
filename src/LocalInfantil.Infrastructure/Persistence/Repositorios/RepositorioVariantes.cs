using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioVariantes : IRepositorioVariantes
{
    private readonly IGeneradorIds _ids;

    public RepositorioVariantes(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> InsertarAsync(ISesionPersistencia sesion, NuevaVariante datos, string creadoEn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
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

            return Leer(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<VarianteRegistrada?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, producto_id, sku, talle, color, activa FROM variante WHERE id = $id;";
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

    public async Task ActualizarAsync(ISesionPersistencia sesion, string id, EdicionVariante edicion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edicion);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE variante SET talle = $talle, color = $color WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$talle", (object?)edicion.Talle ?? DBNull.Value);
            comando.Parameters.AddWithValue("$color", (object?)edicion.Color ?? DBNull.Value);
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
            comando.CommandText = "UPDATE variante SET activa = 0 WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<VarianteRegistrada>> ListarPorProductoAsync(ISesionPersistencia sesion, string productoId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, producto_id, sku, talle, color, activa FROM variante WHERE producto_id = $producto ORDER BY sku;";
            comando.Parameters.AddWithValue("$producto", productoId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<VarianteRegistrada>();
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

    public async Task<long> ContarActivasPorProductoAsync(ISesionPersistencia sesion, string productoId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT COUNT(*) FROM variante WHERE producto_id = $producto AND activa = 1;";
            comando.Parameters.AddWithValue("$producto", productoId);
            return Convert.ToInt64(await comando.ExecuteScalarAsync(cancellationToken));
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<FichaVariante>> ObtenerFichaPorProductoAsync(ISesionPersistencia sesion, string productoId, string fecha, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT v.id, v.sku, v.talle, v.color, v.activa,
                    (SELECT p.importe_minor FROM precio p WHERE p.variante_id = v.id AND p.vigente_desde <= $fecha AND (p.vigente_hasta IS NULL OR $fecha < p.vigente_hasta) ORDER BY p.vigente_desde DESC LIMIT 1),
                    (SELECT p.moneda FROM precio p WHERE p.variante_id = v.id AND p.vigente_desde <= $fecha AND (p.vigente_hasta IS NULL OR $fecha < p.vigente_hasta) ORDER BY p.vigente_desde DESC LIMIT 1),
                    COALESCE((SELECT SUM(m.cantidad) FROM movimiento_inventario m WHERE m.variante_id = v.id), 0)
                FROM variante v WHERE v.producto_id = $producto ORDER BY v.sku;
                """;
            comando.Parameters.AddWithValue("$producto", productoId);
            comando.Parameters.AddWithValue("$fecha", fecha);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<FichaVariante>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new FichaVariante(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.IsDBNull(2) ? null : lector.GetString(2),
                    lector.IsDBNull(3) ? null : lector.GetString(3),
                    lector.GetInt64(4) == 1,
                    lector.IsDBNull(5) ? null : lector.GetInt64(5),
                    lector.IsDBNull(6) ? null : lector.GetString(6),
                    lector.GetInt64(7)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    private static VarianteRegistrada Leer(SqliteDataReader lector)
    {
        return new VarianteRegistrada(
            lector.GetString(0),
            lector.GetString(1),
            lector.GetString(2),
            lector.IsDBNull(3) ? null : lector.GetString(3),
            lector.IsDBNull(4) ? null : lector.GetString(4),
            lector.GetInt64(5) == 1);
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
