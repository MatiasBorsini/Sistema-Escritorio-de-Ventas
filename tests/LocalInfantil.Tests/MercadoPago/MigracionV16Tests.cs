using LocalInfantil.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>F12 §52: V15 con datos → backup → V16 intacta; V16 nueva desde cero.</summary>
public sealed class MigracionV16Tests
{
    private static async Task<string> BaseV15ConDatosAsync(string dir)
    {
        var ruta = Path.Combine(dir, "v15.db");
        var parcial = MigracionesF01.Todas.Where(m => m.Version <= 15).ToList();
        await new MigradorBaseDatos(ruta).MigrarAsync(parcial, null);
        await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
        await conexion.OpenAsync();
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = OFF;";
            await cmd.ExecuteNonQueryAsync();
        }
        await EjecutarAsync(conexion, "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u1','duena','duena','2026-01-01T00:00:00Z');");
        await EjecutarAsync(conexion, "INSERT INTO caja_sesion (id, usuario_id, estado, fondo_inicial_minor, abierta_en, operacion_apertura_id) VALUES ('s1','u1','abierta',0,'2026-01-01T00:00:00Z','op-ap1');");
        await EjecutarAsync(conexion, "INSERT INTO venta (id, numero, operacion_id, estado, total_minor, moneda, creada_en, confirmada_en) VALUES ('v1','T-000001','op1','confirmada',10000,'ARS','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');");
        await EjecutarAsync(conexion, "INSERT INTO pago (id, venta_id, medio, importe_minor, creado_en, recibido_minor) VALUES ('p1','v1','efectivo',10000,'2026-01-01T00:00:00Z',10000);");
        await EjecutarAsync(conexion, "INSERT INTO caja_operacion (id, operacion_id, sesion_id, tipo, usuario_id, creado_en) VALUES ('o1','op1','s1','venta','u1','2026-01-01T00:00:00Z');");
        await EjecutarAsync(conexion, "INSERT INTO caja_movimiento (id, sesion_id, tipo, importe_minor, venta_id, creado_en, caja_operacion_id, pago_id, medio, usuario_id) VALUES ('m1','s1','venta',10000,'v1','2026-01-01T00:00:00Z','o1','p1','efectivo','u1');");
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

    [Fact]
    public async Task V15_con_datos_migra_a_V16_intacta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ruta = await BaseV15ConDatosAsync(dir);
            var dirRespaldos = Path.Combine(dir, "respaldos");
            var aplicadas = await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, dirRespaldos);
            Assert.Contains(16, aplicadas);

            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM venta;"));
            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM pago;"));
            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM caja_movimiento;"));
            Assert.Equal(16, await ContarAsync(ruta, "SELECT MAX(version) FROM schema_migrations;"));

            await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
            await conexion.OpenAsync();
            await EjecutarAsync(conexion, "INSERT INTO pago (id, venta_id, medio, importe_minor, creado_en) VALUES ('p2','v1','mercado_pago',5000,'2026-01-02T00:00:00Z');");
            await EjecutarAsync(conexion, "INSERT INTO mercado_pago_operacion (id, operacion_id, idempotency_key, external_reference, terminal_id, importe_minor, estado_local, caja_sesion_id, usuario_id, creada_en, actualizada_en) VALUES ('mpo1','op-mp','key1','LI-op-mp','TERM__SN1',5000,'preparada','s1','u1','2026-01-02T00:00:00Z','2026-01-02T00:00:00Z');");
            Assert.Equal(1, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_operacion;"));

            var ex = await Assert.ThrowsAsync<SqliteException>(() => EjecutarAsync(conexion, "DELETE FROM mercado_pago_operacion WHERE id='mpo1';"));
            Assert.Contains("historica", ex.Message);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task V16_nueva_desde_cero()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var ruta = Path.Combine(dir, "nueva.db");
            var aplicadas = await new MigradorBaseDatos(ruta).MigrarAsync();
            Assert.Contains(16, aplicadas);
            Assert.Equal(0, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_operacion;"));
            Assert.Equal(0, await ContarAsync(ruta, "SELECT COUNT(*) FROM mercado_pago_reembolso;"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
