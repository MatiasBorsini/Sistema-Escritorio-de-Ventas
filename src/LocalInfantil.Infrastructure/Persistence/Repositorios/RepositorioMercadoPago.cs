using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioMercadoPago : IRepositorioMercadoPago
{
    private static readonly HashSet<string> NoTerminales = new(StringComparer.Ordinal)
    {
        EstadosLocalesMp.Preparada, EstadosLocalesMp.CreandoOrder, EstadosLocalesMp.EsperandoTerminal,
        EstadosLocalesMp.ProcesadaRemota, EstadosLocalesMp.ConfirmandoLocal, EstadosLocalesMp.RequiereConciliacion,
    };

    private static readonly HashSet<string> NoTerminalesReembolso = new(StringComparer.Ordinal)
    {
        EstadosLocalesReembolsoMp.Solicitada, EstadosLocalesReembolsoMp.ProcesadaRemota, EstadosLocalesReembolsoMp.RequiereConciliacion,
    };

    private readonly IGeneradorIds _ids;

    public RepositorioMercadoPago(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> InsertarOperacionAsync(ISesionPersistencia sesion, string operacionId, string idempotencyKey, string externalReference, string terminalId, long importeMinor, string cajaSesionId, string usuarioId, string creadaEn, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO mercado_pago_operacion (id, operacion_id, idempotency_key, external_reference, terminal_id, importe_minor, estado_local, caja_sesion_id, usuario_id, creada_en, actualizada_en) VALUES ($id, $op, $key, $ext, $term, $importe, 'preparada', $sesion, $usuario, $creada, $creada);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$op", operacionId);
            comando.Parameters.AddWithValue("$key", idempotencyKey);
            comando.Parameters.AddWithValue("$ext", externalReference);
            comando.Parameters.AddWithValue("$term", terminalId);
            comando.Parameters.AddWithValue("$importe", importeMinor);
            comando.Parameters.AddWithValue("$sesion", cajaSesionId);
            comando.Parameters.AddWithValue("$usuario", usuarioId);
            comando.Parameters.AddWithValue("$creada", creadaEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task InsertarLineaAsync(ISesionPersistencia sesion, string operacionMpId, string varianteId, string codigoInterno, string nombreProducto, string? talle, string? color, long cantidad, long precioUnitarioMinor, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO mercado_pago_operacion_linea (id, operacion_mp_id, variante_id, codigo_interno, nombre_producto, talle, color, cantidad, precio_unitario_minor, subtotal_minor) VALUES ($id, $op, $var, $cod, $nom, $talle, $color, $cant, $precio, $cant * $precio);";
            comando.Parameters.AddWithValue("$id", _ids.NuevoId());
            comando.Parameters.AddWithValue("$op", operacionMpId);
            comando.Parameters.AddWithValue("$var", varianteId);
            comando.Parameters.AddWithValue("$cod", codigoInterno);
            comando.Parameters.AddWithValue("$nom", nombreProducto);
            comando.Parameters.AddWithValue("$talle", (object?)talle ?? DBNull.Value);
            comando.Parameters.AddWithValue("$color", (object?)color ?? DBNull.Value);
            comando.Parameters.AddWithValue("$cant", cantidad);
            comando.Parameters.AddWithValue("$precio", precioUnitarioMinor);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task InsertarPagoPlanificadoAsync(ISesionPersistencia sesion, string operacionMpId, string medio, long importeMinor, long? recibidoMinor, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO mercado_pago_operacion_pago (id, operacion_mp_id, medio, importe_minor, recibido_minor) VALUES ($id, $op, $medio, $importe, $recibido);";
            comando.Parameters.AddWithValue("$id", _ids.NuevoId());
            comando.Parameters.AddWithValue("$op", operacionMpId);
            comando.Parameters.AddWithValue("$medio", medio);
            comando.Parameters.AddWithValue("$importe", importeMinor);
            comando.Parameters.AddWithValue("$recibido", (object?)recibidoMinor ?? DBNull.Value);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<OperacionMpRegistrada?> ObtenerOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, operacion_id, idempotency_key, external_reference, order_id, terminal_id, importe_minor, estado_local, estado_mp, venta_id, caja_sesion_id, ultimo_error, creada_en, actualizada_en FROM mercado_pago_operacion WHERE operacion_id = $op;";
            comando.Parameters.AddWithValue("$op", operacionId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken)) return null;
            return LeerOperacion(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<OperacionMpRegistrada?> ObtenerOperacionPorOrderAsync(ISesionPersistencia sesion, string orderId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, operacion_id, idempotency_key, external_reference, order_id, terminal_id, importe_minor, estado_local, estado_mp, venta_id, caja_sesion_id, ultimo_error, creada_en, actualizada_en FROM mercado_pago_operacion WHERE order_id = $order;";
            comando.Parameters.AddWithValue("$order", orderId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken)) return null;
            return LeerOperacion(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<OperacionMpRegistrada?> ObtenerOperacionPorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, operacion_id, idempotency_key, external_reference, order_id, terminal_id, importe_minor, estado_local, estado_mp, venta_id, caja_sesion_id, ultimo_error, creada_en, actualizada_en FROM mercado_pago_operacion WHERE venta_id = $venta ORDER BY creada_en DESC LIMIT 1;";
            comando.Parameters.AddWithValue("$venta", ventaId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken)) return null;
            return LeerOperacion(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<OperacionMpRegistrada>> ListarNoTerminalesAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            var estados = string.Join(",", NoTerminales.Select(e => $"'{e}'"));
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = $"SELECT id, operacion_id, idempotency_key, external_reference, order_id, terminal_id, importe_minor, estado_local, estado_mp, venta_id, caja_sesion_id, ultimo_error, creada_en, actualizada_en FROM mercado_pago_operacion WHERE estado_local IN ({estados}) ORDER BY creada_en;";
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<OperacionMpRegistrada>();
            while (await lector.ReadAsync(cancellationToken)) resultado.Add(LeerOperacion(lector));
            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<OperacionMpRegistrada>> ListarPendientesPorSesionAsync(ISesionPersistencia sesion, string cajaSesionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            var estados = string.Join(",", NoTerminales.Select(e => $"'{e}'"));
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = $"SELECT id, operacion_id, idempotency_key, external_reference, order_id, terminal_id, importe_minor, estado_local, estado_mp, venta_id, caja_sesion_id, ultimo_error, creada_en, actualizada_en FROM mercado_pago_operacion WHERE caja_sesion_id = $sesion AND estado_local IN ({estados}) ORDER BY creada_en;";
            comando.Parameters.AddWithValue("$sesion", cajaSesionId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<OperacionMpRegistrada>();
            while (await lector.ReadAsync(cancellationToken)) resultado.Add(LeerOperacion(lector));
            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<(string VarianteId, long Cantidad, long PrecioUnitarioMinor, string CodigoInterno, string NombreProducto, string? Talle, string? Color)>> ListarLineasAsync(ISesionPersistencia sesion, string operacionMpId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT variante_id, cantidad, precio_unitario_minor, codigo_interno, nombre_producto, talle, color FROM mercado_pago_operacion_linea WHERE operacion_mp_id = $op ORDER BY id;";
            comando.Parameters.AddWithValue("$op", operacionMpId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<(string, long, long, string, string, string?, string?)>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add((lector.GetString(0), lector.GetInt64(1), lector.GetInt64(2), lector.GetString(3), lector.GetString(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5), lector.IsDBNull(6) ? null : lector.GetString(6)));
            }
            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<(string Medio, long ImporteMinor, long? RecibidoMinor)>> ListarPagosPlanificadosAsync(ISesionPersistencia sesion, string operacionMpId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT medio, importe_minor, recibido_minor FROM mercado_pago_operacion_pago WHERE operacion_mp_id = $op ORDER BY id;";
            comando.Parameters.AddWithValue("$op", operacionMpId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<(string, long, long?)>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add((lector.GetString(0), lector.GetInt64(1), lector.IsDBNull(2) ? null : lector.GetInt64(2)));
            }
            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task ActualizarEstadoAsync(ISesionPersistencia sesion, string id, string estadoLocal, string? estadoMp, string? orderId, string? ventaId, string? ultimoError, string actualizadaEn, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE mercado_pago_operacion SET estado_local = $estado, estado_mp = COALESCE($mp, estado_mp), order_id = COALESCE($order, order_id), venta_id = COALESCE($venta, venta_id), ultimo_error = $error, actualizada_en = $actualizada WHERE id = $id;";
            comando.Parameters.AddWithValue("$estado", estadoLocal);
            comando.Parameters.AddWithValue("$mp", (object?)estadoMp ?? DBNull.Value);
            comando.Parameters.AddWithValue("$order", (object?)orderId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$venta", (object?)ventaId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$error", (object?)ultimoError ?? DBNull.Value);
            comando.Parameters.AddWithValue("$actualizada", actualizadaEn);
            comando.Parameters.AddWithValue("$id", id);
            if (await comando.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("La operación de Mercado Pago no existe.");
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<string> InsertarReembolsoAsync(ISesionPersistencia sesion, string? operacionMpId, string ventaId, string pagoId, string reembolsoOperacionId, string idempotencyKey, string orderId, long importeMinor, string creadoEn, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO mercado_pago_reembolso (id, operacion_mp_id, venta_id, pago_id, reembolso_operacion_id, idempotency_key, order_id, importe_minor, estado_local, creado_en, actualizado_en) VALUES ($id, $op, $venta, $pago, $reop, $key, $order, $importe, 'solicitada', $creado, $creado);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$op", (object?)operacionMpId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$venta", ventaId);
            comando.Parameters.AddWithValue("$pago", pagoId);
            comando.Parameters.AddWithValue("$reop", reembolsoOperacionId);
            comando.Parameters.AddWithValue("$key", idempotencyKey);
            comando.Parameters.AddWithValue("$order", orderId);
            comando.Parameters.AddWithValue("$importe", importeMinor);
            comando.Parameters.AddWithValue("$creado", creadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<ReembolsoMpRegistrado?> ObtenerReembolsoAsync(ISesionPersistencia sesion, string reembolsoOperacionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, operacion_mp_id, venta_id, pago_id, reembolso_operacion_id, idempotency_key, order_id, importe_minor, estado_local, estado_mp, ultimo_error FROM mercado_pago_reembolso WHERE reembolso_operacion_id = $reop;";
            comando.Parameters.AddWithValue("$reop", reembolsoOperacionId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken)) return null;
            return LeerReembolso(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<ReembolsoMpRegistrado>> ListarReembolsosNoTerminalesAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            var estados = string.Join(",", NoTerminalesReembolso.Select(e => $"'{e}'"));
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = $"SELECT id, operacion_mp_id, venta_id, pago_id, reembolso_operacion_id, idempotency_key, order_id, importe_minor, estado_local, estado_mp, ultimo_error FROM mercado_pago_reembolso WHERE estado_local IN ({estados}) ORDER BY creado_en;";
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<ReembolsoMpRegistrado>();
            while (await lector.ReadAsync(cancellationToken)) resultado.Add(LeerReembolso(lector));
            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<ReembolsoMpRegistrado>> ListarReembolsosPorPagoAsync(ISesionPersistencia sesion, string pagoId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, operacion_mp_id, venta_id, pago_id, reembolso_operacion_id, idempotency_key, order_id, importe_minor, estado_local, estado_mp, ultimo_error FROM mercado_pago_reembolso WHERE pago_id = $pago ORDER BY creado_en;";
            comando.Parameters.AddWithValue("$pago", pagoId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<ReembolsoMpRegistrado>();
            while (await lector.ReadAsync(cancellationToken)) resultado.Add(LeerReembolso(lector));
            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<ReembolsoMpRegistrado>> ListarReembolsosPorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, operacion_mp_id, venta_id, pago_id, reembolso_operacion_id, idempotency_key, order_id, importe_minor, estado_local, estado_mp, ultimo_error FROM mercado_pago_reembolso WHERE venta_id = $venta ORDER BY creado_en;";
            comando.Parameters.AddWithValue("$venta", ventaId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<ReembolsoMpRegistrado>();
            while (await lector.ReadAsync(cancellationToken)) resultado.Add(LeerReembolso(lector));
            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task ActualizarReembolsoAsync(ISesionPersistencia sesion, string id, string estadoLocal, string? estadoMp, string? ultimoError, string actualizadoEn, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE mercado_pago_reembolso SET estado_local = $estado, estado_mp = COALESCE($mp, estado_mp), ultimo_error = $error, actualizado_en = $actualizado WHERE id = $id;";
            comando.Parameters.AddWithValue("$estado", estadoLocal);
            comando.Parameters.AddWithValue("$mp", (object?)estadoMp ?? DBNull.Value);
            comando.Parameters.AddWithValue("$error", (object?)ultimoError ?? DBNull.Value);
            comando.Parameters.AddWithValue("$actualizado", actualizadoEn);
            comando.Parameters.AddWithValue("$id", id);
            if (await comando.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("El reembolso no existe.");
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    private static OperacionMpRegistrada LeerOperacion(SqliteDataReader lector) => new(
        lector.GetString(0), lector.GetString(1), lector.GetString(2), lector.GetString(3),
        lector.IsDBNull(4) ? null : lector.GetString(4), lector.GetString(5), lector.GetInt64(6),
        lector.GetString(7), lector.IsDBNull(8) ? null : lector.GetString(8),
        lector.IsDBNull(9) ? null : lector.GetString(9), lector.GetString(10),
        lector.IsDBNull(11) ? null : lector.GetString(11), lector.GetString(12), lector.GetString(13));

    private static ReembolsoMpRegistrado LeerReembolso(SqliteDataReader lector) => new(
        lector.GetString(0), lector.IsDBNull(1) ? null : lector.GetString(1), lector.GetString(2),
        lector.GetString(3), lector.GetString(4), lector.GetString(5), lector.GetString(6),
        lector.GetInt64(7), lector.GetString(8), lector.IsDBNull(9) ? null : lector.GetString(9),
        lector.IsDBNull(10) ? null : lector.GetString(10));

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
