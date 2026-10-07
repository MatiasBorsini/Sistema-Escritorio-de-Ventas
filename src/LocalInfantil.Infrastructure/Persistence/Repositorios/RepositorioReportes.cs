using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

/// <summary>
/// Solo lectura agregada para reportes. Cada consulta resuelve todo en una
/// lectura SQL (GROUP BY/SUM/COUNT, sin N+1, sin agrupar miles de filas en
/// C#). Semántica por fecha del evento: cada hecho entra en el período en
/// que ocurrió; nunca se reescriben períodos históricos.
/// </summary>
public sealed class RepositorioReportes : IRepositorioReportes
{
    public async Task<ResumenVentasPeriodo> ResumenVentasAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT
                 (SELECT COALESCE(SUM(l.subtotal_minor), 0) FROM venta_linea l JOIN venta v ON v.id = l.venta_id WHERE v.creada_en >= $d AND v.creada_en < $h AND v.estado IN ('confirmada', 'anulada')),
                 (SELECT COUNT(*) FROM venta v WHERE v.creada_en >= $d AND v.creada_en < $h AND v.estado IN ('confirmada', 'anulada')),
                 (SELECT COALESCE(SUM(v.total_minor), 0) FROM reversion_venta r JOIN venta v ON v.id = r.venta_id WHERE r.creado_en >= $d AND r.creado_en < $h),
                 (SELECT COUNT(*) FROM reversion_venta r WHERE r.creado_en >= $d AND r.creado_en < $h),
                 (SELECT COALESCE(SUM(c.importe_entradas_minor), 0) FROM cambio c WHERE c.creado_en >= $d AND c.creado_en < $h),
                 (SELECT COALESCE(SUM(c.importe_salidas_minor), 0) FROM cambio c WHERE c.creado_en >= $d AND c.creado_en < $h),
                 (SELECT COALESCE(SUM(p.importe_minor), 0) FROM pago p WHERE p.creado_en >= $d AND p.creado_en < $h),
                 (SELECT COALESCE(SUM(ac.importe_minor), 0) FROM cuenta_corriente_abono_componente ac JOIN cuenta_corriente_abono a ON a.id = ac.abono_id WHERE a.creado_en >= $d AND a.creado_en < $h),
                 (SELECT COALESCE(SUM(cp.importe_minor), 0) FROM cambio_pago cp JOIN cambio c ON c.id = cp.cambio_id WHERE cp.direccion = 'cobro' AND c.creado_en >= $d AND c.creado_en < $h),
                 (SELECT COALESCE(SUM(-m.importe_minor), 0) FROM caja_movimiento m WHERE m.tipo = 'reintegro' AND m.creado_en >= $d AND m.creado_en < $h),
                 (SELECT COALESCE(SUM(cp.importe_minor), 0) FROM cambio_pago cp JOIN cambio c ON c.id = cp.cambio_id WHERE cp.direccion = 'reintegro' AND c.creado_en >= $d AND c.creado_en < $h);
                """;
            comando.Parameters.AddWithValue("$d", desdeUtc);
            comando.Parameters.AddWithValue("$h", hastaUtc);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            await lector.ReadAsync(cancellationToken);
            var brutas = lector.GetInt64(0);
            var anulaciones = lector.GetInt64(2);
            var entradas = lector.GetInt64(4);
            var salidas = lector.GetInt64(5);
            var cobrado = lector.GetInt64(6) + lector.GetInt64(7) + lector.GetInt64(8);
            var reintegros = lector.GetInt64(9) + lector.GetInt64(10);
            return new ResumenVentasPeriodo(
                brutas,
                (int)lector.GetInt64(1),
                anulaciones,
                (int)lector.GetInt64(3),
                entradas,
                salidas,
                brutas - anulaciones - entradas + salidas,
                cobrado,
                reintegros,
                cobrado - reintegros);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<FilaDiaria>> VentasPorDiaAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, string desplazamientoHoras, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT dia, SUM(brutas), SUM(ajustes), SUM(netas), SUM(cobros), SUM(reintegros) FROM (
                 SELECT date(v.creada_en, $off) AS dia, SUM(l.subtotal_minor) AS brutas, 0 AS ajustes, SUM(l.subtotal_minor) AS netas, 0 AS cobros, 0 AS reintegros
                 FROM venta v JOIN venta_linea l ON l.venta_id = v.id
                 WHERE v.creada_en >= $d AND v.creada_en < $h AND v.estado IN ('confirmada', 'anulada')
                 GROUP BY 1
                 UNION ALL
                 SELECT date(r.creado_en, $off), 0, -SUM(v.total_minor), -SUM(v.total_minor), 0, 0
                 FROM reversion_venta r JOIN venta v ON v.id = r.venta_id
                 WHERE r.creado_en >= $d AND r.creado_en < $h
                 GROUP BY 1
                 UNION ALL
                 SELECT date(c.creado_en, $off), 0, SUM(-c.importe_entradas_minor + c.importe_salidas_minor), SUM(-c.importe_entradas_minor + c.importe_salidas_minor), 0, 0
                 FROM cambio c
                 WHERE c.creado_en >= $d AND c.creado_en < $h
                 GROUP BY 1
                 UNION ALL
                 SELECT date(p.creado_en, $off), 0, 0, 0, SUM(p.importe_minor), 0
                 FROM pago p
                 WHERE p.creado_en >= $d AND p.creado_en < $h
                 GROUP BY 1
                 UNION ALL
                 SELECT date(a.creado_en, $off), 0, 0, 0, SUM(ac.importe_minor), 0
                 FROM cuenta_corriente_abono_componente ac JOIN cuenta_corriente_abono a ON a.id = ac.abono_id
                 WHERE a.creado_en >= $d AND a.creado_en < $h
                 GROUP BY 1
                 UNION ALL
                 SELECT date(c.creado_en, $off), 0, 0, 0, SUM(cp.importe_minor), 0
                 FROM cambio_pago cp JOIN cambio c ON c.id = cp.cambio_id
                 WHERE cp.direccion = 'cobro' AND c.creado_en >= $d AND c.creado_en < $h
                 GROUP BY 1
                 UNION ALL
                 SELECT date(m.creado_en, $off), 0, 0, 0, 0, SUM(-m.importe_minor)
                 FROM caja_movimiento m
                 WHERE m.tipo = 'reintegro' AND m.creado_en >= $d AND m.creado_en < $h
                 GROUP BY 1
                 UNION ALL
                 SELECT date(c.creado_en, $off), 0, 0, 0, 0, SUM(cp.importe_minor)
                 FROM cambio_pago cp JOIN cambio c ON c.id = cp.cambio_id
                 WHERE cp.direccion = 'reintegro' AND c.creado_en >= $d AND c.creado_en < $h
                 GROUP BY 1
                ) GROUP BY 1 ORDER BY 1;
                """;
            comando.Parameters.AddWithValue("$d", desdeUtc);
            comando.Parameters.AddWithValue("$h", hastaUtc);
            comando.Parameters.AddWithValue("$off", desplazamientoHoras);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<FilaDiaria>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new FilaDiaria(
                    lector.GetString(0),
                    lector.GetInt64(1),
                    lector.GetInt64(2),
                    lector.GetInt64(3),
                    lector.GetInt64(4),
                    lector.GetInt64(5)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<DetalleVentaReporte>> DetalleVentasAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, int limite, int desplazamiento, CancellationToken cancellationToken = default)
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

        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT fecha, tipo, referencia, cliente, bruta, ajustes, neto, cobrado, estado FROM (
                 SELECT v.creada_en AS fecha, 'venta' AS tipo, v.numero AS referencia, v.cliente_nombre_snapshot AS cliente,
                        v.total_minor AS bruta, 0 AS ajustes, v.total_minor AS neto,
                        (SELECT COALESCE(SUM(p.importe_minor), 0) FROM pago p WHERE p.venta_id = v.id) AS cobrado,
                        v.estado AS estado
                 FROM venta v
                 WHERE v.creada_en >= $d AND v.creada_en < $h AND v.estado IN ('confirmada', 'anulada')
                 UNION ALL
                 SELECT r.creado_en, 'anulacion', v.numero, v.cliente_nombre_snapshot, 0, -v.total_minor, -v.total_minor, NULL, 'anulada'
                 FROM reversion_venta r JOIN venta v ON v.id = r.venta_id
                 WHERE r.creado_en >= $d AND r.creado_en < $h
                 UNION ALL
                 SELECT c.creado_en, 'cambio', c.numero, (SELECT v2.cliente_nombre_snapshot FROM venta v2 WHERE v2.id = c.venta_id),
                        0, (-c.importe_entradas_minor + c.importe_salidas_minor), (-c.importe_entradas_minor + c.importe_salidas_minor), NULL, 'registrado'
                 FROM cambio c
                 WHERE c.creado_en >= $d AND c.creado_en < $h
                ) ORDER BY 1 DESC, 3 DESC LIMIT $limite OFFSET $desplazamiento;
                """;
            comando.Parameters.AddWithValue("$d", desdeUtc);
            comando.Parameters.AddWithValue("$h", hastaUtc);
            comando.Parameters.AddWithValue("$limite", limite + 1);
            comando.Parameters.AddWithValue("$desplazamiento", desplazamiento);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<DetalleVentaReporte>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new DetalleVentaReporte(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.IsDBNull(3) ? null : lector.GetString(3),
                    lector.GetInt64(4),
                    lector.GetInt64(5),
                    lector.GetInt64(6),
                    lector.IsDBNull(7) ? null : lector.GetInt64(7),
                    lector.GetString(8)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<DesgloseMedio>> DesgloseMediosAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT m.medio,
                       COALESCE(v.ventas, 0), COALESCE(a.abonos, 0), COALESCE(cc.cobros, 0), COALESCE(r.reintegros, 0),
                       COALESCE(v.ventas, 0) + COALESCE(a.abonos, 0) + COALESCE(cc.cobros, 0) - COALESCE(r.reintegros, 0),
                       COALESCE(v.corregido, 0)
                FROM (SELECT 'efectivo' AS medio UNION ALL SELECT 'transferencia' UNION ALL SELECT 'debito' UNION ALL SELECT 'credito' UNION ALL SELECT 'billetera' UNION ALL SELECT 'mercado_pago') m
                LEFT JOIN (SELECT COALESCE(pc.medio_nuevo, p.medio) AS medio, SUM(p.importe_minor) AS ventas,
                                  MAX(pc.pago_id IS NOT NULL) AS corregido
                           FROM pago p LEFT JOIN pago_correccion pc ON pc.pago_id = p.id
                           WHERE p.creado_en >= $d AND p.creado_en < $h
                           GROUP BY 1) v ON v.medio = m.medio
                LEFT JOIN (SELECT ac.medio AS medio, SUM(ac.importe_minor) AS abonos
                           FROM cuenta_corriente_abono_componente ac JOIN cuenta_corriente_abono a ON a.id = ac.abono_id
                           WHERE a.creado_en >= $d AND a.creado_en < $h
                           GROUP BY 1) a ON a.medio = m.medio
                LEFT JOIN (SELECT cp.medio AS medio, SUM(cp.importe_minor) AS cobros
                           FROM cambio_pago cp JOIN cambio c ON c.id = cp.cambio_id
                           WHERE cp.direccion = 'cobro' AND c.creado_en >= $d AND c.creado_en < $h
                           GROUP BY 1) cc ON cc.medio = m.medio
                LEFT JOIN (SELECT medio, SUM(monto) AS reintegros FROM (
                             SELECT cm.medio AS medio, -cm.importe_minor AS monto
                             FROM caja_movimiento cm
                             WHERE cm.tipo = 'reintegro' AND cm.medio IS NOT NULL AND cm.creado_en >= $d AND cm.creado_en < $h
                             UNION ALL
                             SELECT cp.medio AS medio, cp.importe_minor AS monto
                             FROM cambio_pago cp JOIN cambio c ON c.id = cp.cambio_id
                             WHERE cp.direccion = 'reintegro' AND c.creado_en >= $d AND c.creado_en < $h
                           ) GROUP BY 1) r ON r.medio = m.medio
                ORDER BY m.medio;
                """;
            comando.Parameters.AddWithValue("$d", desdeUtc);
            comando.Parameters.AddWithValue("$h", hastaUtc);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<DesgloseMedio>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new DesgloseMedio(
                    lector.GetString(0),
                    lector.GetInt64(1),
                    lector.GetInt64(2),
                    lector.GetInt64(3),
                    lector.GetInt64(4),
                    lector.GetInt64(5),
                    lector.GetInt64(6) == 1));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<FilaStockActual>> StockActualAsync(ISesionPersistencia sesion, string texto, string? categoriaId, bool soloCero, long? hastaUnidades, int limite, int desplazamiento, CancellationToken cancellationToken = default)
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
                SELECT producto_id, producto, codigo_interno, variante_id, talle, color, categoria, disponible, apartado, fisico FROM (
                 SELECT p.id AS producto_id, p.nombre AS producto, var.codigo_interno AS codigo_interno, var.id AS variante_id,
                        var.talle AS talle, var.color AS color, c.nombre AS categoria,
                        COALESCE((SELECT SUM(m.cantidad) FROM movimiento_inventario m WHERE m.variante_id = var.id), 0)
                        - COALESCE((SELECT SUM(CASE WHEN a.tipo = 'apartar' THEN a.cantidad ELSE -a.cantidad END) FROM apartado_movimiento a WHERE a.variante_id = var.id), 0) AS disponible,
                        COALESCE((SELECT SUM(CASE WHEN a.tipo = 'apartar' THEN a.cantidad ELSE -a.cantidad END) FROM apartado_movimiento a WHERE a.variante_id = var.id), 0) AS apartado,
                        COALESCE((SELECT SUM(m.cantidad) FROM movimiento_inventario m WHERE m.variante_id = var.id), 0) AS fisico
                 FROM variante var JOIN producto p ON p.id = var.producto_id LEFT JOIN categoria c ON c.id = p.categoria_id
                 WHERE var.activa = 1
                   AND ($t = '' OR p.nombre LIKE $contiene OR var.codigo_interno LIKE $contiene)
                   AND ($cat IS NULL OR p.categoria_id = $cat)
                ) WHERE ($cero = 0 OR fisico = 0) AND ($hasta IS NULL OR fisico <= $hasta)
                ORDER BY 2, 3 LIMIT $limite OFFSET $desplazamiento;
                """;
            comando.Parameters.AddWithValue("$t", t);
            comando.Parameters.AddWithValue("$contiene", $"%{t}%");
            comando.Parameters.AddWithValue("$cat", (object?)categoriaId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$cero", soloCero ? 1 : 0);
            comando.Parameters.AddWithValue("$hasta", (object?)hastaUnidades ?? DBNull.Value);
            comando.Parameters.AddWithValue("$limite", limite + 1);
            comando.Parameters.AddWithValue("$desplazamiento", desplazamiento);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<FilaStockActual>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new FilaStockActual(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5),
                    lector.IsDBNull(6) ? null : lector.GetString(6),
                    lector.GetInt64(7),
                    lector.GetInt64(8),
                    lector.GetInt64(9)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<RankingVenta>> RankingVentasAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, int limite, int desplazamiento, CancellationToken cancellationToken = default)
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

        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT producto_id, nombre, codigo_interno, variante_id, talle, color,
                       SUM(brutas), SUM(devueltas), SUM(salidas), SUM(brutas) - SUM(devueltas) + SUM(salidas) FROM (
                 SELECT var.producto_id AS producto_id, p.nombre AS nombre, var.codigo_interno AS codigo_interno, var.id AS variante_id,
                        var.talle AS talle, var.color AS color, SUM(l.cantidad) AS brutas, 0 AS devueltas, 0 AS salidas
                 FROM venta_linea l JOIN venta v ON v.id = l.venta_id
                      JOIN variante var ON var.id = l.variante_id JOIN producto p ON p.id = var.producto_id
                 WHERE v.creada_en >= $d AND v.creada_en < $h AND v.estado IN ('confirmada', 'anulada')
                 GROUP BY var.id
                 UNION ALL
                 SELECT var.producto_id, p.nombre, var.codigo_interno, var.id, var.talle, var.color, 0, SUM(e.cantidad), 0
                 FROM cambio_entrada e JOIN cambio c ON c.id = e.cambio_id
                      JOIN variante var ON var.id = e.variante_id JOIN producto p ON p.id = var.producto_id
                 WHERE c.creado_en >= $d AND c.creado_en < $h
                 GROUP BY var.id
                 UNION ALL
                 SELECT var.producto_id, p.nombre, var.codigo_interno, var.id, var.talle, var.color, 0, 0, SUM(s.cantidad)
                 FROM cambio_salida s JOIN cambio c ON c.id = s.cambio_id
                      JOIN variante var ON var.id = s.variante_id JOIN producto p ON p.id = var.producto_id
                 WHERE c.creado_en >= $d AND c.creado_en < $h
                 GROUP BY var.id
                ) GROUP BY 4 HAVING SUM(brutas) + SUM(devueltas) + SUM(salidas) > 0
                ORDER BY SUM(brutas) - SUM(devueltas) + SUM(salidas) DESC, 3 LIMIT $limite OFFSET $desplazamiento;
                """;
            comando.Parameters.AddWithValue("$d", desdeUtc);
            comando.Parameters.AddWithValue("$h", hastaUtc);
            comando.Parameters.AddWithValue("$limite", limite + 1);
            comando.Parameters.AddWithValue("$desplazamiento", desplazamiento);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<RankingVenta>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new RankingVenta(
                    lector.IsDBNull(0) ? null : lector.GetString(0),
                    lector.GetString(2),
                    lector.GetString(1),
                    lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5),
                    lector.GetInt64(6),
                    lector.GetInt64(7),
                    lector.GetInt64(8),
                    lector.GetInt64(9)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<RankingVenta>> RankingPorProductoAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, int limite, int desplazamiento, CancellationToken cancellationToken = default)
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

        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT producto_id, nombre, MIN(codigo_interno), NULL, NULL, NULL,
                       SUM(brutas), SUM(devueltas), SUM(salidas), SUM(brutas) - SUM(devueltas) + SUM(salidas) FROM (
                 SELECT var.producto_id AS producto_id, p.nombre AS nombre, var.codigo_interno AS codigo_interno, SUM(l.cantidad) AS brutas, 0 AS devueltas, 0 AS salidas
                 FROM venta_linea l JOIN venta v ON v.id = l.venta_id
                      JOIN variante var ON var.id = l.variante_id JOIN producto p ON p.id = var.producto_id
                 WHERE v.creada_en >= $d AND v.creada_en < $h AND v.estado IN ('confirmada', 'anulada')
                 GROUP BY var.id
                 UNION ALL
                 SELECT var.producto_id, p.nombre, var.codigo_interno, 0, SUM(e.cantidad), 0
                 FROM cambio_entrada e JOIN cambio c ON c.id = e.cambio_id
                      JOIN variante var ON var.id = e.variante_id JOIN producto p ON p.id = var.producto_id
                 WHERE c.creado_en >= $d AND c.creado_en < $h
                 GROUP BY var.id
                 UNION ALL
                 SELECT var.producto_id, p.nombre, var.codigo_interno, 0, 0, SUM(s.cantidad)
                 FROM cambio_salida s JOIN cambio c ON c.id = s.cambio_id
                      JOIN variante var ON var.id = s.variante_id JOIN producto p ON p.id = var.producto_id
                 WHERE c.creado_en >= $d AND c.creado_en < $h
                 GROUP BY var.id
                ) GROUP BY 1 HAVING SUM(brutas) + SUM(devueltas) + SUM(salidas) > 0
                ORDER BY SUM(brutas) - SUM(devueltas) + SUM(salidas) DESC, 2 LIMIT $limite OFFSET $desplazamiento;
                """;
            comando.Parameters.AddWithValue("$d", desdeUtc);
            comando.Parameters.AddWithValue("$h", hastaUtc);
            comando.Parameters.AddWithValue("$limite", limite + 1);
            comando.Parameters.AddWithValue("$desplazamiento", desplazamiento);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<RankingVenta>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new RankingVenta(
                    lector.IsDBNull(0) ? null : lector.GetString(0),
                    lector.GetString(2),
                    lector.GetString(1),
                    null,
                    null,
                    null,
                    lector.GetInt64(6),
                    lector.GetInt64(7),
                    lector.GetInt64(8),
                    lector.GetInt64(9)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<SinVentas>> SinVentasAsync(ISesionPersistencia sesion, string corteUtc, int limite, int desplazamiento, CancellationToken cancellationToken = default)
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

        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT variante_id, producto, codigo_interno, talle, color, ultima_venta, ultima_recepcion, ultimo_movimiento,
                       CASE WHEN ultima_venta IS NULL THEN 1 ELSE 0 END AS nunca FROM (
                 SELECT var.id AS variante_id, p.nombre AS producto, var.codigo_interno AS codigo_interno,
                        var.talle AS talle, var.color AS color,
                        (SELECT MAX(v.creada_en) FROM venta_linea l JOIN venta v ON v.id = l.venta_id
                         WHERE l.variante_id = var.id AND v.estado IN ('confirmada', 'anulada')) AS ultima_venta,
                        (SELECT MAX(r.creado_en) FROM recepcion_linea rl JOIN recepcion r ON r.id = rl.recepcion_id
                         WHERE rl.variante_id = var.id) AS ultima_recepcion,
                        (SELECT MAX(m.creado_en) FROM movimiento_inventario m WHERE m.variante_id = var.id) AS ultimo_movimiento
                 FROM variante var JOIN producto p ON p.id = var.producto_id
                 WHERE var.activa = 1
                ) WHERE ultima_venta IS NULL OR ultima_venta < $corte
                ORDER BY 9 DESC, 6, 3 LIMIT $limite OFFSET $desplazamiento;
                """;
            comando.Parameters.AddWithValue("$corte", corteUtc);
            comando.Parameters.AddWithValue("$limite", limite + 1);
            comando.Parameters.AddWithValue("$desplazamiento", desplazamiento);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<SinVentas>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new SinVentas(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.IsDBNull(3) ? null : lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5),
                    lector.IsDBNull(6) ? null : lector.GetString(6),
                    lector.IsDBNull(7) ? null : lector.GetString(7),
                    lector.GetInt64(8) == 1));
            }

            return resultado;
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
}
