using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioRecepciones : IRepositorioRecepciones
{
    private readonly IGeneradorIds _ids;

    public RepositorioRecepciones(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> InsertarAsync(
        ISesionPersistencia sesion,
        string proveedorId,
        string operacionId,
        string? comprobanteRef,
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
            comando.CommandText = "INSERT INTO recepcion (id, proveedor_id, operacion_id, comprobante_ref, creado_por_usuario_id, creado_en) VALUES ($id, $proveedor, $operacion, $comprobante, $usuario, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$proveedor", proveedorId);
            comando.Parameters.AddWithValue("$operacion", operacionId);
            comando.Parameters.AddWithValue("$comprobante", (object?)comprobanteRef ?? DBNull.Value);
            comando.Parameters.AddWithValue("$usuario", (object?)creadoPorUsuarioId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creadoEn", creadoEn);
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
        string recepcionId,
        string varianteId,
        long cantidad,
        long? costoUnitarioMinor,
        string? moneda,
        CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO recepcion_linea (id, recepcion_id, variante_id, cantidad, costo_unitario_minor, moneda) VALUES ($id, $recepcion, $variante, $cantidad, $costo, $moneda);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$recepcion", recepcionId);
            comando.Parameters.AddWithValue("$variante", varianteId);
            comando.Parameters.AddWithValue("$cantidad", cantidad);
            comando.Parameters.AddWithValue("$costo", (object?)costoUnitarioMinor ?? DBNull.Value);
            comando.Parameters.AddWithValue("$moneda", (object?)moneda ?? DBNull.Value);
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
            comando.CommandText = "SELECT 1 FROM recepcion WHERE operacion_id = $operacion LIMIT 1;";
            comando.Parameters.AddWithValue("$operacion", operacionId);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<RecepcionRegistrada?> ObtenerPorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, proveedor_id, operacion_id, comprobante_ref, creado_por_usuario_id, creado_en FROM recepcion WHERE operacion_id = $operacion LIMIT 1;";
            comando.Parameters.AddWithValue("$operacion", operacionId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken)) return null;
            return new RecepcionRegistrada(
                lector.GetString(0), lector.GetString(1), lector.GetString(2),
                lector.IsDBNull(3) ? null : lector.GetString(3),
                lector.IsDBNull(4) ? null : lector.GetString(4), lector.GetString(5));
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<bool> ExisteReversionAsync(ISesionPersistencia sesion, string recepcionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT 1 FROM reversion_recepcion WHERE recepcion_id = $recepcion LIMIT 1;";
            comando.Parameters.AddWithValue("$recepcion", recepcionId);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task InsertarReversionAsync(
        ISesionPersistencia sesion,
        string recepcionId,
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
            comando.CommandText = "INSERT INTO reversion_recepcion (id, recepcion_id, operacion_id, motivo, creado_por_usuario_id, creado_en) VALUES ($id, $recepcion, $operacion, $motivo, $usuario, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$recepcion", recepcionId);
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

    public async Task<IReadOnlyList<LineaRecepcion>> ListarLineasAsync(ISesionPersistencia sesion, string recepcionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT variante_id, cantidad, costo_unitario_minor, moneda FROM recepcion_linea WHERE recepcion_id = $recepcion ORDER BY variante_id;";
            comando.Parameters.AddWithValue("$recepcion", recepcionId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<LineaRecepcion>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new LineaRecepcion(
                    lector.GetString(0),
                    lector.GetInt64(1),
                    lector.IsDBNull(2) ? null : lector.GetInt64(2),
                    lector.IsDBNull(3) ? null : lector.GetString(3)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<RecepcionRegistrada?> ObtenerAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, proveedor_id, operacion_id, comprobante_ref, creado_por_usuario_id, creado_en FROM recepcion WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken)) return null;
            return new RecepcionRegistrada(
                lector.GetString(0),
                lector.GetString(1),
                lector.GetString(2),
                lector.IsDBNull(3) ? null : lector.GetString(3),
                lector.IsDBNull(4) ? null : lector.GetString(4),
                lector.GetString(5));
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<RecepcionRegistrada>> ListarAsync(ISesionPersistencia sesion, int limite, int desplazamiento, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, proveedor_id, operacion_id, comprobante_ref, creado_por_usuario_id, creado_en FROM recepcion ORDER BY creado_en DESC, id DESC LIMIT $limite OFFSET $desplazamiento;";
            comando.Parameters.AddWithValue("$limite", limite);
            comando.Parameters.AddWithValue("$desplazamiento", desplazamiento);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var lista = new List<RecepcionRegistrada>();
            while (await lector.ReadAsync(cancellationToken))
            {
                lista.Add(new RecepcionRegistrada(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.IsDBNull(3) ? null : lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4),
                    lector.GetString(5)));
            }
            return lista;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<ResumenRecepcion>> BuscarResumenAsync(
        ISesionPersistencia sesion,
        string texto,
        string filtro,
        int limite,
        int desplazamiento,
        CancellationToken cancellationToken = default,
        DateTimeOffset? desdeInclusiveUtc = null,
        DateTimeOffset? hastaExclusiveUtc = null)
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

        if (filtro != FiltroRecepciones.Todas && filtro != FiltroRecepciones.Confirmadas && filtro != FiltroRecepciones.Revertidas)
        {
            throw new ArgumentException("Filtro desconocido.", nameof(filtro));
        }

        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT id, proveedor_id, proveedor_nombre, operacion_id, comprobante_ref,
                       creado_por_usuario_id, creado_por_nombre, creado_en,
                       cantidad_lineas, unidades, lineas_sin_costo, total_minor, revertida
                FROM (
                    SELECT r.id AS id, r.proveedor_id AS proveedor_id, p.nombre AS proveedor_nombre,
                           r.operacion_id AS operacion_id, r.comprobante_ref AS comprobante_ref,
                           r.creado_por_usuario_id AS creado_por_usuario_id, u.nombre AS creado_por_nombre,
                           r.creado_en AS creado_en,
                           (SELECT COUNT(*) FROM recepcion_linea l WHERE l.recepcion_id = r.id) AS cantidad_lineas,
                           (SELECT COALESCE(SUM(l.cantidad), 0) FROM recepcion_linea l WHERE l.recepcion_id = r.id) AS unidades,
                           (SELECT COUNT(*) FROM recepcion_linea l WHERE l.recepcion_id = r.id AND l.costo_unitario_minor IS NULL) AS lineas_sin_costo,
                           (SELECT COALESCE(SUM(l.cantidad * l.costo_unitario_minor), 0) FROM recepcion_linea l WHERE l.recepcion_id = r.id AND l.costo_unitario_minor IS NOT NULL) AS total_minor,
                           EXISTS(SELECT 1 FROM reversion_recepcion rr WHERE rr.recepcion_id = r.id) AS revertida
                    FROM recepcion r JOIN proveedor p ON p.id = r.proveedor_id
                                     LEFT JOIN usuario u ON u.id = r.creado_por_usuario_id
                    WHERE (r.comprobante_ref LIKE $t OR p.nombre LIKE $t)
                      AND ($desde IS NULL OR julianday(r.creado_en) >= julianday($desde))
                      AND ($hasta IS NULL OR julianday(r.creado_en) < julianday($hasta))
                )
                WHERE $f = 'todas' OR ($f = 'revertidas' AND revertida = 1) OR ($f = 'confirmadas' AND revertida = 0)
                ORDER BY julianday(creado_en) DESC, id DESC LIMIT $limite OFFSET $desplazamiento;
                """;
            comando.Parameters.AddWithValue("$t", $"%{texto.Trim()}%");
            comando.Parameters.AddWithValue("$f", filtro);
            object parametroDesde = desdeInclusiveUtc is { } desde
                ? desde.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                : DBNull.Value;
            object parametroHasta = hastaExclusiveUtc is { } hasta
                ? hasta.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                : DBNull.Value;
            comando.Parameters.AddWithValue("$desde", parametroDesde);
            comando.Parameters.AddWithValue("$hasta", parametroHasta);
            comando.Parameters.AddWithValue("$limite", limite + 1);
            comando.Parameters.AddWithValue("$desplazamiento", desplazamiento);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var lista = new List<ResumenRecepcion>();
            while (await lector.ReadAsync(cancellationToken))
            {
                lista.Add(new ResumenRecepcion(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5),
                    lector.IsDBNull(6) ? null : lector.GetString(6),
                    lector.GetString(7),
                    (int)lector.GetInt64(8),
                    lector.GetInt64(9),
                    (int)lector.GetInt64(10),
                    lector.GetInt64(11),
                    lector.GetInt64(12) == 1));
            }

            return lista;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<LineaRecepcionDetalle>> ListarLineasDetalleAsync(ISesionPersistencia sesion, string recepcionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT v.sku, v.talle, v.color, l.cantidad, l.costo_unitario_minor, l.moneda
                FROM recepcion_linea l JOIN variante v ON v.id = l.variante_id
                WHERE l.recepcion_id = $recepcion ORDER BY v.sku;
                """;
            comando.Parameters.AddWithValue("$recepcion", recepcionId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<LineaRecepcionDetalle>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new LineaRecepcionDetalle(
                    lector.GetString(0),
                    lector.IsDBNull(1) ? null : lector.GetString(1),
                    lector.IsDBNull(2) ? null : lector.GetString(2),
                    lector.GetInt64(3),
                    lector.IsDBNull(4) ? null : lector.GetInt64(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<bool> ExisteReversionPorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT 1 FROM reversion_recepcion WHERE operacion_id = $op LIMIT 1;";
            comando.Parameters.AddWithValue("$op", operacionId);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
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
