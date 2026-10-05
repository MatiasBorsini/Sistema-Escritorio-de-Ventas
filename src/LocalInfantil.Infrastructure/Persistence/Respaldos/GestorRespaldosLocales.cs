using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using System.Runtime.Versioning;
using LocalInfantil.Infrastructure.Tiempo;

namespace LocalInfantil.Infrastructure.Persistence.Respaldos;

public enum MotivoRespaldo { Programado, CierreCaja, Manual }
public enum EstadoRemotoRespaldo { Pendiente, Subiendo, Confirmado, RequiereAtencion }
public sealed record ManifiestoRespaldo(string BackupId, string CreadoUtc, string Motivo, int Formato, string RutaLocal, string EstadoLocal, EstadoRemotoRespaldo EstadoRemoto, string Sha256, int Intentos, string? UltimoIntentoUtc, string? UltimoError, string? RemoteFileId, string? RemoteReservedId, long TamanoBytes, int VersionEsquema)
{
    public string? OperacionOrigen {get;init;}
    public string EstadoRemotoHumano => EstadoRemoto switch { EstadoRemotoRespaldo.Pendiente=>"Pendiente de subir",EstadoRemotoRespaldo.Subiendo=>"Subiendo",EstadoRemotoRespaldo.Confirmado=>"Respaldada en Drive",_=>"Requiere atención" };
    public string MotivoHumano => Motivo switch { "Programado"=>"Programada", "CierreCaja"=>"Cierre de caja", "Manual"=>"Manual", _=>"Otro" };
}

/// <summary>Empaqueta snapshots creados por IServicioRespaldos. El coordinador no crea otro mecanismo SQLite.</summary>
[SupportedOSPlatform("windows")]
public sealed class GestorRespaldosLocales
{
    private readonly string _db; private readonly string _root; private readonly string _local; private readonly string _queue; private readonly string _staging; private readonly string _secrets;
    private readonly IServicioRespaldos _snapshots; private readonly FormatoRespaldoProtegido _formato = new(); private readonly IReloj _reloj; private readonly IProtectorSecretos _protector;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _serial = new(1, 1);
    private byte[]? _clavePendiente;

    public string DirectorioRaiz => _root;
    public string DirectorioLocal => _local;
    public string DirectorioCola => _queue;
    public string DirectorioStaging => _staging;
    public string DirectorioSecretos => _secrets;
    public bool Configurado => File.Exists(Path.Combine(_secrets, "master.dpapi"));

    public GestorRespaldosLocales(string rutaBaseDatos, string? raiz = null, IServicioRespaldos? snapshots = null, IReloj? reloj = null,IProtectorSecretos? protector=null)
    {
        _db = Path.GetFullPath(rutaBaseDatos); _root = Path.GetFullPath(raiz ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalInfantil", "Backups"));
        _local = Path.Combine(_root,"Local"); _queue = Path.Combine(_root,"Queue"); _staging = Path.Combine(_root,"Staging"); _secrets = Path.Combine(_root,"Secrets");
        _snapshots = snapshots ?? new ServicioRespaldos(reloj ?? new RelojSistema()); _reloj = reloj ?? new RelojSistema();_protector=protector??ProtectorDpapi.Instancia;
    }

    public string CrearCodigoRecuperacion()
    {
        if (Configurado || _clavePendiente is not null) throw new InvalidOperationException("El código de recuperación ya fue configurado.");
        _clavePendiente = RandomNumberGenerator.GetBytes(32);
        return CodigoRecuperacion.Codificar(_clavePendiente);
    }

    public void ConfirmarCodigoRecuperacion(string codigo)
    {
        if (_clavePendiente is null || !CodigoRecuperacion.Validar(codigo, _clavePendiente)) throw new InvalidOperationException("El código ingresado no coincide.");
        ProtegerClaveLocal(_clavePendiente);
        CryptographicOperations.ZeroMemory(_clavePendiente); _clavePendiente = null;
    }

    /// <summary>Importa la clave de recuperación de otra PC y la protege para el usuario Windows actual.</summary>
    public void ProtegerCodigoExistente(string codigo)
    {
        var key=CodigoRecuperacion.Decodificar(codigo);
        try{ProtegerClaveLocal(key);}
        finally{CryptographicOperations.ZeroMemory(key);}
    }

    public async Task<RespaldoCreado> CrearAsync(MotivoRespaldo motivo, CancellationToken ct = default,string? operacionOrigen=null)
    {
        await _serial.WaitAsync(ct);
        FileStream? lockFile = null;
        byte[]? key = null; string? snapshotDir = null; string? encryptedTemp = null;
        try
        {
            Directory.CreateDirectory(Path.Combine(_root,"Locks"));
            lockFile = await AdquirirBloqueoAsync(Path.Combine(_root,"Locks",NombreMutex(_db)+".lock"),ct);
            if(!string.IsNullOrWhiteSpace(operacionOrigen))
            {
                var existing=(await ListarAsync(ct)).FirstOrDefault(x=>x.OperacionOrigen==operacionOrigen);
                if(existing is not null)return new(existing,0,existing.TamanoBytes);
            }
            key = CargarClave();
            foreach (var dir in new[] { _local, _queue, _staging }) Directory.CreateDirectory(dir);
            LimpiarTemporales();
            var id = Guid.NewGuid().ToString("N"); var ahora = _reloj.UtcNow().ToUniversalTime();
            snapshotDir = Path.Combine(_staging, id); Directory.CreateDirectory(snapshotDir);
            var archivoSnapshot = await _snapshots.CrearRespaldoAsync(_db, snapshotDir, MigracionesF01.Todas.Select(m => m.Version).ToArray(), "snapshot", ct);
            var hash = await HashAsync(archivoSnapshot.Ruta, ct); var tamanioOriginal = new FileInfo(archivoSnapshot.Ruta).Length;
            var gzipPath=Path.Combine(snapshotDir,"snapshot.db.gz");
            await using(var raw=File.OpenRead(archivoSnapshot.Ruta))
            await using(var gzipFile=new FileStream(gzipPath,FileMode.CreateNew,FileAccess.Write,FileShare.None,64*1024,FileOptions.Asynchronous|FileOptions.WriteThrough))
            await using(var gzip=new GZipStream(gzipFile,CompressionLevel.Optimal,leaveOpen:true))await raw.CopyToAsync(gzip,64*1024,ct);
            var compressedBytes=new FileInfo(gzipPath).Length;
            var versionApp=typeof(GestorRespaldosLocales).Assembly.GetName().Version?.ToString()??"unknown";
            var metadata = FormatoRespaldoProtegido.ConTamanoPaquete(new FormatoRespaldoProtegido.MetadataRespaldo(id, ahora.ToString("O"), motivo.ToString(), 1, versionApp, hash, archivoSnapshot.Versiones.Max(), tamanioOriginal, 0,operacionOrigen),compressedBytes);
            var nombre = $"localinfantil_{ahora:yyyyMMdd_HHmmss}_{id[..8]}.libackup"; var destinoFinal = Path.Combine(_local,nombre); var temporal = destinoFinal + ".tmp"; encryptedTemp=temporal;
            await using (var source = File.OpenRead(gzipPath))
            await using (var target = new FileStream(temporal,FileMode.CreateNew,FileAccess.Write,FileShare.None,64*1024,FileOptions.Asynchronous|FileOptions.WriteThrough))
                await _formato.CrearDesdeGZipAsync(source,target,key!,metadata,ct);
            var verificacionPath=Path.Combine(snapshotDir,"verificacion.db");
            await using (var encrypted = File.OpenRead(temporal))
            await using (var restored = new FileStream(verificacionPath,FileMode.CreateNew,FileAccess.Write,FileShare.None,64*1024,FileOptions.Asynchronous|FileOptions.WriteThrough))
            {
                var metadataCheck = await _formato.DescifrarAsync(encrypted,restored,key!,ct); await restored.FlushAsync(ct);
                if (metadataCheck.BackupId != id || metadataCheck.Sha256BaseDatos != hash || metadataCheck.TamanoOriginalBytes!=tamanioOriginal || metadataCheck.TamanoComprimidoBytes!=compressedBytes || metadataCheck.TamanoPaqueteBytes!=new FileInfo(temporal).Length) throw new InvalidDataException("La verificación del paquete no coincide.");
            }
            await _snapshots.VerificarAsync(verificacionPath,MigracionesF01.Todas.Select(m=>m.Version).ToArray(),ct);
            if (!string.Equals(await HashAsync(verificacionPath,ct),hash,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("La huella del paquete restaurado no coincide.");
            File.Delete(verificacionPath);
            File.Move(temporal,destinoFinal);
            var info = new FileInfo(destinoFinal);
            var manifest = new ManifiestoRespaldo(id,ahora.ToString("O"),motivo.ToString(),1,destinoFinal,"Verificado",EstadoRemotoRespaldo.Pendiente,hash,0,null,null,null,null,info.Length,archivoSnapshot.Versiones.Max()){OperacionOrigen=operacionOrigen};
            await GuardarManifiestoAsync(manifest,ct); Directory.Delete(snapshotDir,true);
            await RetenerLocalAsync(ct);
            return new(manifest, tamanioOriginal, info.Length);
        }
        finally
        {
            if(key is not null) CryptographicOperations.ZeroMemory(key);
            if(snapshotDir is not null && Directory.Exists(snapshotDir)) try { Directory.Delete(snapshotDir,true); } catch(IOException) { }
            if(encryptedTemp is not null && File.Exists(encryptedTemp)) try { File.Delete(encryptedTemp); } catch(IOException) { }
            if(lockFile is not null) await lockFile.DisposeAsync(); _serial.Release();
        }
    }

    public async Task<IReadOnlyList<ManifiestoRespaldo>> ListarAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_queue); var result = new List<ManifiestoRespaldo>();
        foreach (var file in Directory.EnumerateFiles(_queue,"*.json")) { ct.ThrowIfCancellationRequested(); try { var item=JsonSerializer.Deserialize<ManifiestoRespaldo>(await File.ReadAllTextAsync(file,ct)); if(item is not null && EsRutaLocalValida(item) && Path.GetFileName(file)==item.BackupId+".json") result.Add(item); } catch (JsonException) { } catch (IOException) { } catch (ArgumentException) { } }
        return result.OrderByDescending(x=>x.CreadoUtc,StringComparer.Ordinal).ToArray();
    }

    public async Task<string> PrepararRestauracionAsync(string archivo, string codigoRecuperacion, CancellationToken ct = default)
    {
        var key=CodigoRecuperacion.Decodificar(codigoRecuperacion); var destino=Path.Combine(_staging,$"restore-{Guid.NewGuid():N}.db"); Directory.CreateDirectory(_staging);
        try
        {
            await using var source=File.OpenRead(archivo); await using var output=new FileStream(destino,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,64*1024,FileOptions.Asynchronous|FileOptions.WriteThrough);
            var meta=await _formato.DescifrarAsync(source,output,key,ct); await output.FlushAsync(ct); output.Close();
            if(!string.Equals(await HashAsync(destino,ct),meta.Sha256BaseDatos,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("La huella de base no coincide.");
            await _snapshots.VerificarAsync(destino,MigracionesF01.Todas.Select(m=>m.Version).ToArray(),ct); return destino;
        }
        catch { if(File.Exists(destino)) File.Delete(destino); throw; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public async Task GuardarEstadoRemotoAsync(string backupId, EstadoRemotoRespaldo estado, int intentos, string? fileId, string? error, CancellationToken ct=default)
    {
        var file=Path.Combine(_queue,backupId+".json"); if(!File.Exists(file)) throw new FileNotFoundException("No se encontró el manifiesto local.");
        var current=JsonSerializer.Deserialize<ManifiestoRespaldo>(await File.ReadAllTextAsync(file,ct)) ?? throw new InvalidDataException("Manifiesto ilegible.");
        await GuardarManifiestoAsync(current with { EstadoRemoto=estado, Intentos=intentos, UltimoIntentoUtc=_reloj.UtcNow().ToUniversalTime().ToString("O"), UltimoError=Sanitizar(error), RemoteFileId=fileId ?? current.RemoteFileId },ct);
    }

    public async Task SolicitarCierreCajaAsync(string operacionId,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(operacionId)||operacionId.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'))throw new ArgumentException("La referencia de cierre no es válida.",nameof(operacionId));
        Directory.CreateDirectory(_queue);var request=new SolicitudCierreCaja(operacionId,_reloj.UtcNow().ToUniversalTime().ToString("O"));PublicarAtomico(Path.Combine(_queue,$"cierre-{operacionId}.json"),JsonSerializer.SerializeToUtf8Bytes(request,JsonOptions));await Task.CompletedTask;ct.ThrowIfCancellationRequested();
    }
    public IReadOnlyList<string> SolicitudesCierrePendientes()
    {
        Directory.CreateDirectory(_queue);var result=new List<string>();foreach(var file in Directory.EnumerateFiles(_queue,"cierre-*.json"))try{var request=JsonSerializer.Deserialize<SolicitudCierreCaja>(File.ReadAllText(file));if(request is not null&&!string.IsNullOrWhiteSpace(request.OperacionId))result.Add(request.OperacionId);}catch(JsonException){}return result;
    }
    public async Task CompletarSolicitudCierreAsync(string operacionId,CancellationToken ct=default)
    {
        var file=Path.Combine(_queue,$"cierre-{operacionId}.json");if(File.Exists(file))File.Delete(file);await Task.CompletedTask;ct.ThrowIfCancellationRequested();
    }

    public async Task RegistrarIdRemotoReservadoAsync(string backupId,string reservedId,CancellationToken ct=default)
    {
        var file=Path.Combine(_queue,backupId+".json");var current=JsonSerializer.Deserialize<ManifiestoRespaldo>(await File.ReadAllTextAsync(file,ct))??throw new InvalidDataException("Manifiesto ilegible.");
        await GuardarManifiestoAsync(current with{RemoteReservedId=reservedId,EstadoRemoto=EstadoRemotoRespaldo.Subiendo},ct);
    }

    public async Task SincronizarDriveAsync(IDriveRespaldos drive,CancellationToken ct=default)
    {
        var items=await ListarAsync(ct);IReadOnlyList<ClienteGoogleDriveRespaldos.ArchivoDrive>? remoteSnapshot=null;
        try{remoteSnapshot=await drive.ListarAsync(ct);}catch(OperationCanceledException){throw;}catch{/* No cambiar confirmaciones ni borrar con un listado ambiguo. */}
        if(remoteSnapshot is not null)
        {
            var remoteIds=remoteSnapshot.Select(x=>x.Id).ToHashSet(StringComparer.Ordinal);
            foreach(var confirmed in items.Where(x=>x.EstadoRemoto==EstadoRemotoRespaldo.Confirmado&&x.RemoteFileId is not null&&DateTimeOffset.TryParse(x.CreadoUtc,out var date)&&_reloj.UtcNow()-date<=TimeSpan.FromDays(30)&&!remoteIds.Contains(x.RemoteFileId)))
                await GuardarManifiestoAsync(confirmed with{EstadoRemoto=EstadoRemotoRespaldo.Pendiente,Intentos=0,UltimoIntentoUtc=null,UltimoError="La copia remota ya no está disponible; se volverá a subir con un ID nuevo.",RemoteFileId=null,RemoteReservedId=null},ct);
        }
        foreach(var item in (await ListarAsync(ct)).Where(x=>File.Exists(x.RutaLocal)&&x.EstadoLocal=="Verificado"&&x.EstadoRemoto is EstadoRemotoRespaldo.Pendiente or EstadoRemotoRespaldo.Subiendo or EstadoRemotoRespaldo.RequiereAtencion))
        {
            ct.ThrowIfCancellationRequested();if(DateTimeOffset.TryParse(item.UltimoIntentoUtc,out var last)){var minutes=Math.Min(360,Math.Pow(2,Math.Min(8,item.Intentos)));if(_reloj.UtcNow()-last<TimeSpan.FromMinutes(minutes))continue;}
            var attempts=item.Intentos+1;
            try
            {
                var current=item;
                if(string.IsNullOrWhiteSpace(current.RemoteReservedId)){var reserved=await drive.ReservarIdArchivoAsync(current.BackupId,ct);await RegistrarIdRemotoReservadoAsync(current.BackupId,reserved,ct);current=current with{RemoteReservedId=reserved};}
                await GuardarEstadoRemotoAsync(item.BackupId,EstadoRemotoRespaldo.Subiendo,attempts,null,null,ct);
                var remote=await drive.SubirAsync(current,ct);await GuardarEstadoRemotoAsync(item.BackupId,EstadoRemotoRespaldo.Confirmado,attempts,remote,null,ct);
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex){var attention=ex.Message.Contains("conectar Google Drive",StringComparison.OrdinalIgnoreCase)||ex.Message.Contains("configurado",StringComparison.OrdinalIgnoreCase);await GuardarEstadoRemotoAsync(item.BackupId,attention?EstadoRemotoRespaldo.RequiereAtencion:EstadoRemotoRespaldo.Pendiente,attempts,null,ex.Message,ct);}
        }
        await RetenerDriveAsync(drive,ct);
    }

    public async Task RetenerDriveAsync(IDriveRespaldos drive,CancellationToken ct=default)
    {
        // Una consulta fallida o incompleta lanza antes de borrar cualquier archivo.
        var all=await drive.ListarAsync(ct);var now=_reloj.UtcNow().ToUniversalTime();var delete=new List<ClienteGoogleDriveRespaldos.ArchivoDrive>();
        foreach(var item in all.Where(x=>now-x.CreadoUtc.ToUniversalTime()>TimeSpan.FromDays(30)))delete.Add(item);
        var olderGroups=all.Where(x=>now-x.CreadoUtc.ToUniversalTime()>TimeSpan.FromDays(7)&&now-x.CreadoUtc.ToUniversalTime()<=TimeSpan.FromDays(30)).GroupBy(x=>x.CreadoUtc.UtcDateTime.Date);
        foreach(var day in olderGroups)
        {
            var keep=day.OrderByDescending(x=>string.Equals(x.Motivo,"CierreCaja",StringComparison.OrdinalIgnoreCase)).ThenByDescending(x=>x.CreadoUtc).First();
            delete.AddRange(day.Where(x=>x.Id!=keep.Id));
        }
        foreach(var item in delete.DistinctBy(x=>x.Id))await drive.EliminarAsync(item.Id,ct);
    }

    public async Task RetenerLocalAsync(CancellationToken ct=default)
    {
        var items=(await ListarAsync(ct)).Where(x=>File.Exists(x.RutaLocal)).OrderByDescending(x=>x.CreadoUtc,StringComparer.Ordinal).ToList();
        var limite=_reloj.UtcNow().ToUniversalTime().AddDays(-7); var validar=items.Where(x=>DateTimeOffset.TryParse(x.CreadoUtc,out var date)&&date>=limite).Select(x=>x.BackupId).ToHashSet(StringComparer.Ordinal);
        var keep=items.Where(x=>validar.Contains(x.BackupId)||x.EstadoRemoto!=EstadoRemotoRespaldo.Confirmado).Select(x=>x.BackupId).ToHashSet(StringComparer.Ordinal);
        if(items.FirstOrDefault() is { } newest) keep.Add(newest.BackupId);
        foreach(var item in items.Where(x=>!keep.Contains(x.BackupId))) { ct.ThrowIfCancellationRequested(); try { File.Delete(item.RutaLocal); File.Delete(Path.Combine(_queue,item.BackupId+".json")); } catch(IOException) { } }
    }

    public async Task RecuperarAsync(CancellationToken ct=default)
    {
        foreach(var temp in Directory.Exists(_root)?Directory.EnumerateFiles(_root,"*.tmp*",SearchOption.AllDirectories):Array.Empty<string>()) { ct.ThrowIfCancellationRequested(); try{File.Delete(temp);}catch(IOException){} }
        if(Directory.Exists(_staging))foreach(var dir in Directory.EnumerateDirectories(_staging).Where(x=>!Path.GetFileName(x).StartsWith("restore-",StringComparison.Ordinal)))try{Directory.Delete(dir,true);}catch(IOException){}
        Directory.CreateDirectory(_queue);Directory.CreateDirectory(_local);
        foreach(var sidecar in Directory.EnumerateFiles(_queue,"*.json"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var item=JsonSerializer.Deserialize<ManifiestoRespaldo>(await File.ReadAllTextAsync(sidecar,ct));
                if(item is not null&&Path.GetFileName(sidecar)==item.BackupId+".json"&&EsRutaLocalValida(item)&&!File.Exists(item.RutaLocal)&&item.EstadoLocal!="Error de copia local")
                    await GuardarManifiestoAsync(item with{EstadoLocal="Error de copia local",EstadoRemoto=EstadoRemotoRespaldo.RequiereAtencion,UltimoError="No se encontró el archivo local asociado al manifiesto."},ct);
            }
            catch(JsonException){}
            catch(ArgumentException){}
            catch(IOException){}
        }
        if(!Configurado)return;
        var key=CargarClave();
        try
        {
            foreach(var archive in Directory.EnumerateFiles(_local,"*.libackup"))
            {
                ct.ThrowIfCancellationRequested();if((await ListarAsync(ct)).Any(m=>Path.GetFullPath(m.RutaLocal)==Path.GetFullPath(archive)))continue;
                var temp=Path.Combine(_staging,$"reconcile-{Guid.NewGuid():N}.db");Directory.CreateDirectory(_staging);
                try
                {
                    FormatoRespaldoProtegido.MetadataRespaldo meta;
                    await using(var source=File.OpenRead(archive))
                    await using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,64*1024,FileOptions.Asynchronous|FileOptions.WriteThrough))
                    {meta=await _formato.DescifrarAsync(source,output,key,ct);await output.FlushAsync(ct);}
                    if(!string.Equals(await HashAsync(temp,ct),meta.Sha256BaseDatos,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("El hash de la base no coincide.");
                    await _snapshots.VerificarAsync(temp,MigracionesF01.Todas.Select(m=>m.Version).ToArray(),ct);
                    var info=new FileInfo(archive);var manifest=new ManifiestoRespaldo(meta.BackupId,meta.CreadoUtc,meta.Motivo,meta.Formato,archive,"Verificado",EstadoRemotoRespaldo.Pendiente,meta.Sha256BaseDatos,0,null,null,null,null,info.Length,meta.VersionEsquema){OperacionOrigen=meta.OperacionOrigen};
                    await GuardarManifiestoAsync(manifest,ct);
                }
                catch(Exception ex) when(ex is InvalidDataException or RespaldoInvalidoException or CryptographicException){/* Conservar el archivo huérfano para diagnóstico/recuperación manual; no declararlo válido. */}
                finally{if(File.Exists(temp))try{File.Delete(temp);}catch(IOException){}}
            }
        }
        finally{CryptographicOperations.ZeroMemory(key);}
    }

    private byte[] CargarClave()
    {
        var file=Path.Combine(_secrets,"master.dpapi"); if(!File.Exists(file)) throw new InvalidOperationException("Configurá primero el código de recuperación de respaldos.");
        return _protector.Desproteger(File.ReadAllBytes(file),Encoding.UTF8.GetBytes("LocalInfantil.Backups.v1"));
    }
    private void ProtegerClaveLocal(byte[] key)
    {
        Directory.CreateDirectory(_secrets);
        var protegido=_protector.Proteger(key,Encoding.UTF8.GetBytes("LocalInfantil.Backups.v1"));
        PublicarAtomico(Path.Combine(_secrets,"master.dpapi"),protegido);
    }
    private bool EsRutaLocalValida(ManifiestoRespaldo item)
    {
        if(string.IsNullOrWhiteSpace(item.RutaLocal)||!string.Equals(Path.GetExtension(item.RutaLocal),".libackup",StringComparison.OrdinalIgnoreCase))return false;
        var full=Path.GetFullPath(item.RutaLocal);var parent=Path.GetDirectoryName(full);
        return string.Equals(parent,_local,StringComparison.OrdinalIgnoreCase);
    }
    private async Task GuardarManifiestoAsync(ManifiestoRespaldo m,CancellationToken ct) { Directory.CreateDirectory(_queue); var bytes=JsonSerializer.SerializeToUtf8Bytes(m,JsonOptions); PublicarAtomico(Path.Combine(_queue,m.BackupId+".json"),bytes); await Task.CompletedTask; ct.ThrowIfCancellationRequested(); }
    private static void PublicarAtomico(string path,byte[] data) { var tmp=path+".tmp-"+Guid.NewGuid().ToString("N"); using(var fs=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){fs.Write(data);fs.Flush(true);} if(File.Exists(path)) File.Replace(tmp,path,null); else File.Move(tmp,path); }
    private void LimpiarTemporales() { foreach(var f in Directory.EnumerateFiles(_staging,"*.tmp",SearchOption.AllDirectories)) try{File.Delete(f);}catch(IOException){} foreach(var f in Directory.EnumerateFiles(_local,"*.tmp")) try{File.Delete(f);}catch(IOException){} }
    private static string NombreMutex(string path) { var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))); return "LocalInfantil.Backup."+hash; }
    private static async Task<FileStream> AdquirirBloqueoAsync(string path,CancellationToken ct)
    {
        var until=DateTime.UtcNow.AddSeconds(2);
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            try{return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.Asynchronous);}
            catch(IOException) when(DateTime.UtcNow<until){await Task.Delay(100,ct);}
            catch(IOException ex){throw new IOException("Ya hay otra instancia creando un respaldo para esta base.",ex);}
        }
    }
    private static string? Sanitizar(string? value) { if(string.IsNullOrWhiteSpace(value))return null; var clean=new string(value.Where(c=>!char.IsControl(c)).Take(240).ToArray()); return clean.Replace(Environment.UserName,"[usuario]",StringComparison.OrdinalIgnoreCase); }
    private static async Task<string> HashAsync(string path,CancellationToken ct) { await using var fs=File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(fs,ct)); }
    public sealed record RespaldoCreado(ManifiestoRespaldo Manifiesto,long TamanoOriginal,long TamanoPaquete);
    private sealed record SolicitudCierreCaja(string OperacionId,string CreadaUtc);
}

public static class RutaRespaldos
{
    public static string Resolver() => Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalInfantil","Backups"));
    public static bool PareceSincronizada(string path)
    {
        var full=Path.GetFullPath(path);
        var envPaths=new[]{Environment.GetEnvironmentVariable("OneDrive"),Environment.GetEnvironmentVariable("OneDriveConsumer"),Environment.GetEnvironmentVariable("OneDriveCommercial")}.Where(x=>!string.IsNullOrWhiteSpace(x));
        if(envPaths.Any(root=>full.StartsWith(Path.GetFullPath(root!),StringComparison.OrdinalIgnoreCase))) return true;
        return full.Contains("Google Drive",StringComparison.OrdinalIgnoreCase)||full.Contains("Dropbox",StringComparison.OrdinalIgnoreCase)||full.Contains("OneDrive",StringComparison.OrdinalIgnoreCase);
    }
}

public static class CodigoRecuperacion
{
    private const string Alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public static string Codificar(byte[] key)
    {
        var checksum=SHA256.HashData(key);var payload=key.Concat(checksum[..3]).ToArray();CryptographicOperations.ZeroMemory(checksum);
        try
        {
            var bits=new StringBuilder(); foreach(var b in payload) bits.Append(Convert.ToString(b,2).PadLeft(8,'0'));
            var result=new StringBuilder(); for(var i=0;i<bits.Length;i+=5) result.Append(Alphabet[Convert.ToInt32(bits.ToString(i,Math.Min(5,bits.Length-i)),2)]);
            var s=result.ToString(); return string.Join('-',Enumerable.Range(0,(s.Length+4)/5).Select(i=>s.Substring(i*5,Math.Min(5,s.Length-i*5))));
        }
        finally{CryptographicOperations.ZeroMemory(payload);}
    }
    public static byte[] Decodificar(string code)
    {
        var chars=new string((code??"").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray()); var bits=new StringBuilder();
        foreach(var c in chars) { var index=Alphabet.IndexOf(c); if(index<0)throw new InvalidDataException("Código de recuperación inválido."); bits.Append(Convert.ToString(index,2).PadLeft(5,'0')); }
        var bytes=new byte[bits.Length/8]; for(var i=0;i<bytes.Length;i++)bytes[i]=Convert.ToByte(bits.ToString(i*8,8),2);
        try
        {
            if(bytes.Length!=35)throw new InvalidDataException("El código de recuperación no es válido.");
            var checksum=SHA256.HashData(bytes[..32]);try{if(!CryptographicOperations.FixedTimeEquals(checksum[..3],bytes[32..])) throw new InvalidDataException("El código de recuperación no es válido.");return bytes[..32];}
            finally{CryptographicOperations.ZeroMemory(checksum);}
        }
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public static bool Validar(string code,byte[] expected) { try{return CryptographicOperations.FixedTimeEquals(Decodificar(code),expected);}catch{return false;} }
}
