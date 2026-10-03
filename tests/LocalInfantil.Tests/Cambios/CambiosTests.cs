using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Application.Contracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalInfantil.Tests.Cambios;

public sealed class CambiosTests
{
    [Fact]
    public async Task V12_se_aplica_en_base_fresca_y_define_diarios_inmutables()
    {
        var dir=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir); var path=Path.Combine(dir,"f06.db");
        try
        {
            var aplicadas=await new MigradorBaseDatos(path).MigrarAsync();
            Assert.Equal(12,aplicadas.Last());
            await using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());
            await db.OpenAsync();
            Assert.Equal("ok",await ScalarAsync(db,"PRAGMA integrity_check;"));
            Assert.Equal(0L,await ScalarLongAsync(db,"SELECT COUNT(*) FROM pragma_foreign_key_check;"));
            Assert.Equal(1L,await ScalarLongAsync(db,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='cambio';"));
            Assert.Equal(1L,await ScalarLongAsync(db,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='cambio_pago';"));
            Assert.Equal(1L,await ScalarLongAsync(db,"SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name='trg_cambio_entrada_limite_vendido';"));
            Assert.Equal(1L,await ScalarLongAsync(db,"SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name='trg_caja_movimiento_datos_coherentes';"));
        }
        finally { if(Directory.Exists(dir))Directory.Delete(dir,true); }
    }

    [Fact]
    public async Task V12_conserva_movimientos_y_operaciones_V11_y_recrea_sus_resguardos()
    {
        var dir=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var path=Path.Combine(dir,"f06-v11.db");
        try
        {
            await new MigradorBaseDatos(path).MigrarAsync(MigracionesF01.Todas.Take(11).ToArray());
            await using(var before=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString()))
            {
                await before.OpenAsync();using var cmd=before.CreateCommand();cmd.CommandText="""
                    PRAGMA foreign_keys=ON;
                    INSERT INTO usuario(id,nombre,rol,activo,creado_en) VALUES('u-v11','duena','duena',1,'2026-10-03T09:00:00Z');
                    INSERT INTO producto(id,codigo_interno,nombre,categoria_id,activo,creado_en) VALUES('p-v11','REM-001','Remera histórica','cat-rem',1,'2026-10-03T09:00:00Z');
                    INSERT INTO variante(id,producto_id,sku,talle,color,activa,creado_en,codigo_interno,talle_norm,color_norm) VALUES('v-v11','p-v11','REM-001-01','M','Rojo',1,'2026-10-03T09:00:00Z','REM-001-01','M','ROJO');
                    INSERT INTO venta(id,numero,operacion_id,estado,total_minor,moneda,creada_en,confirmada_en,creado_por_usuario_id) VALUES('t-v11','T-000001','v11-venta','confirmada',15000,'ARS','2026-10-03T09:00:00Z','2026-10-03T09:00:00Z','u-v11');
                    INSERT INTO venta_linea(id,venta_id,variante_id,cantidad,precio_unitario_minor,subtotal_minor,promocion_ref,codigo_interno,nombre_producto,talle,color) VALUES('l-v11','t-v11','v-v11',1,15000,15000,NULL,'REM-001-01','Remera histórica','M','Rojo');
                    INSERT INTO pago(id,venta_id,medio,importe_minor,recibido_minor,creado_en) VALUES('pago-v11','t-v11','debito',15000,NULL,'2026-10-03T09:00:00Z');
                    INSERT INTO caja_sesion(id,usuario_id,estado,fondo_inicial_minor,moneda,abierta_en,operacion_apertura_id) VALUES('s-v11','u-v11','abierta',0,'ARS','2026-10-03T09:00:00Z','v11-apertura');
                    INSERT INTO caja_operacion(id,operacion_id,sesion_id,tipo,usuario_id,creado_en) VALUES('o-v11','v11-movimiento','s-v11','movimiento_manual','u-v11','2026-10-03T09:01:00Z');
                    INSERT INTO caja_movimiento(id,sesion_id,tipo,importe_minor,motivo,creado_en,caja_operacion_id,medio,categoria,usuario_id)
                      VALUES('m-v11','s-v11','ingreso',12500,'Prueba de preservación','2026-10-03T09:01:00Z','o-v11','efectivo','otro_ingreso','u-v11');
                    """;await cmd.ExecuteNonQueryAsync();
            }
            var backup=Path.Combine(dir,"backups");var applied=await new MigradorBaseDatos(path).MigrarAsync(MigracionesF01.Todas,backup);
            Assert.Equal(new[]{12},applied);
            await using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());await db.OpenAsync();
            Assert.Equal("ok",await ScalarAsync(db,"PRAGMA integrity_check;"));Assert.Equal(0L,await ScalarLongAsync(db,"SELECT COUNT(*) FROM pragma_foreign_key_check;"));
            Assert.Equal(1L,await ScalarLongAsync(db,"SELECT COUNT(*) FROM venta WHERE id='t-v11' AND numero='T-000001' AND operacion_id='v11-venta' AND total_minor=15000 AND estado='confirmada';"));
            Assert.Equal(1L,await ScalarLongAsync(db,"SELECT COUNT(*) FROM venta_linea WHERE id='l-v11' AND codigo_interno='REM-001-01' AND nombre_producto='Remera histórica' AND precio_unitario_minor=15000;"));
            Assert.Equal(1L,await ScalarLongAsync(db,"SELECT COUNT(*) FROM pago WHERE id='pago-v11' AND medio='debito' AND importe_minor=15000;"));
            Assert.Equal(1L,await ScalarLongAsync(db,"SELECT COUNT(*) FROM caja_movimiento WHERE tipo='ingreso' AND importe_minor=12500 AND motivo='Prueba de preservación' AND pago_id IS NULL AND cambio_pago_id IS NULL;"));
            Assert.Equal(1L,await ScalarLongAsync(db,"SELECT COUNT(*) FROM caja_operacion WHERE operacion_id='v11-movimiento' AND tipo='movimiento_manual';"));
            Assert.Single(Directory.GetFiles(backup,"*.db"));

            using(var first=db.CreateCommand())
            {
                first.CommandText="INSERT INTO cambio(id,numero,operacion_id,venta_id,usuario_id,creado_en,importe_entradas_minor,importe_salidas_minor,diferencia_minor) VALUES('c-v12-a','C-000001','c-op-a','t-v11','u-v11','2026-10-03T09:02:00Z',15000,0,-15000); INSERT INTO cambio_entrada(id,cambio_id,venta_linea_id,variante_id,cantidad,precio_unitario_minor,apta,codigo_interno,nombre_producto,talle,color) VALUES('ce-v12-a','c-v12-a','l-v11','v-v11',1,15000,1,'REM-001-01','Remera histórica','M','Rojo');";
                await first.ExecuteNonQueryAsync();
            }
            using(var secondHeader=db.CreateCommand())
            {
                secondHeader.CommandText="INSERT INTO cambio(id,numero,operacion_id,venta_id,usuario_id,creado_en,importe_entradas_minor,importe_salidas_minor,diferencia_minor) VALUES('c-v12-b','C-000002','c-op-b','t-v11','u-v11','2026-10-03T09:03:00Z',15000,0,-15000);";
                await secondHeader.ExecuteNonQueryAsync();
            }
            using(var overReturn=db.CreateCommand())
            {
                overReturn.CommandText="INSERT INTO cambio_entrada(id,cambio_id,venta_linea_id,variante_id,cantidad,precio_unitario_minor,apta,codigo_interno,nombre_producto,talle,color) VALUES('ce-v12-b','c-v12-b','l-v11','v-v11',1,15000,1,'REM-001-01','Remera histórica','M','Rojo');";
                await Assert.ThrowsAsync<SqliteException>(()=>overReturn.ExecuteNonQueryAsync());
            }
        }
        finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection db,string sql){using var cmd=db.CreateCommand();cmd.CommandText=sql;return Convert.ToInt64(await cmd.ExecuteScalarAsync());}
    private static async Task<string> ScalarAsync(SqliteConnection db,string sql){using var cmd=db.CreateCommand();cmd.CommandText=sql;return Convert.ToString(await cmd.ExecuteScalarAsync())??string.Empty;}
}
