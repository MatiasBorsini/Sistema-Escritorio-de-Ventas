using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Infrastructure.Persistence.Respaldos;
using LocalInfantil.Infrastructure.Persistence.Migrations;

namespace LocalInfantil.Wpf.Infra;

/// <summary>Proceso auxiliar sin UI que espera el cierre total de WPF antes del reemplazo atómico.</summary>
public static class IntercambiadorRestauracion
{
    public static void Iniciar(string rutaBaseDatos,string staging)
    {
        var exe=Environment.ProcessPath??throw new InvalidOperationException("No se pudo localizar el ejecutable de LocalInfantil.");
        var rollback=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(rutaBaseDatos))!,Path.GetFileName(rutaBaseDatos)+$".pre-restore-{Guid.NewGuid():N}.db");
        var marker=Path.Combine(Path.GetTempPath(),$"LocalInfantil-restore-{Guid.NewGuid():N}.status");
        var start=new ProcessStartInfo(exe){UseShellExecute=false};start.ArgumentList.Add("--restore-swap");start.ArgumentList.Add(Path.GetFullPath(rutaBaseDatos));start.ArgumentList.Add(Path.GetFullPath(staging));start.ArgumentList.Add(Environment.ProcessId.ToString());start.ArgumentList.Add(rollback);start.ArgumentList.Add(marker);
        using var process=Process.Start(start)??throw new InvalidOperationException("No se pudo iniciar el proceso seguro de restauración.");
        System.Windows.Application.Current.Shutdown(0);
    }

    public static async Task EjecutarHelperAsync(string[] args,CancellationToken ct=default)
    {
        if(args.Length<6)throw new ArgumentException("Argumentos de restauración incompletos.");
        var db=Path.GetFullPath(args[1]);var staged=Path.GetFullPath(args[2]);var parent=int.Parse(args[3]);var rollback=Path.GetFullPath(args[4]);var marker=Path.GetFullPath(args[5]);
        try{using var parentProcess=Process.GetProcessById(parent);await parentProcess.WaitForExitAsync(ct);}catch(ArgumentException){}
        var switched=false;var restauracionComprobada=false;Process? restoredProcess=null;
        try
        {
            await ValidarCajaCerradaAsync(db,ct);rollback=await ReemplazarBaseVerificadaAsync(db,staged,rollback,ct);switched=true;
            var exe=Environment.ProcessPath??throw new InvalidOperationException("No se localizó el ejecutable.");var start=new ProcessStartInfo(exe){UseShellExecute=false};start.ArgumentList.Add("--dev-db");start.ArgumentList.Add(db);start.ArgumentList.Add("--restore-verify");start.ArgumentList.Add(marker);restoredProcess=Process.Start(start);
            var deadline=DateTime.UtcNow.AddSeconds(60);while(DateTime.UtcNow<deadline&&!File.Exists(marker))await Task.Delay(250,ct);
            var result=File.Exists(marker)?await File.ReadAllTextAsync(marker,ct):"ERROR timeout";
            if(result.StartsWith("OK",StringComparison.Ordinal)){restauracionComprobada=true;return;}
            if(restoredProcess is { HasExited:false }){try{restoredProcess.Kill(true);await restoredProcess.WaitForExitAsync(ct);}catch{} }
            if(switched&&File.Exists(rollback)){var temp=db+".rollback.tmp";File.Copy(rollback,temp,true);File.Replace(temp,db,null);}
            var restart=new ProcessStartInfo(exe){UseShellExecute=false};restart.ArgumentList.Add("--dev-db");restart.ArgumentList.Add(db);Process.Start(restart);
        }
        catch(Exception ex)
        {
            if(restoredProcess is { HasExited:false })try{restoredProcess.Kill(true);await restoredProcess.WaitForExitAsync(ct);}catch{}
            if(switched&&File.Exists(rollback))try{var temp=db+".rollback.tmp";File.Copy(rollback,temp,true);File.Replace(temp,db,null);}catch{}
            try{await File.WriteAllTextAsync(marker,"ERROR "+ex.GetType().Name,ct);}catch{}
            var exe=Environment.ProcessPath;if(exe is not null){var start=new ProcessStartInfo(exe){UseShellExecute=false};start.ArgumentList.Add("--dev-db");start.ArgumentList.Add(db);Process.Start(start);}
        }
        finally{try{if(File.Exists(marker))File.Delete(marker);}catch{} if(restauracionComprobada)try{if(File.Exists(staged))File.Delete(staged);}catch{}}
    }

    public static async Task ValidarCajaCerradaAsync(string db,CancellationToken ct=default)
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false,DefaultTimeout=5}.ToString());await connection.OpenAsync(ct);
        try
        {
            using(var query=connection.CreateCommand()){query.CommandText="SELECT COUNT(*) FROM caja_sesion WHERE estado = 'abierta';";if(Convert.ToInt64(await query.ExecuteScalarAsync(ct))!=0)throw new InvalidOperationException("Cerrá la caja antes de restaurar un respaldo.");}
            using(var checkpoint=connection.CreateCommand()){checkpoint.CommandText="PRAGMA wal_checkpoint(TRUNCATE);";using var reader=await checkpoint.ExecuteReaderAsync(ct);if(await reader.ReadAsync(ct)&&reader.GetInt64(0)!=0)throw new IOException("SQLite todavía mantiene una operación activa; no se reemplazó la base.");}
        }
        finally{await connection.CloseAsync();await connection.DisposeAsync();}
        SqliteConnection.ClearAllPools();
        foreach(var suffix in new[]{"-wal","-shm"}){var sidecar=db+suffix;if(File.Exists(sidecar)&&new FileInfo(sidecar).Length>0)throw new IOException("Hay un archivo auxiliar SQLite pendiente; se canceló la restauración sin reemplazar la base.");if(File.Exists(sidecar))try{File.Delete(sidecar);}catch(IOException){}}
    }
    public static async Task<string> ReemplazarBaseVerificadaAsync(string db,string staged,string rollback,CancellationToken ct=default,Action<string,string>? replace=null)
    {
        db=Path.GetFullPath(db);staged=Path.GetFullPath(staged);rollback=Path.GetFullPath(rollback);var versions=MigracionesF01.Todas.Select(m=>m.Version).ToArray();var verifier=new ServicioRespaldos(new TiempoHelper());
        await verifier.VerificarAsync(staged,versions,ct);
        SqliteConnection.ClearAllPools();var adjacentTemp=db+$".restore-{Guid.NewGuid():N}.tmp";File.Copy(staged,adjacentTemp,false);
        try
        {
            File.Copy(db,rollback,false);
            try{await verifier.VerificarAsync(rollback,versions,ct);}catch{File.Delete(rollback);throw;}
            try
            {
                (replace??((source,destination)=>File.Replace(source,destination,null)))(adjacentTemp,db);
                await verifier.VerificarAsync(db,versions,ct);return rollback;
            }
            catch
            {
                if(File.Exists(rollback))
                {
                    var restoreTemp=db+$".rollback-{Guid.NewGuid():N}.tmp";File.Copy(rollback,restoreTemp,false);
                    try{File.Replace(restoreTemp,db,null);}finally{if(File.Exists(restoreTemp))File.Delete(restoreTemp);}
                }
                throw;
            }
        }
        finally{if(File.Exists(adjacentTemp))File.Delete(adjacentTemp);}
    }
    private sealed class TiempoHelper:IReloj{public DateTimeOffset UtcNow()=>DateTimeOffset.UtcNow;}
}
