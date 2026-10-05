using System.Security.Cryptography;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Respaldos;
using LocalInfantil.Infrastructure.Tiempo;
using LocalInfantil.Tests.Ventas;
using LocalInfantil.Wpf.Infra;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Respaldos;

public sealed class IntercambioRestauracionTests
{
    [Fact]
    public async Task Reemplazo_correcto_verifica_staging_y_conserva_backup_previo()
    {
        var root=CrearRoot();try{var active=Path.Combine(root,"active.db");var staged=Path.Combine(root,"staged.db");await DbAsync(active,"Antes");await DbAsync(staged,"Restaurada");var rollback=Path.Combine(root,"active.pre.db");
            var result=await IntercambiadorRestauracion.ReemplazarBaseVerificadaAsync(active,staged,rollback);Assert.Equal(rollback,result);Assert.Equal("Restaurada",await UserAsync(active));Assert.Equal("Antes",await UserAsync(rollback));
        }finally{Limpiar(root);}
    }

    [Fact]
    public async Task Fallo_antes_y_durante_reemplazo_restauran_base_anterior()
    {
        var root=CrearRoot();try
        {
            var active=Path.Combine(root,"active.db");var staged=Path.Combine(root,"staged.db");await DbAsync(active,"Antes");await DbAsync(staged,"Restaurada");var hash=await HashAsync(active);
            await Assert.ThrowsAsync<IOException>(()=>IntercambiadorRestauracion.ReemplazarBaseVerificadaAsync(active,staged,Path.Combine(root,"before.db"),replace:(_,_)=>throw new IOException("fallo previo")));
            Assert.Equal(hash,await HashAsync(active));
            void ReemplazarYLanzar(string source,string target){File.Replace(source,target,null);throw new IOException("fallo posterior a swap");}
            await Assert.ThrowsAsync<IOException>(()=>IntercambiadorRestauracion.ReemplazarBaseVerificadaAsync(active,staged,Path.Combine(root,"during.db"),replace:ReemplazarYLanzar));Assert.Equal("Antes",await UserAsync(active));
        }finally{Limpiar(root);}
    }

    [Fact]
    public async Task Staging_corrupto_formato_incompatible_y_clave_incorrecta_no_tocan_base_activa()
    {
        var root=CrearRoot();try
        {
            var active=Path.Combine(root,"active.db");await DbAsync(active,"Antes");var hash=await HashAsync(active);var corrupt=Path.Combine(root,"corrupt.db");await File.WriteAllTextAsync(corrupt,"no sqlite");
            await Assert.ThrowsAnyAsync<Exception>(()=>IntercambiadorRestauracion.ReemplazarBaseVerificadaAsync(active,corrupt,Path.Combine(root,"rollback-corrupt.db")));Assert.Equal(hash,await HashAsync(active));
            var manager=new GestorRespaldosLocales(active,Path.Combine(root,"Backups"));var code=manager.CrearCodigoRecuperacion();manager.ConfirmarCodigoRecuperacion(code);var backup=await manager.CrearAsync(MotivoRespaldo.Manual);
            var otherKey=CodigoRecuperacion.Codificar(RandomNumberGenerator.GetBytes(32));await Assert.ThrowsAnyAsync<Exception>(()=>manager.PrepararRestauracionAsync(backup.Manifiesto.RutaLocal,otherKey));Assert.Equal(hash,await HashAsync(active));
            var badVersion=Path.Combine(root,"future.libackup");File.Copy(backup.Manifiesto.RutaLocal,badVersion);using(var fs=new FileStream(badVersion,FileMode.Open,FileAccess.Write,FileShare.None)){fs.Position=8;fs.WriteByte(255);}
            await Assert.ThrowsAsync<InvalidDataException>(()=>manager.PrepararRestauracionAsync(badVersion,code));Assert.Equal(hash,await HashAsync(active));
        }finally{Limpiar(root);}
    }

    [Fact]
    public async Task Caja_abierta_rechaza_validacion_antes_del_intercambio()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var hash=await HashAsync(e.Servicios.RutaBaseDatos);await Assert.ThrowsAsync<InvalidOperationException>(()=>IntercambiadorRestauracion.ValidarCajaCerradaAsync(e.Servicios.RutaBaseDatos));Assert.Equal(hash,await HashAsync(e.Servicios.RutaBaseDatos));
    }

    private static string CrearRoot(){var p=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    private static void Limpiar(string root){try{SqliteConnection.ClearAllPools();Directory.Delete(root,true);}catch(IOException){}}
    private static async Task DbAsync(string path,string name){await new MigradorBaseDatos(path).MigrarAsync(MigracionesF01.Todas);await using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());await db.OpenAsync();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO usuario(id,nombre,rol,activo,creado_en,huella_contrasena) VALUES($id,$name,'duena',1,'2026-10-05','hash');";cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));cmd.Parameters.AddWithValue("$name",name);await cmd.ExecuteNonQueryAsync();}
    private static async Task<string> UserAsync(string path){await using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());await db.OpenAsync();using var cmd=db.CreateCommand();cmd.CommandText="SELECT nombre FROM usuario LIMIT 1;";return Convert.ToString(await cmd.ExecuteScalarAsync())!;}
    private static async Task<string> HashAsync(string path){await using var s=File.OpenRead(path);return Convert.ToHexString(await SHA256.HashDataAsync(s));}
}
