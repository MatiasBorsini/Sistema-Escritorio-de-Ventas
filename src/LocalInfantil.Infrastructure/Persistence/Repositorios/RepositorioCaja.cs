using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioCaja : IRepositorioCaja
{
    private readonly IGeneradorIds _ids;

    public RepositorioCaja(IGeneradorIds ids) => _ids = ids ?? throw new ArgumentNullException(nameof(ids));

    public async Task<SesionCajaRegistrada?> ObtenerAbiertaAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        using var cmd = CrearComando(s, $"{SelectSesion} WHERE c.estado = 'abierta' LIMIT 1;");
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? LeerSesion(r) : null;
    }

    public async Task<SesionCajaRegistrada?> ObtenerSesionAsync(ISesionPersistencia sesion, string sesionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        using var cmd = CrearComando(s, $"{SelectSesion} WHERE c.id = $id;");
        cmd.Parameters.AddWithValue("$id", sesionId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? LeerSesion(r) : null;
    }

    public async Task<string> AbrirSesionAsync(ISesionPersistencia sesion, string usuarioId, long fondoInicialMinor, string operacionId, string abiertaEn, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var cmd = CrearComando(s, "INSERT INTO caja_sesion (id, usuario_id, estado, fondo_inicial_minor, moneda, abierta_en, operacion_apertura_id) VALUES ($id, $usuario, 'abierta', $fondo, 'ARS', $fecha, $operacion);");
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$usuario", usuarioId);
            cmd.Parameters.AddWithValue("$fondo", fondoInicialMinor);
            cmd.Parameters.AddWithValue("$fecha", abiertaEn);
            cmd.Parameters.AddWithValue("$operacion", operacionId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex)) { throw ErroresSqlite.OcupadoException(ex); }
    }

    public async Task<string> InsertarOperacionAsync(ISesionPersistencia sesion, NuevaOperacionCaja operacion, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var cmd = CrearComando(s, "INSERT INTO caja_operacion (id, operacion_id, sesion_id, tipo, usuario_id, creado_en) VALUES ($id, $op, $sesion, $tipo, $usuario, $fecha);");
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$op", operacion.OperacionId);
            cmd.Parameters.AddWithValue("$sesion", operacion.SesionId);
            cmd.Parameters.AddWithValue("$tipo", operacion.Tipo);
            cmd.Parameters.AddWithValue("$usuario", operacion.UsuarioId);
            cmd.Parameters.AddWithValue("$fecha", operacion.CreadoEn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex)) { throw ErroresSqlite.OcupadoException(ex); }
    }

    public async Task<string> InsertarMovimientoAsync(ISesionPersistencia sesion, NuevoMovimientoCaja movimiento, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var cmd = CrearComando(s, "INSERT INTO caja_movimiento (id, sesion_id, tipo, importe_minor, venta_id, motivo, creado_en, caja_operacion_id, pago_id, medio, categoria, usuario_id, movimiento_origen_id) VALUES ($id, $sesion, $tipo, $importe, $venta, $motivo, $fecha, $op, $pago, $medio, $categoria, $usuario, $origen);");
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$sesion", movimiento.SesionId);
            cmd.Parameters.AddWithValue("$tipo", movimiento.Tipo);
            cmd.Parameters.AddWithValue("$importe", movimiento.ImporteMinor);
            cmd.Parameters.AddWithValue("$venta", (object?)movimiento.VentaId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$motivo", (object?)movimiento.Motivo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$fecha", movimiento.CreadoEn);
            cmd.Parameters.AddWithValue("$op", (object?)movimiento.CajaOperacionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pago", (object?)movimiento.PagoId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$medio", (object?)movimiento.Medio ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$categoria", (object?)movimiento.Categoria ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$usuario", (object?)movimiento.UsuarioId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$origen", (object?)movimiento.MovimientoOrigenId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex)) { throw ErroresSqlite.OcupadoException(ex); }
    }

    public async Task<bool> ExisteOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        using var cmd = CrearComando(s, "SELECT 1 FROM caja_operacion WHERE operacion_id = $op LIMIT 1;");
        cmd.Parameters.AddWithValue("$op", operacionId);
        return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<long> CalcularEfectivoEsperadoAsync(ISesionPersistencia sesion, string sesionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        using var cmd = CrearComando(s, "SELECT c.fondo_inicial_minor + COALESCE(SUM(CASE WHEN m.medio = 'efectivo' THEN m.importe_minor ELSE 0 END), 0) FROM caja_sesion c LEFT JOIN caja_movimiento m ON m.sesion_id = c.id WHERE c.id = $id GROUP BY c.id;");
        cmd.Parameters.AddWithValue("$id", sesionId);
        var valor = await cmd.ExecuteScalarAsync(cancellationToken);
        if (valor is null || valor is DBNull) throw new InvalidOperationException("La sesión de caja no existe.");
        return Convert.ToInt64(valor);
    }

    public async Task CerrarSesionAsync(ISesionPersistencia sesion, string sesionId, string usuarioId, string operacionId, string cerradaEn, long esperadoMinor, long contadoMinor, long diferenciaMinor, string? observacion, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var cmd = CrearComando(s, "UPDATE caja_sesion SET estado = 'cerrada', cerrado_por_usuario_id = $usuario, cerrada_en = $fecha, operacion_cierre_id = $op, efectivo_esperado_minor = $esperado, efectivo_contado_minor = $contado, diferencia_minor = $diferencia, observacion_cierre = $observacion WHERE id = $id AND estado = 'abierta';");
            cmd.Parameters.AddWithValue("$usuario", usuarioId);
            cmd.Parameters.AddWithValue("$fecha", cerradaEn);
            cmd.Parameters.AddWithValue("$op", operacionId);
            cmd.Parameters.AddWithValue("$esperado", esperadoMinor);
            cmd.Parameters.AddWithValue("$contado", contadoMinor);
            cmd.Parameters.AddWithValue("$diferencia", diferenciaMinor);
            cmd.Parameters.AddWithValue("$observacion", (object?)observacion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", sesionId);
            if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("La caja ya fue cerrada o dejó de estar disponible.");
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex)) { throw ErroresSqlite.OcupadoException(ex); }
    }

    public async Task<IReadOnlyList<MovimientoCajaRegistrado>> ListarMovimientosAsync(ISesionPersistencia sesion, string sesionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        using var cmd = CrearComando(s, """
            SELECT m.id, m.tipo, m.importe_minor, m.venta_id, v.numero, m.pago_id, m.medio, m.categoria,
                   m.motivo, u.nombre, m.movimiento_origen_id, o.operacion_id, m.creado_en
            FROM caja_movimiento m
            LEFT JOIN venta v ON v.id = m.venta_id
            LEFT JOIN usuario u ON u.id = m.usuario_id
            LEFT JOIN caja_operacion o ON o.id = m.caja_operacion_id
            WHERE m.sesion_id = $id
            ORDER BY m.creado_en DESC, m.id DESC;
            """);
        cmd.Parameters.AddWithValue("$id", sesionId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var result = new List<MovimientoCajaRegistrado>();
        while (await r.ReadAsync(cancellationToken)) result.Add(new(
            r.GetString(0), r.GetString(1), r.GetInt64(2), N(r, 3), N(r, 4), N(r, 5), N(r, 6), N(r, 7), N(r, 8), N(r, 9), N(r, 10), N(r, 11), r.GetString(12)));
        return result;
    }

    public async Task<IReadOnlyList<ImportePorMedioCaja>> ResumirMediosAsync(ISesionPersistencia sesion, string sesionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        using var cmd = CrearComando(s, "SELECT medio, COALESCE(SUM(importe_minor), 0) FROM caja_movimiento WHERE sesion_id = $id AND medio IS NOT NULL GROUP BY medio ORDER BY medio;");
        cmd.Parameters.AddWithValue("$id", sesionId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var result = new List<ImportePorMedioCaja>();
        while (await r.ReadAsync(cancellationToken)) result.Add(new(r.GetString(0), r.GetInt64(1)));
        return result;
    }

    public async Task<IReadOnlyList<ImportePorTipoCaja>> ResumirMovimientosAsync(ISesionPersistencia sesion, string sesionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        using var cmd = CrearComando(s, "SELECT tipo, medio, COALESCE(SUM(importe_minor), 0) FROM caja_movimiento WHERE sesion_id = $id GROUP BY tipo, medio ORDER BY tipo, medio;");
        cmd.Parameters.AddWithValue("$id", sesionId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var result = new List<ImportePorTipoCaja>();
        while (await r.ReadAsync(cancellationToken)) result.Add(new(r.GetString(0), N(r, 1), r.GetInt64(2)));
        return result;
    }

    public async Task<IReadOnlyList<SesionCajaRegistrada>> ListarSesionesAsync(ISesionPersistencia sesion, int limite, int desplazamiento, CancellationToken cancellationToken = default)
    {
        if (limite < 1 || limite > 100) throw new ArgumentOutOfRangeException(nameof(limite));
        if (desplazamiento < 0) throw new ArgumentOutOfRangeException(nameof(desplazamiento));
        var s = Sesion(sesion);
        using var cmd = CrearComando(s, $"{SelectSesion} WHERE c.estado = 'cerrada' ORDER BY c.abierta_en DESC, c.id DESC LIMIT $limite OFFSET $offset;");
        cmd.Parameters.AddWithValue("$limite", limite + 1);
        cmd.Parameters.AddWithValue("$offset", desplazamiento);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var result = new List<SesionCajaRegistrada>();
        while (await r.ReadAsync(cancellationToken)) result.Add(LeerSesion(r));
        return result;
    }

    public async Task<PagoCajaActual?> ObtenerPagoActualAsync(ISesionPersistencia sesion, string pagoId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        using var cmd = CrearComando(s, $"{SelectPagoActual} WHERE p.id = $id;");
        cmd.Parameters.AddWithValue("$id", pagoId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? LeerPago(r) : null;
    }

    public async Task<IReadOnlyList<PagoCajaActual>> ListarPagosActualesPorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        using var cmd = CrearComando(s, $"{SelectPagoActual} WHERE p.venta_id = $id ORDER BY p.id;");
        cmd.Parameters.AddWithValue("$id", ventaId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var result = new List<PagoCajaActual>();
        while (await r.ReadAsync(cancellationToken)) result.Add(LeerPago(r));
        return result;
    }

    public async Task<IReadOnlyList<PagoCajaActual>> BuscarPagosCorregiblesAsync(ISesionPersistencia sesion, string filtroNumeroVenta, int limite, CancellationToken cancellationToken = default)
    {
        if (limite is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(limite));
        var s = Sesion(sesion);
        using var cmd = CrearComando(s, """
            SELECT p.id, p.venta_id, v.numero, v.estado, p.medio, p.medio, p.importe_minor, 0, mo.id
            FROM pago p
            JOIN venta v ON v.id = p.venta_id
            JOIN caja_movimiento mo ON mo.pago_id = p.id AND mo.tipo = 'venta'
            WHERE v.estado = 'confirmada'
              AND NOT EXISTS (SELECT 1 FROM pago_correccion pc WHERE pc.pago_id = p.id)
              AND v.numero LIKE $filtro || '%'
            ORDER BY v.creada_en DESC, v.numero DESC, p.id
            LIMIT $limite;
            """);
        cmd.Parameters.AddWithValue("$filtro", filtroNumeroVenta.Trim());
        cmd.Parameters.AddWithValue("$limite", limite);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var result = new List<PagoCajaActual>();
        while (await r.ReadAsync(cancellationToken)) result.Add(LeerPago(r));
        return result;
    }

    public async Task<long> SumarReintegrosPorPagoAsync(ISesionPersistencia sesion, string pagoId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        using var cmd = CrearComando(s, "SELECT COALESCE(-SUM(importe_minor), 0) FROM caja_movimiento WHERE pago_id = $pago AND tipo = 'reintegro';");
        cmd.Parameters.AddWithValue("$pago", pagoId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task InsertarCorreccionPagoAsync(ISesionPersistencia sesion, string pagoId, string medioAnterior, string medioNuevo, long importeMinor, string motivo, string usuarioId, string operacionId, string cajaOperacionId, string creadoEn, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var cmd = CrearComando(s, "INSERT INTO pago_correccion (id, pago_id, medio_anterior, medio_nuevo, importe_minor, motivo, usuario_id, operacion_id, caja_operacion_id, creado_en) VALUES ($id, $pago, $anterior, $nuevo, $importe, $motivo, $usuario, $operacion, $cajaOp, $fecha);");
            cmd.Parameters.AddWithValue("$id", _ids.NuevoId());
            cmd.Parameters.AddWithValue("$pago", pagoId);
            cmd.Parameters.AddWithValue("$anterior", medioAnterior);
            cmd.Parameters.AddWithValue("$nuevo", medioNuevo);
            cmd.Parameters.AddWithValue("$importe", importeMinor);
            cmd.Parameters.AddWithValue("$motivo", motivo);
            cmd.Parameters.AddWithValue("$usuario", usuarioId);
            cmd.Parameters.AddWithValue("$operacion", operacionId);
            cmd.Parameters.AddWithValue("$cajaOp", cajaOperacionId);
            cmd.Parameters.AddWithValue("$fecha", creadoEn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex)) { throw ErroresSqlite.OcupadoException(ex); }
    }

    private const string SelectSesion = """
        SELECT c.id, c.estado, c.fondo_inicial_minor, c.moneda, c.abierta_en, c.usuario_id, u.nombre,
               c.cerrada_en, c.cerrado_por_usuario_id, uc.nombre, c.efectivo_esperado_minor,
               c.efectivo_contado_minor, c.diferencia_minor, c.observacion_cierre,
               c.operacion_apertura_id, c.operacion_cierre_id
        FROM caja_sesion c LEFT JOIN usuario u ON u.id = c.usuario_id
                           LEFT JOIN usuario uc ON uc.id = c.cerrado_por_usuario_id
        """;

    private const string SelectPagoActual = """
        SELECT p.id, p.venta_id, v.numero, v.estado, p.medio, COALESCE(pc.medio_nuevo, p.medio),
               p.importe_minor, (pc.pago_id IS NOT NULL),
               (SELECT m.id FROM caja_movimiento m WHERE m.pago_id = p.id AND m.tipo = 'venta' ORDER BY m.creado_en, m.id LIMIT 1)
        FROM pago p JOIN venta v ON v.id = p.venta_id
        LEFT JOIN pago_correccion pc ON pc.pago_id = p.id
        """;

    private static SesionCajaRegistrada LeerSesion(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5), N(r, 6),
        N(r, 7), N(r, 8), N(r, 9), L(r, 10), L(r, 11), L(r, 12), N(r, 13), r.GetString(14), N(r, 15));

    private static PagoCajaActual LeerPago(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt64(6), r.GetInt64(7) == 1, N(r, 8));

    private static string? N(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static long? L(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    private static SqliteCommand CrearComando(ISesionSqlite sesion, string sql)
    {
        var comando = sesion.Conexion.CreateCommand();
        comando.CommandTimeout = sesion.TimeoutSegundos;
        comando.CommandText = sql;
        return comando;
    }

    private static ISesionSqlite Sesion(ISesionPersistencia sesion) => sesion as ISesionSqlite
        ?? throw new ArgumentException("La sesión no pertenece a este proveedor SQLite.", nameof(sesion));

    private static void ExigirEscritura(ISesionSqlite sesion)
    {
        if (sesion.SoloLectura) throw new InvalidOperationException("La sesión es de solo lectura y no admite escrituras.");
    }
}
