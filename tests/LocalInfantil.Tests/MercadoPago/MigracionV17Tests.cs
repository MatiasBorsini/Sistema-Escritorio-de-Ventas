using LocalInfantil.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>F12-B: V17 aditiva. V16 con datos → V17 intacta; V17 nueva desde cero.</summary>
public sealed class MigracionV17Tests
{
    private static async Task<string> BaseV16ConDatosAsync(string dir)
    {
        var ruta = Path.Combine(dir, "v16.db");
        var hastaV16 = MigracionesF01.Todas.Where(m => m.Version <= 16).ToList();
        await new MigradorBaseDatos(ruta).MigrarAsync(hastaV16, null);
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

    private const string InsertarCobroManual =
        "INSERT INTO mercado_pago_cobro_manual (id, operacion_id, venta_id, pago_id, importe_minor, comprobante_ref, terminal_id, pos_id, declarado_en, estado_conciliacion, usuario_id, caja_sesion_id, creado_en, actualizado_en) " +
        "VALUES ('cm1','op-mp','v1','p1',10000,'0001','TERM__SN1','P1','2026-01-02T00:00:00Z','pendiente','u1','s1','2026-01-02T00:00:00Z','2026-01-02T00:00:00Z');";

    [Fact]
    public async Task V16_con_datos_migra_a_V17_intacta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ruta = await BaseV16ConDatosAsync(dir);
            var aplicadas = await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, Path.Combine(dir, "respaldos"));
            Assert.Contains(17, aplicadas);

            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM venta;"));
            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM pago;"));
            Assert.Equal(19, await ContarAsync(ruta, "SELECT MAX(version) FROM schema_migrations;"));
            Assert.Equal(0, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));

            await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
            await conexion.OpenAsync();
            await EjecutarAsync(conexion, InsertarCobroManual);
            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));

            var borrar = await Assert.ThrowsAsync<SqliteException>(() => EjecutarAsync(conexion, "DELETE FROM mercado_pago_cobro_manual WHERE id='cm1';"));
            Assert.Contains("historico", borrar.Message);

            var editar = await Assert.ThrowsAsync<SqliteException>(() => EjecutarAsync(conexion, "UPDATE mercado_pago_cobro_manual SET importe_minor=1 WHERE id='cm1';"));
            Assert.Contains("datos base", editar.Message);

            await EjecutarAsync(conexion, "UPDATE mercado_pago_cobro_manual SET estado_conciliacion='conciliada' WHERE id='cm1';");
            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_cobro_manual WHERE estado_conciliacion='conciliada';"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task V17_nueva_desde_cero()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var ruta = Path.Combine(dir, "nueva.db");
            var aplicadas = await new MigradorBaseDatos(ruta).MigrarAsync();
            Assert.Contains(17, aplicadas);
            Assert.Equal(0, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
