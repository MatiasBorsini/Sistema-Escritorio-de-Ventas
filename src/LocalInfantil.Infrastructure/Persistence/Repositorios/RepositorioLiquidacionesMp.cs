using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioLiquidacionesMp : IRepositorioLiquidacionesMp
{
    private const string ColsImportacion =
        "id, origen, archivo_nombre, archivo_hash, cuenta_mp, formato_reporte, zona_horaria, rango_desde, rango_hasta, filas_totales, filas_aplicadas, filas_error, estado, usuario_id, creado_en";

    private const string ColsLiquidacion =
        "id, importacion_id, fila_ordinal, fila_hash, identidad_remota, fuente, source_id, record_type, descripcion, clasificacion, " +
        "fecha_transaccion, fecha, moneda, external_reference, order_mp, order_id, gross_minor, " +
        "net_credito_minor, net_debito_minor, mp_fee_minor, financing_fee_minor, shipping_fee_minor, " +
        "taxes_minor, coupon_minor, effective_coupon_minor, installments, payment_method, " +
        "payment_method_type, pos_id, poi_id, store_id, operation_tags, metadata_json, " +
        "estado_conciliacion, venta_id, pago_id, operacion_mp_id, cobro_manual_id, observacion, creado_en";

    private readonly IGeneradorIds _ids;

    public RepositorioLiquidacionesMp(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<bool> ExisteImportacionPorHashAsync(ISesionPersistencia sesion, string archivoHash, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT 1 FROM mercado_pago_importacion WHERE archivo_hash = $hash LIMIT 1;";
            comando.Parameters.AddWithValue("$hash", archivoHash);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<string> InsertarImportacionAsync(ISesionPersistencia sesion, ImportacionMpRegistrada importacion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importacion);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText =
                $"INSERT INTO mercado_pago_importacion ({ColsImportacion}) VALUES ($id, $origen, $nombre, $hash, $cuenta, $formato, $zona, $desde, $hasta, $totales, $aplicadas, $errores, $estado, $usuario, $creado);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$origen", importacion.Origen);
            comando.Parameters.AddWithValue("$nombre", (object?)importacion.ArchivoNombre ?? DBNull.Value);
            comando.Parameters.AddWithValue("$hash", (object?)importacion.ArchivoHash ?? DBNull.Value);
            comando.Parameters.AddWithValue("$cuenta", (object?)importacion.CuentaMp ?? DBNull.Value);
            comando.Parameters.AddWithValue("$formato", (object?)importacion.FormatoReporte ?? DBNull.Value);
            comando.Parameters.AddWithValue("$zona", (object?)importacion.ZonaHoraria ?? DBNull.Value);
            comando.Parameters.AddWithValue("$desde", (object?)importacion.RangoDesde ?? DBNull.Value);
            comando.Parameters.AddWithValue("$hasta", (object?)importacion.RangoHasta ?? DBNull.Value);
            comando.Parameters.AddWithValue("$totales", importacion.FilasTotales);
            comando.Parameters.AddWithValue("$aplicadas", importacion.FilasAplicadas);
            comando.Parameters.AddWithValue("$errores", importacion.FilasError);
            comando.Parameters.AddWithValue("$estado", importacion.Estado);
            comando.Parameters.AddWithValue("$usuario", importacion.UsuarioId ?? string.Empty);
            comando.Parameters.AddWithValue("$creado", importacion.CreadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task ActualizarResultadoImportacionAsync(ISesionPersistencia sesion, string id, string estado, long filasTotales, long filasAplicadas, long filasError, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE mercado_pago_importacion SET estado = $estado, filas_totales = $totales, filas_aplicadas = $aplicadas, filas_error = $errores WHERE id = $id;";
            comando.Parameters.AddWithValue("$estado", estado);
            comando.Parameters.AddWithValue("$totales", filasTotales);
            comando.Parameters.AddWithValue("$aplicadas", filasAplicadas);
            comando.Parameters.AddWithValue("$errores", filasError);
            comando.Parameters.AddWithValue("$id", id);
            if (await comando.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("La importación no existe.");
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<string> InsertarLiquidacionAsync(ISesionPersistencia sesion, string importacionId, FilaLiquidacionMp fila, string creadoEn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fila);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                INSERT INTO mercado_pago_liquidacion
                (id, importacion_id, fila_ordinal, fila_hash, identidad_remota, fuente, source_id, record_type, descripcion, clasificacion,
                 fecha_transaccion, fecha, moneda, external_reference, order_mp, order_id, gross_minor,
                 net_credito_minor, net_debito_minor, mp_fee_minor, financing_fee_minor, shipping_fee_minor,
                 taxes_minor, coupon_minor, effective_coupon_minor, installments, payment_method,
                 payment_method_type, pos_id, poi_id, store_id, operation_tags, metadata_json,
                 estado_conciliacion, creado_en)
                VALUES ($id, $imp, $ordinal, $hash, $identidad, 'archivo', $source, $tipo, $desc, $clase,
                 $ftrx, $fecha, $moneda, $ext, $ordermp, $orderid, $gross,
                 $ncred, $ndeb, $mpfee, $finfee, $shipfee,
                 $taxes, $cupon, $ecupon, $cuotas, $pmedio,
                 $ptipo, $pos, $poi, $store, $tags, $meta,
                 'pendiente', $creado);
                """;
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$imp", importacionId);
            comando.Parameters.AddWithValue("$ordinal", fila.Ordinal);
            comando.Parameters.AddWithValue("$hash", fila.FilaHash);
            comando.Parameters.AddWithValue("$identidad", (object?)fila.IdentidadRemota ?? DBNull.Value);
            comando.Parameters.AddWithValue("$source", (object?)fila.SourceId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$tipo", fila.RecordType);
            comando.Parameters.AddWithValue("$desc", fila.Descripcion);
            comando.Parameters.AddWithValue("$clase", fila.Clasificacion);
            comando.Parameters.AddWithValue("$ftrx", (object?)fila.FechaTransaccion ?? DBNull.Value);
            comando.Parameters.AddWithValue("$fecha", (object?)fila.Fecha ?? DBNull.Value);
            comando.Parameters.AddWithValue("$moneda", (object?)fila.Moneda ?? DBNull.Value);
            comando.Parameters.AddWithValue("$ext", (object?)fila.ExternalReference ?? DBNull.Value);
            comando.Parameters.AddWithValue("$ordermp", (object?)fila.OrderMp ?? DBNull.Value);
            comando.Parameters.AddWithValue("$orderid", (object?)fila.OrderId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$gross", (object?)fila.GrossMinor ?? DBNull.Value);
            comando.Parameters.AddWithValue("$ncred", (object?)fila.NetCreditoMinor ?? DBNull.Value);
            comando.Parameters.AddWithValue("$ndeb", (object?)fila.NetDebitoMinor ?? DBNull.Value);
            comando.Parameters.AddWithValue("$mpfee", (object?)fila.MpFeeMinor ?? DBNull.Value);
            comando.Parameters.AddWithValue("$finfee", (object?)fila.FinancingFeeMinor ?? DBNull.Value);
            comando.Parameters.AddWithValue("$shipfee", (object?)fila.ShippingFeeMinor ?? DBNull.Value);
            comando.Parameters.AddWithValue("$taxes", (object?)fila.TaxesMinor ?? DBNull.Value);
            comando.Parameters.AddWithValue("$cupon", (object?)fila.CouponMinor ?? DBNull.Value);
            comando.Parameters.AddWithValue("$ecupon", (object?)fila.EffectiveCouponMinor ?? DBNull.Value);
            comando.Parameters.AddWithValue("$cuotas", (object?)fila.Installments ?? DBNull.Value);
            comando.Parameters.AddWithValue("$pmedio", (object?)fila.PaymentMethod ?? DBNull.Value);
            comando.Parameters.AddWithValue("$ptipo", (object?)fila.PaymentMethodType ?? DBNull.Value);
            comando.Parameters.AddWithValue("$pos", (object?)fila.PosId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$poi", (object?)fila.PoiId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$store", (object?)fila.StoreId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$tags", (object?)fila.OperationTags ?? DBNull.Value);
            comando.Parameters.AddWithValue("$meta", (object?)fila.MetadataJson ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creado", creadoEn);
            if (await comando.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("No se pudo insertar la liquidación.");
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<LiquidacionMpRegistrada?> ObtenerLiquidacionAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = $"SELECT {ColsLiquidacion} FROM mercado_pago_liquidacion WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken)) return null;
            return LeerLiquidacion(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<LiquidacionMpRegistrada>> ListarLiquidacionesAsync(ISesionPersistencia sesion, string? estadoConciliacion, int limite, int desplazamiento, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            if (estadoConciliacion is null)
                comando.CommandText = $"SELECT {ColsLiquidacion} FROM mercado_pago_liquidacion ORDER BY creado_en DESC, id DESC LIMIT $limite OFFSET $offset;";
            else
            {
                comando.CommandText = $"SELECT {ColsLiquidacion} FROM mercado_pago_liquidacion WHERE estado_conciliacion = $estado ORDER BY creado_en DESC, id DESC LIMIT $limite OFFSET $offset;";
                comando.Parameters.AddWithValue("$estado", estadoConciliacion);
            }
            comando.Parameters.AddWithValue("$limite", limite);
            comando.Parameters.AddWithValue("$offset", desplazamiento);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var lista = new List<LiquidacionMpRegistrada>();
            while (await lector.ReadAsync(cancellationToken)) lista.Add(LeerLiquidacion(lector));
            return lista;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<LiquidacionMpRegistrada>> ListarPendientesAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = $"SELECT {ColsLiquidacion} FROM mercado_pago_liquidacion WHERE estado_conciliacion IN ('pendiente','requiere_intervencion','con_diferencia') ORDER BY fecha, creado_en;";
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var lista = new List<LiquidacionMpRegistrada>();
            while (await lector.ReadAsync(cancellationToken)) lista.Add(LeerLiquidacion(lector));
            return lista;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListarHashesExistentesAsync(ISesionPersistencia sesion, IReadOnlyCollection<string> hashes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.Count == 0) return Array.Empty<string>();
        var s = Sesion(sesion);
        try
        {
            var existentes = new List<string>();
            var lista = hashes.ToArray();
            const int bloque = 400;
            for (var inicio = 0; inicio < lista.Length; inicio += bloque)
            {
                var porcion = lista.Skip(inicio).Take(bloque).ToArray();
                using var comando = s.Conexion.CreateCommand();
                comando.CommandTimeout = s.TimeoutSegundos;
                var parametros = string.Join(",", porcion.Select((_, i) => $"$h{i}"));
                comando.CommandText = $"SELECT fila_hash FROM mercado_pago_liquidacion WHERE fila_hash IN ({parametros});";
                for (var i = 0; i < porcion.Length; i++)
                    comando.Parameters.AddWithValue($"$h{i}", porcion[i]);
                await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
                while (await lector.ReadAsync(cancellationToken)) existentes.Add(lector.GetString(0));
            }
            return existentes;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListarIdentidadesRemotasExistentesAsync(ISesionPersistencia sesion, IReadOnlyCollection<string> identidades, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identidades);
        if (identidades.Count == 0) return Array.Empty<string>();
        var s = Sesion(sesion);
        try
        {
            var existentes = new List<string>();
            var lista = identidades.ToArray();
            const int bloque = 400;
            for (var inicio = 0; inicio < lista.Length; inicio += bloque)
            {
                var porcion = lista.Skip(inicio).Take(bloque).ToArray();
                using var comando = s.Conexion.CreateCommand();
                comando.CommandTimeout = s.TimeoutSegundos;
                var parametros = string.Join(",", porcion.Select((_, i) => $"$h{i}"));
                comando.CommandText = $"SELECT identidad_remota FROM mercado_pago_liquidacion WHERE identidad_remota IN ({parametros});";
                for (var i = 0; i < porcion.Length; i++)
                    comando.Parameters.AddWithValue($"$h{i}", porcion[i]);
                await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
                while (await lector.ReadAsync(cancellationToken)) existentes.Add(lector.GetString(0));
            }
            return existentes;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }
    public async Task<IReadOnlyList<ImportacionMpRegistrada>> ListarImportacionesAsync(ISesionPersistencia sesion, int limite, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = $"SELECT {ColsImportacion} FROM mercado_pago_importacion ORDER BY creado_en DESC, id DESC LIMIT $limite;";
            comando.Parameters.AddWithValue("$limite", limite);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var lista = new List<ImportacionMpRegistrada>();
            while (await lector.ReadAsync(cancellationToken)) lista.Add(LeerImportacion(lector));
            return lista;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task ActualizarConciliacionLiquidacionAsync(ISesionPersistencia sesion, string liquidacionId, string estadoConciliacion, string? ventaId, string? pagoId, string? operacionMpId, string? cobroManualId, string? observacion, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE mercado_pago_liquidacion SET estado_conciliacion = $estado, venta_id = $venta, pago_id = $pago, operacion_mp_id = $op, cobro_manual_id = $cobro, observacion = $obs WHERE id = $id;";
            comando.Parameters.AddWithValue("$estado", estadoConciliacion);
            comando.Parameters.AddWithValue("$venta", (object?)ventaId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$pago", (object?)pagoId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$op", (object?)operacionMpId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$cobro", (object?)cobroManualId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$obs", (object?)observacion ?? DBNull.Value);
            comando.Parameters.AddWithValue("$id", liquidacionId);
            if (await comando.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("La liquidación no existe.");
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<string> InsertarConciliacionAsync(ISesionPersistencia sesion, string liquidacionId, string? ventaId, string? pagoId, string? operacionMpId, string? cobroManualId, string tipoMatch, string? diferenciaDetalle, string? usuarioId, string creadoEn, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO mercado_pago_conciliacion (id, liquidacion_id, venta_id, pago_id, operacion_mp_id, cobro_manual_id, tipo_match, diferencia_detalle, confirmado_por_usuario_id, creado_en) VALUES ($id, $liq, $venta, $pago, $op, $cobro, $tipo, $dif, $usuario, $creado);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$liq", liquidacionId);
            comando.Parameters.AddWithValue("$venta", (object?)ventaId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$pago", (object?)pagoId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$op", (object?)operacionMpId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$cobro", (object?)cobroManualId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$tipo", tipoMatch);
            comando.Parameters.AddWithValue("$dif", (object?)diferenciaDetalle ?? DBNull.Value);
            comando.Parameters.AddWithValue("$usuario", (object?)usuarioId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creado", creadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<bool> ExisteConciliacionDeLiquidacionAsync(ISesionPersistencia sesion, string liquidacionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT 1 FROM mercado_pago_conciliacion WHERE liquidacion_id = $liq LIMIT 1;";
            comando.Parameters.AddWithValue("$liq", liquidacionId);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<bool> ExisteConciliacionPorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT 1 FROM mercado_pago_conciliacion WHERE venta_id = $venta LIMIT 1;";
            comando.Parameters.AddWithValue("$venta", ventaId);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<bool> ExisteConciliacionPorOperacionAsync(ISesionPersistencia sesion, string operacionMpId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT 1 FROM mercado_pago_conciliacion WHERE operacion_mp_id = $op LIMIT 1;";
            comando.Parameters.AddWithValue("$op", operacionMpId);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    private static LiquidacionMpRegistrada LeerLiquidacion(SqliteDataReader l) => new()
    {
        Id = l.GetString(0),
        ImportacionId = l.GetString(1),
        FilaOrdinal = l.GetInt64(2),
        FilaHash = l.GetString(3),
        IdentidadRemota = l.IsDBNull(4) ? null : l.GetString(4),
        Fuente = l.GetString(5),
        SourceId = l.IsDBNull(6) ? null : l.GetString(6),
        RecordType = l.GetString(7),
        Descripcion = l.GetString(8),
        Clasificacion = l.GetString(9),
        FechaTransaccion = l.IsDBNull(10) ? null : l.GetString(10),
        Fecha = l.IsDBNull(11) ? null : l.GetString(11),
        Moneda = l.IsDBNull(12) ? null : l.GetString(12),
        ExternalReference = l.IsDBNull(13) ? null : l.GetString(13),
        OrderMp = l.IsDBNull(14) ? null : l.GetString(14),
        OrderId = l.IsDBNull(15) ? null : l.GetString(15),
        GrossMinor = l.IsDBNull(16) ? null : l.GetInt64(16),
        NetCreditoMinor = l.IsDBNull(17) ? null : l.GetInt64(17),
        NetDebitoMinor = l.IsDBNull(18) ? null : l.GetInt64(18),
        MpFeeMinor = l.IsDBNull(19) ? null : l.GetInt64(19),
        FinancingFeeMinor = l.IsDBNull(20) ? null : l.GetInt64(20),
        ShippingFeeMinor = l.IsDBNull(21) ? null : l.GetInt64(21),
        TaxesMinor = l.IsDBNull(22) ? null : l.GetInt64(22),
        CouponMinor = l.IsDBNull(23) ? null : l.GetInt64(23),
        EffectiveCouponMinor = l.IsDBNull(24) ? null : l.GetInt64(24),
        Installments = l.IsDBNull(25) ? null : l.GetInt64(25),
        PaymentMethod = l.IsDBNull(26) ? null : l.GetString(26),
        PaymentMethodType = l.IsDBNull(27) ? null : l.GetString(27),
        PosId = l.IsDBNull(28) ? null : l.GetString(28),
        PoiId = l.IsDBNull(29) ? null : l.GetString(29),
        StoreId = l.IsDBNull(30) ? null : l.GetString(30),
        OperationTags = l.IsDBNull(31) ? null : l.GetString(31),
        MetadataJson = l.IsDBNull(32) ? null : l.GetString(32),
        EstadoConciliacion = l.GetString(33),
        VentaId = l.IsDBNull(34) ? null : l.GetString(34),
        PagoId = l.IsDBNull(35) ? null : l.GetString(35),
        OperacionMpId = l.IsDBNull(36) ? null : l.GetString(36),
        CobroManualId = l.IsDBNull(37) ? null : l.GetString(37),
        Observacion = l.IsDBNull(38) ? null : l.GetString(38),
        CreadoEn = l.GetString(39)
    };

    private static ImportacionMpRegistrada LeerImportacion(SqliteDataReader l) => new()
    {
        Id = l.GetString(0),
        Origen = l.GetString(1),
        ArchivoNombre = l.IsDBNull(2) ? null : l.GetString(2),
        ArchivoHash = l.IsDBNull(3) ? null : l.GetString(3),
        CuentaMp = l.IsDBNull(4) ? null : l.GetString(4),
        FormatoReporte = l.IsDBNull(5) ? null : l.GetString(5),
        ZonaHoraria = l.IsDBNull(6) ? null : l.GetString(6),
        RangoDesde = l.IsDBNull(7) ? null : l.GetString(7),
        RangoHasta = l.IsDBNull(8) ? null : l.GetString(8),
        FilasTotales = l.GetInt64(9),
        FilasAplicadas = l.GetInt64(10),
        FilasError = l.GetInt64(11),
        Estado = l.GetString(12),
        UsuarioId = l.GetString(13),
        CreadoEn = l.GetString(14)
    };

    private static ISesionSqlite Sesion(ISesionPersistencia sesion)
    {
        if (sesion is not ISesionSqlite s)
            throw new ArgumentException("La sesión no pertenece a este proveedor SQLite.", nameof(sesion));
        return s;
    }

    private static void ExigirEscritura(ISesionSqlite sesion)
    {
        if (sesion.SoloLectura)
            throw new InvalidOperationException("La sesión es de solo lectura y no admite escrituras.");
    }
}
