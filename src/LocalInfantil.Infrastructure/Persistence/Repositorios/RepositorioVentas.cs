using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioVentas : IRepositorioVentas
{
    private readonly IGeneradorIds _ids;

    public RepositorioVentas(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> InsertarAsync(
        ISesionPersistencia sesion,
        string numero,
        string operacionId,
        long totalMinor,
        string moneda,
        string? creadoPorUsuarioId,
        string creadoEn,
        string confirmadaEn,
        CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO venta (id, numero, operacion_id, estado, total_minor, moneda, creado_por_usuario_id, creada_en, confirmada_en) VALUES ($id, $numero, $operacion, 'confirmada', $total, $moneda, $usuario, $creadoEn, $confirmadaEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$numero", numero);
            comando.Parameters.AddWithValue("$operacion", operacionId);
            comando.Parameters.AddWithValue("$total", totalMinor);
            comando.Parameters.AddWithValue("$moneda", moneda);
            comando.Parameters.AddWithValue("$usuario", (object?)creadoPorUsuarioId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creadoEn", creadoEn);
            comando.Parameters.AddWithValue("$confirmadaEn", confirmadaEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task InsertarLineaAsync(
        ISesionPersistencia sesion,
        string ventaId,
        string varianteId,
        string codigoInterno,
        string nombreProducto,
        string? talle,
        string? color,
        long cantidad,
        long precioUnitarioMinor,
        CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO venta_linea (id, venta_id, variante_id, cantidad, precio_unitario_minor, subtotal_minor, codigo_interno, nombre_producto, talle, color) VALUES ($id, $venta, $variante, $cantidad, $precio, $cantidad * $precio, $codigo, $nombre, $talle, $color);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$venta", ventaId);
            comando.Parameters.AddWithValue("$variante", varianteId);
            comando.Parameters.AddWithValue("$cantidad", cantidad);
            comando.Parameters.AddWithValue("$precio", precioUnitarioMinor);
            comando.Parameters.AddWithValue("$codigo", codigoInterno);
            comando.Parameters.AddWithValue("$nombre", nombreProducto);
            comando.Parameters.AddWithValue("$talle", (object?)talle ?? DBNull.Value);
            comando.Parameters.AddWithValue("$color", (object?)color ?? DBNull.Value);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<bool> ExistePorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT 1 FROM venta WHERE operacion_id = $operacion LIMIT 1;";
            comando.Parameters.AddWithValue("$operacion", operacionId);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<VentaRegistrada?> ObtenerPorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT v.id, v.numero, v.operacion_id, v.estado, v.total_minor, v.moneda, v.referencia_fiscal, v.creada_en, v.confirmada_en, u.nombre
                FROM venta v LEFT JOIN usuario u ON u.id = v.creado_por_usuario_id
                WHERE v.operacion_id = $operacion;
                """;
            comando.Parameters.AddWithValue("$operacion", operacionId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return LeerVenta(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<VentaRegistrada?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT v.id, v.numero, v.operacion_id, v.estado, v.total_minor, v.moneda, v.referencia_fiscal, v.creada_en, v.confirmada_en, u.nombre
                FROM venta v LEFT JOIN usuario u ON u.id = v.creado_por_usuario_id
                WHERE v.id = $id;
                """;
            comando.Parameters.AddWithValue("$id", id);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return LeerVenta(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<VentaRegistrada?> ObtenerPorNumeroAsync(ISesionPersistencia sesion, string numero, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(numero))
        {
            throw new ArgumentException("El número es obligatorio.", nameof(numero));
        }

        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT v.id, v.numero, v.operacion_id, v.estado, v.total_minor, v.moneda, v.referencia_fiscal, v.creada_en, v.confirmada_en, u.nombre
                FROM venta v LEFT JOIN usuario u ON u.id = v.creado_por_usuario_id
                WHERE v.numero = $numero;
                """;
            comando.Parameters.AddWithValue("$numero", numero.Trim());
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return LeerVenta(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task MarcarAnuladaAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE venta SET estado = 'anulada' WHERE id = $id AND estado = 'confirmada';";
            comando.Parameters.AddWithValue("$id", id);
            var filas = await comando.ExecuteNonQueryAsync(cancellationToken);
            if (filas == 0)
            {
                throw new InvalidOperationException("La venta no está confirmada.");
            }
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<LineaVentaRegistrada>> ListarLineasAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, variante_id, codigo_interno, nombre_producto, talle, color, cantidad, precio_unitario_minor, subtotal_minor FROM venta_linea WHERE venta_id = $venta ORDER BY codigo_interno;";
            comando.Parameters.AddWithValue("$venta", ventaId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<LineaVentaRegistrada>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new LineaVentaRegistrada(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5),
                    lector.GetInt64(6),
                    lector.GetInt64(7),
                    lector.GetInt64(8)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<ResumenVenta>> BuscarResumenAsync(ISesionPersistencia sesion, string texto, string estado, int limite, int desplazamiento, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        if (limite < 1 || limite > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limite), "El límite debe estar entre 1 y 100.");
        }

        if (desplazamiento < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(desplazamiento), "El desplazamiento no puede ser negativo.");
        }

        var t = texto.Trim();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT v.id, v.numero, v.creada_en, v.total_minor, v.estado, u.nombre,
                       (r.venta_id IS NOT NULL),
                       (SELECT COUNT(*) FROM venta_linea l WHERE l.venta_id = v.id)
                FROM venta v LEFT JOIN usuario u ON u.id = v.creado_por_usuario_id
                             LEFT JOIN reversion_venta r ON r.venta_id = v.id
                WHERE ($t = '' OR v.numero LIKE $contiene)
                  AND ($estado = 'todas' OR ($estado = 'anuladas' AND r.venta_id IS NOT NULL) OR ($estado = 'confirmadas' AND r.venta_id IS NULL))
                ORDER BY v.creada_en DESC, v.numero DESC LIMIT $limite OFFSET $desplazamiento;
                """;
            comando.Parameters.AddWithValue("$t", t);
            comando.Parameters.AddWithValue("$contiene", $"%{t}%");
            comando.Parameters.AddWithValue("$estado", estado);
            comando.Parameters.AddWithValue("$limite", limite + 1);
            comando.Parameters.AddWithValue("$desplazamiento", desplazamiento);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<ResumenVenta>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new ResumenVenta(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.GetInt64(3),
                    lector.GetString(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5),
                    lector.GetInt64(6) == 1,
                    (int)lector.GetInt64(7)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<int> ObtenerSiguienteNumeroAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        // Sin transacción anidada: corre dentro del BEGIN IMMEDIATE del alta.
        try
        {
            int numero;
            using (var cmd = s.Conexion.CreateCommand())
            {
                cmd.CommandTimeout = s.TimeoutSegundos;
                cmd.CommandText = "SELECT ultimo_numero FROM secuencia_venta WHERE id = 'venta';";
                var result = await cmd.ExecuteScalarAsync(cancellationToken);
                numero = result is null || result is DBNull ? 1 : Convert.ToInt32(result) + 1;
            }

            using (var cmd = s.Conexion.CreateCommand())
            {
                cmd.CommandTimeout = s.TimeoutSegundos;
                cmd.CommandText = "INSERT INTO secuencia_venta (id, ultimo_numero) VALUES ('venta', $num) ON CONFLICT(id) DO UPDATE SET ultimo_numero = $num;";
                cmd.Parameters.AddWithValue("$num", numero);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            return numero;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<bool> ExisteReversionAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT 1 FROM reversion_venta WHERE venta_id = $venta LIMIT 1;";
            comando.Parameters.AddWithValue("$venta", ventaId);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task InsertarReversionAsync(
        ISesionPersistencia sesion,
        string ventaId,
        string operacionId,
        string motivo,
        string? creadoPorUsuarioId,
        string creadoEn,
        CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO reversion_venta (id, venta_id, operacion_id, motivo, creado_por_usuario_id, creado_en) VALUES ($id, $venta, $operacion, $motivo, $usuario, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$venta", ventaId);
            comando.Parameters.AddWithValue("$operacion", operacionId);
            comando.Parameters.AddWithValue("$motivo", motivo);
            comando.Parameters.AddWithValue("$usuario", (object?)creadoPorUsuarioId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creadoEn", creadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<ReversionVentaRegistrada?> ObtenerReversionAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT r.id, r.venta_id, r.operacion_id, r.motivo, u.nombre, r.creado_en
                FROM reversion_venta r LEFT JOIN usuario u ON u.id = r.creado_por_usuario_id
                WHERE r.venta_id = $venta;
                """;
            comando.Parameters.AddWithValue("$venta", ventaId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new ReversionVentaRegistrada(
                lector.GetString(0),
                lector.GetString(1),
                lector.GetString(2),
                lector.GetString(3),
                lector.IsDBNull(4) ? null : lector.GetString(4),
                lector.GetString(5));
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    private static VentaRegistrada LeerVenta(SqliteDataReader lector)
    {
        return new VentaRegistrada(
            lector.GetString(0),
            lector.GetString(1),
            lector.GetString(2),
            lector.GetString(3),
            lector.GetInt64(4),
            lector.GetString(5),
            lector.IsDBNull(6) ? null : lector.GetString(6),
            lector.GetString(7),
            lector.IsDBNull(8) ? null : lector.GetString(8),
            lector.IsDBNull(9) ? null : lector.GetString(9));
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
