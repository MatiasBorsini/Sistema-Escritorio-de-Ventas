using LocalInfantil.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>F12-C: V18 aditiva. V17 con datos → V18 intacta; V18 nueva desde cero.</summary>
public sealed class MigracionV18Tests
{
    private static async Task<string> BaseV17ConDatosAsync(string dir)
    {
        var ruta = Path.Combine(dir, "v17.db");
        var hastaV17 = MigracionesF01.Todas.Where(m => m.Version <= 17).ToList();
        await new MigradorBaseDatos(ruta).MigrarAsync(hastaV17, null);
        await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
        await conexion.OpenAsync();
        await EjecutarAsync(conexion, "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u1','duena','duena','2026-01-01T00:00:00Z');");
        await EjecutarAsync(conexion, "INSERT INTO caja_sesion (id, usuario_id, estado, fondo_inicial_minor, abierta_en, operacion_apertura_id) VALUES ('s1','u1','abierta',0,'2026-01-01T00:00:00Z','op-ap1');");
        await EjecutarAsync(conexion, "INSERT INTO venta (id, numero, operacion_id, estado, total_minor, moneda, creada_en, confirmada_en) VALUES ('v1','T-000001','op1','confirmada',10000,'ARS','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');");
        await EjecutarAsync(conexion, "INSERT INTO pago (id, venta_id, medio, importe_minor, creado_en, recibido_minor) VALUES ('p1','v1','efectivo',10000,'2026-01-01T00:00:00Z',10000);");
        return ruta;
    }

    private static async Task EjecutarAsync(SqliteConnection conexion, string sql)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> ContarAsync(string ruta, string sql)
    {
        await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
        await conexion.OpenAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private const string InsertarImportacion =
        "INSERT INTO mercado_pago_importacion (id, origen, archivo_nombre, archivo_hash, estado, usuario_id, creado_en) " +
        "VALUES ('imp1','archivo','r.csv',lower(hex(randomblob(32))),'aplicado','u1','2026-01-02T00:00:00Z');";

    private const string InsertarLiquidacion =
        "INSERT INTO mercado_pago_liquidacion (id, importacion_id, fila_hash, fuente, source_id, record_type, descripcion, clasificacion, gross_minor, net_credito_minor, net_debito_minor, estado_conciliacion, creado_en) " +
        "VALUES ('liq1','imp1',lower(hex(randomblob(32))),'archivo','PAY1','release','payment','cobro',30000,28500,0,'pendiente','2026-01-02T00:00:00Z');";

    [Fact]
    public async Task V17_con_datos_migra_a_V18_intacta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ruta = await BaseV17ConDatosAsync(dir);
            var hastaV18 = MigracionesF01.Todas.Where(m => m.Version <= 18).ToList();
            var aplicadas = await new MigradorBaseDatos(ruta).MigrarAsync(hastaV18, Path.Combine(dir, "respaldos"));
            Assert.Contains(18, aplicadas);

            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM venta;"));
            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM pago;"));
            Assert.Equal(18, await ContarAsync(ruta, "SELECT MAX(version) FROM schema_migrations;"));
            Assert.Equal(0, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));

            await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
            await conexion.OpenAsync();
            await EjecutarAsync(conexion, InsertarImportacion);
            await EjecutarAsync(conexion, InsertarLiquidacion);
            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));

            var borrar = await Assert.ThrowsAsync<SqliteException>(() => EjecutarAsync(conexion, "DELETE FROM mercado_pago_liquidacion WHERE id='liq1';"));
            Assert.Contains("historica", borrar.Message);

            var editar = await Assert.ThrowsAsync<SqliteException>(() => EjecutarAsync(conexion, "UPDATE mercado_pago_liquidacion SET gross_minor=1 WHERE id='liq1';"));
            Assert.Contains("inmutable", editar.Message);

            await EjecutarAsync(conexion, "UPDATE mercado_pago_liquidacion SET estado_conciliacion='conciliada' WHERE id='liq1';");
            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_liquidacion WHERE estado_conciliacion='conciliada';"));

            await EjecutarAsync(conexion, "INSERT INTO mercado_pago_conciliacion (id, liquidacion_id, venta_id, pago_id, tipo_match, creado_en) VALUES ('con1','liq1','v1','p1','asistido_humano','2026-01-02T00:00:00Z');");
            var conciliarEditar = await Assert.ThrowsAsync<SqliteException>(() => EjecutarAsync(conexion, "UPDATE mercado_pago_conciliacion SET tipo_match='exacto_order' WHERE id='con1';"));
            Assert.Contains("inmutable", conciliarEditar.Message);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task V18_fila_hash_era_unico_global_se_congela()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ruta = await BaseV17ConDatosAsync(dir);
            var hastaV18 = MigracionesF01.Todas.Where(m => m.Version <= 18).ToList();
            await new MigradorBaseDatos(ruta).MigrarAsync(hastaV18, Path.Combine(dir, "respaldos"));
            await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
            await conexion.OpenAsync();
            await EjecutarAsync(conexion, InsertarImportacion);
            await EjecutarAsync(conexion, "INSERT INTO mercado_pago_liquidacion (id, importacion_id, fila_hash, fuente, record_type, descripcion, clasificacion, estado_conciliacion, creado_en) VALUES ('liqA','imp1','" + new string('a', 64) + "','archivo','release','payment','cobro','pendiente','2026-01-02T00:00:00Z');");
            await EjecutarAsync(conexion, "INSERT OR IGNORE INTO mercado_pago_liquidacion (id, importacion_id, fila_hash, fuente, record_type, descripcion, clasificacion, estado_conciliacion, creado_en) VALUES ('liqB','imp1','" + new string('a', 64) + "','archivo','release','payment','cobro','pendiente','2026-01-02T00:00:00Z');");
            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task V18_nueva_desde_cero()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var ruta = Path.Combine(dir, "nueva.db");
            var aplicadas = await new MigradorBaseDatos(ruta).MigrarAsync();
            Assert.Contains(18, aplicadas);
            Assert.Equal(0, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_importacion;"));
            Assert.Equal(0, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
            Assert.Equal(0, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_conciliacion;"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
