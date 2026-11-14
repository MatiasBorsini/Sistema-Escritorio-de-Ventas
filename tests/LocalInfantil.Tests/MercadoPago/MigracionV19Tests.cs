using LocalInfantil.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>
/// F12-B (D-50): V19 reconstruye mercado_pago_liquidacion para quitar el
/// UNIQUE global de fila_hash. La identidad de fila pasa a ser
/// (importacion_id, fila_ordinal) y fila_hash queda como huella de contenido no
/// única. Debe preservar filas y conciliaciones existentes, reconstruir índices
/// y triggers, y dejar la integridad referencial intacta.
/// </summary>
public sealed class MigracionV19Tests
{
    private static async Task<string> BaseV18ConDatosAsync(string dir)
    {
        var ruta = Path.Combine(dir, "v18.db");
        var hastaV18 = MigracionesF01.Todas.Where(m => m.Version <= 18).ToList();
        await new MigradorBaseDatos(ruta).MigrarAsync(hastaV18, null);
        await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
        await conexion.OpenAsync();
        await EjecutarAsync(conexion, "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u1','duena','duena','2026-01-01T00:00:00Z');");
        await EjecutarAsync(conexion, "INSERT INTO caja_sesion (id, usuario_id, estado, fondo_inicial_minor, abierta_en, operacion_apertura_id) VALUES ('s1','u1','abierta',0,'2026-01-01T00:00:00Z','op-ap1');");
        await EjecutarAsync(conexion, "INSERT INTO venta (id, numero, operacion_id, estado, total_minor, moneda, creada_en, confirmada_en) VALUES ('v1','T-000001','op1','confirmada',30000,'ARS','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');");
        await EjecutarAsync(conexion, "INSERT INTO pago (id, venta_id, medio, importe_minor, creado_en) VALUES ('p1','v1','mercado_pago',30000,'2026-01-01T00:00:00Z');");
        await EjecutarAsync(conexion, "INSERT INTO mercado_pago_importacion (id, origen, archivo_nombre, archivo_hash, estado, usuario_id, creado_en) VALUES ('imp1','archivo','r.csv',lower(hex(randomblob(32))),'aplicado','u1','2026-01-02T00:00:00Z');");
        // V18: fila sin ordinal ni identidad_remota (columnas que aún no existen).
        await EjecutarAsync(conexion,
            "INSERT INTO mercado_pago_liquidacion (id, importacion_id, fila_hash, fuente, source_id, record_type, descripcion, clasificacion, external_reference, gross_minor, net_credito_minor, net_debito_minor, estado_conciliacion, venta_id, pago_id, creado_en) " +
            "VALUES ('liq1','imp1','" + new string('a', 64) + "','archivo','PAY1','release','payment','cobro','LI-op1',30000,28500,0,'conciliada','v1','p1','2026-01-02T00:00:00Z');");
        await EjecutarAsync(conexion,
            "INSERT INTO mercado_pago_conciliacion (id, liquidacion_id, venta_id, pago_id, tipo_match, creado_en) VALUES ('con1','liq1','v1','p1','exacto_external_ref','2026-01-02T00:00:00Z');");
        return ruta;
    }

    private static async Task EjecutarAsync(SqliteConnection conexion, string sql)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> ContarAsync(SqliteConnection conexion, string sql)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<long> ContarAsync(string ruta, string sql)
    {
        await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
        await conexion.OpenAsync();
        return await ContarAsync(conexion, sql);
    }

    private static async Task<object?> EscalarAsync(SqliteConnection conexion, string sql)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    [Fact]
    public async Task V18_con_datos_migra_a_V19_preservando_filas_y_conciliaciones()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ruta = await BaseV18ConDatosAsync(dir);
            var aplicadas = await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, Path.Combine(dir, "respaldos"));
            Assert.Contains(19, aplicadas);

            await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
            await conexion.OpenAsync();

            // Datos preservados y claves/relaciones intactas.
            Assert.Equal(19, await ContarAsync(conexion, "SELECT MAX(version) FROM schema_migrations;"));
            Assert.Equal(1, await ContarAsync(conexion, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
            Assert.Equal(1, await ContarAsync(conexion, "SELECT COUNT(*) FROM mercado_pago_conciliacion;"));
            Assert.Equal(1, await ContarAsync(conexion, "SELECT fila_ordinal FROM mercado_pago_liquidacion WHERE id='liq1';"));
            Assert.Equal("C:PAY1", (string)(await EscalarAsync(conexion, "SELECT identidad_remota FROM mercado_pago_liquidacion WHERE id='liq1';"))!);
            Assert.Equal("con1", (string)(await EscalarAsync(conexion, "SELECT id FROM mercado_pago_conciliacion WHERE liquidacion_id='liq1';"))!);

            // Integridad.
            Assert.Equal("ok", (string)(await EscalarAsync(conexion, "PRAGMA integrity_check;"))!);
            Assert.Equal(0, await ContarAsync(conexion, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));

            // Ahora dos filas con contenido idéntico sí conviven (por ordinal).
            await EjecutarAsync(conexion,
                "INSERT INTO mercado_pago_liquidacion (id, importacion_id, fila_ordinal, fila_hash, identidad_remota, fuente, record_type, descripcion, clasificacion, external_reference, gross_minor, net_credito_minor, net_debito_minor, estado_conciliacion, creado_en) " +
                "VALUES ('liq2','imp1',2,'" + new string('a', 64) + "','C:PAY2','archivo','release','payment','cobro','LI-op2',30000,28500,0,'pendiente','2026-01-02T00:00:00Z');");
            Assert.Equal(2, await ContarAsync(conexion, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));

            // Ordinal repetido dentro de la importación: rechazado.
            var ordinalDuplicado = await Assert.ThrowsAsync<SqliteException>(() => EjecutarAsync(conexion,
                "INSERT INTO mercado_pago_liquidacion (id, importacion_id, fila_ordinal, fila_hash, fuente, record_type, descripcion, clasificacion, estado_conciliacion, creado_en) " +
                "VALUES ('liq3','imp1',1,'" + new string('b', 64) + "','archivo','release','payment','cobro','pendiente','2026-01-02T00:00:00Z');"));
            Assert.Contains("UNIQUE", ordinalDuplicado.Message, StringComparison.OrdinalIgnoreCase);

            // Triggers reconstruidos: inmutable y no borrable.
            var editar = await Assert.ThrowsAsync<SqliteException>(() => EjecutarAsync(conexion, "UPDATE mercado_pago_liquidacion SET gross_minor=1 WHERE id='liq1';"));
            Assert.Contains("inmutable", editar.Message);
            var borrar = await Assert.ThrowsAsync<SqliteException>(() => EjecutarAsync(conexion, "DELETE FROM mercado_pago_liquidacion WHERE id='liq1';"));
            Assert.Contains("historica", borrar.Message);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task V19_nueva_permite_filas_identicas_y_dedup_por_identidad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var ruta = Path.Combine(dir, "v19.db");
            var aplicadas = await new MigradorBaseDatos(ruta).MigrarAsync();
            Assert.Contains(19, aplicadas);

            await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
            await conexion.OpenAsync();
            await EjecutarAsync(conexion, "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u1','duena','duena','2026-01-01T00:00:00Z');");
            await EjecutarAsync(conexion, "INSERT INTO mercado_pago_importacion (id, origen, archivo_nombre, archivo_hash, estado, usuario_id, creado_en) VALUES ('imp1','archivo','r.csv',lower(hex(randomblob(32))),'aplicado','u1','2026-01-02T00:00:00Z');");

            const string filaHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
            await EjecutarAsync(conexion,
                "INSERT INTO mercado_pago_liquidacion (id, importacion_id, fila_ordinal, fila_hash, fuente, record_type, descripcion, clasificacion, gross_minor, estado_conciliacion, creado_en) " +
                $"VALUES ('liqA','imp1',1,'{filaHash}','archivo','release','payment','cobro',30000,'pendiente','2026-01-02T00:00:00Z');");
            await EjecutarAsync(conexion,
                "INSERT INTO mercado_pago_liquidacion (id, importacion_id, fila_ordinal, fila_hash, fuente, record_type, descripcion, clasificacion, gross_minor, estado_conciliacion, creado_en) " +
                $"VALUES ('liqB','imp1',2,'{filaHash}','archivo','release','payment','cobro',30000,'pendiente','2026-01-02T00:00:00Z');");
            Assert.Equal(2, await ContarAsync(conexion, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
