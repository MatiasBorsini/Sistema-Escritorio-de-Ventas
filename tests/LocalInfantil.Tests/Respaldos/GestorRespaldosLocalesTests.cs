using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Respaldos;
using LocalInfantil.Tests.Fakes;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Tests.Ventas;
using LocalInfantil.Application.Features.Cambios;
using LocalInfantil.Application.Features.CuentaCorriente;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Infrastructure.Tiempo;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Respaldos;

public sealed class GestorRespaldosLocalesTests
{
    [Fact]
    public async Task Crea_snapshot_encriptado_y_manifiesto_sin_claves()
    {
        var root=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var db=Path.Combine(root,"db.sqlite");await new MigradorBaseDatos(db).MigrarAsync(MigracionesF01.Todas);
            var manager=new GestorRespaldosLocales(db,Path.Combine(root,"Backups"));var code=manager.CrearCodigoRecuperacion();manager.ConfirmarCodigoRecuperacion(code);
            var backup=await manager.CrearAsync(MotivoRespaldo.Manual);
            Assert.True(File.Exists(backup.Manifiesto.RutaLocal));Assert.Equal(".libackup",Path.GetExtension(backup.Manifiesto.RutaLocal));Assert.Equal(EstadoRemotoRespaldo.Pendiente,backup.Manifiesto.EstadoRemoto);
            Assert.Single(await manager.ListarAsync());var sidecarPath=Path.Combine(manager.DirectorioCola,backup.Manifiesto.BackupId+".json");var sidecar=await File.ReadAllTextAsync(sidecarPath);
            Assert.DoesNotContain(code,sidecar,StringComparison.Ordinal);Assert.DoesNotContain("token",sidecar,StringComparison.OrdinalIgnoreCase);
            using(var encrypted=File.OpenRead(backup.Manifiesto.RutaLocal))
            using(var clear=new MemoryStream())
            {
                var metadata=await new FormatoRespaldoProtegido().DescifrarAsync(encrypted,clear,CodigoRecuperacion.Decodificar(code),CancellationToken.None);
                Assert.Equal(backup.TamanoOriginal,metadata.TamanoOriginalBytes);Assert.Equal(backup.TamanoPaquete,metadata.TamanoPaqueteBytes);Assert.True(metadata.TamanoComprimidoBytes>=0);
            }
            File.Delete(sidecarPath);var restarted=new GestorRespaldosLocales(db,manager.DirectorioRaiz);await restarted.RecuperarAsync();Assert.Single(await restarted.ListarAsync());
            var newPcManager=new GestorRespaldosLocales(db,Path.Combine(root,"NewPc"));newPcManager.ProtegerCodigoExistente(code);Assert.True(newPcManager.Configurado);
            var staged=await newPcManager.PrepararRestauracionAsync(backup.Manifiesto.RutaLocal,code);Assert.True(File.Exists(staged));File.Delete(staged);
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }

    [Fact]
    public async Task Snapshot_wal_conserva_punto_consistente_durante_transaccion_de_escritura()
    {
        var root=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var dbPath=Path.Combine(root,"db.sqlite");
        try
        {
            await new MigradorBaseDatos(dbPath).MigrarAsync(MigracionesF01.Todas);await using var writer=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=dbPath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());await writer.OpenAsync();
            using(var mode=writer.CreateCommand()){mode.CommandText="PRAGMA journal_mode=WAL;";Assert.Equal("wal",Convert.ToString(await mode.ExecuteScalarAsync()));}
            await using var transaction=await writer.BeginTransactionAsync();using(var insert=writer.CreateCommand()){insert.Transaction=(SqliteTransaction)transaction;insert.CommandText="INSERT INTO usuario(id,nombre,rol,activo,creado_en,huella_contrasena) VALUES('en-escritura','Escritura pendiente','duena',1,'2026-10-05','hash');";await insert.ExecuteNonQueryAsync();}
            var service=new ServicioRespaldos(new RelojSistema());var created=await service.CrearRespaldoAsync(dbPath,Path.Combine(root,"backups"),Enumerable.Range(1,19).ToArray());Assert.Equal("wal",await ScalarTextAsync(dbPath,"PRAGMA journal_mode;"));Assert.Equal(0,await ScalarAsync(created.Ruta,"SELECT COUNT(*) FROM usuario WHERE id='en-escritura';"));
            await transaction.CommitAsync();Assert.Equal(1,await ScalarAsync(dbPath,"SELECT COUNT(*) FROM usuario WHERE id='en-escritura';"));Assert.Equal(new[]{"ok"},await TextRowsAsync(created.Ruta,"PRAGMA integrity_check;"));
        }
        finally{try{SqliteConnection.ClearAllPools();Directory.Delete(root,true);}catch(IOException){}}
    }

    private static async Task<long> ScalarAsync(string path,string sql){await using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());await db.OpenAsync();using var cmd=db.CreateCommand();cmd.CommandText=sql;return Convert.ToInt64(await cmd.ExecuteScalarAsync());}
    private static async Task<string?> ScalarTextAsync(string path,string sql){await using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());await db.OpenAsync();using var cmd=db.CreateCommand();cmd.CommandText=sql;return Convert.ToString(await cmd.ExecuteScalarAsync());}
    private static async Task<string[]> TextRowsAsync(string path,string sql){await using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());await db.OpenAsync();using var cmd=db.CreateCommand();cmd.CommandText=sql;await using var r=await cmd.ExecuteReaderAsync();var rows=new List<string>();while(await r.ReadAsync())rows.Add(r.GetString(0));return rows.ToArray();}

    [Fact]
    public void Ruta_de_respaldos_esta_fuera_de_la_base_y_codigos_invalidos_se_rechazan()
    {
        var resolved=RutaRespaldos.Resolver();Assert.EndsWith(Path.Combine("LocalInfantil","Backups"),resolved,StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidDataException>(()=>CodigoRecuperacion.Decodificar("ABCD-EFGH"));
    }

    [Fact]
    public async Task Drive_reintenta_con_el_mismo_id_reservado_y_no_duplica()
    {
        var root=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var clock=new RelojFijo(DateTimeOffset.UtcNow);
        try
        {
            var db=Path.Combine(root,"db.sqlite");await new MigradorBaseDatos(db).MigrarAsync(MigracionesF01.Todas);var manager=new GestorRespaldosLocales(db,Path.Combine(root,"Backups"),reloj:clock);var code=manager.CrearCodigoRecuperacion();manager.ConfirmarCodigoRecuperacion(code);var copy=await manager.CrearAsync(MotivoRespaldo.Programado);var drive=new DriveFalso();
            await manager.SincronizarDriveAsync(drive);var pending=Assert.Single(await manager.ListarAsync());Assert.Equal(EstadoRemotoRespaldo.Pendiente,pending.EstadoRemoto);Assert.NotNull(pending.RemoteReservedId);Assert.Equal(1,drive.Reservas);Assert.Equal(1,drive.Subidas);
            var restarted=new GestorRespaldosLocales(db,manager.DirectorioRaiz,reloj:clock);await restarted.RecuperarAsync();pending=Assert.Single(await restarted.ListarAsync());Assert.Equal("remote-id-1",pending.RemoteReservedId);Assert.Equal(EstadoRemotoRespaldo.Pendiente,pending.EstadoRemoto);
            await restarted.SincronizarDriveAsync(drive);Assert.Equal(1,drive.Subidas);
            clock.Avanzar(TimeSpan.FromMinutes(3));await restarted.SincronizarDriveAsync(drive);var confirmed=Assert.Single(await restarted.ListarAsync());Assert.Equal(EstadoRemotoRespaldo.Confirmado,confirmed.EstadoRemoto);Assert.Equal("remote-id-1",confirmed.RemoteFileId);Assert.Equal(1,drive.Reservas);Assert.Equal(2,drive.Subidas);Assert.NotEqual(copy.Manifiesto.BackupId,confirmed.RemoteFileId);
            drive.Archivos.Clear();await manager.SincronizarDriveAsync(drive);var recreated=Assert.Single(await manager.ListarAsync());Assert.Equal(EstadoRemotoRespaldo.Confirmado,recreated.EstadoRemoto);Assert.Equal("remote-id-2",recreated.RemoteReservedId);Assert.Equal(2,drive.Reservas);Assert.Equal(3,drive.Subidas);
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }

    [Fact]
    public async Task Reinicio_limpia_temporales_reconcilia_huerfano_y_marca_sidecar_sin_archivo()
    {
        var root=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var db=Path.Combine(root,"db.sqlite");await new MigradorBaseDatos(db).MigrarAsync(MigracionesF01.Todas);var manager=new GestorRespaldosLocales(db,Path.Combine(root,"Backups"));var code=manager.CrearCodigoRecuperacion();manager.ConfirmarCodigoRecuperacion(code);var first=await manager.CrearAsync(MotivoRespaldo.Manual);
            Directory.CreateDirectory(manager.DirectorioStaging);var stale=Path.Combine(manager.DirectorioStaging,"orphan.tmp");await File.WriteAllTextAsync(stale,"not a backup");var finalWithoutSidecar=Path.Combine(manager.DirectorioLocal,"orphan.libackup");File.Copy(first.Manifiesto.RutaLocal,finalWithoutSidecar);File.Delete(first.Manifiesto.RutaLocal);File.Delete(Path.Combine(manager.DirectorioCola,first.Manifiesto.BackupId+".json"));
            var restarted=new GestorRespaldosLocales(db,manager.DirectorioRaiz);await restarted.RecuperarAsync();Assert.False(File.Exists(stale));Assert.Single(await restarted.ListarAsync());Assert.Contains(await restarted.ListarAsync(),x=>x.RutaLocal==finalWithoutSidecar);
            var visible=Assert.Single(await restarted.ListarAsync());File.Delete(visible.RutaLocal);await restarted.RecuperarAsync();visible=Assert.Single(await restarted.ListarAsync());Assert.Equal("Error de copia local",visible.EstadoLocal);Assert.Equal(EstadoRemotoRespaldo.RequiereAtencion,visible.EstadoRemoto);Assert.NotNull(visible.UltimoError);
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }

    [Fact]
    public async Task Solicitud_de_cierre_es_durable_idempotente_y_se_completa()
    {
        var root=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));var manager=new GestorRespaldosLocales(Path.Combine(root,"db.sqlite"),Path.Combine(root,"Backups"));
        try{await manager.SolicitarCierreCajaAsync("close-123");await manager.SolicitarCierreCajaAsync("close-123");Assert.Equal(new[]{"close-123"},manager.SolicitudesCierrePendientes());await manager.CompletarSolicitudCierreAsync("close-123");Assert.Empty(manager.SolicitudesCierrePendientes());}
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }

    [Fact]
    public void Protector_de_secretos_es_abstraible_y_no_guarda_clave_en_claro()
    {
        var root=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));var protector=new ProtectorFalso();var manager=new GestorRespaldosLocales(Path.Combine(root,"db.sqlite"),Path.Combine(root,"Backups"),protector:protector);var code=CodigoRecuperacion.Codificar(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        try
        {
            manager.ProtegerCodigoExistente(code);var path=Path.Combine(manager.DirectorioSecretos,"master.dpapi");var stored=File.ReadAllBytes(path);Assert.NotEqual(CodigoRecuperacion.Decodificar(code),stored);Assert.Equal(CodigoRecuperacion.Decodificar(code),protector.Desproteger(stored,"LocalInfantil.Backups.v1"u8));
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }

    [Fact]
    public async Task Retencion_drive_conserva_todos_7_dias_y_prioriza_cierre_hasta_30()
    {
        var now=new DateTimeOffset(2026,10,5,12,0,0,TimeSpan.Zero);var drive=new DriveFalso{Archivos=[
            new("recent-a","a",100,now.AddDays(-1),"Manual","a"),new("recent-b","b",100,now.AddDays(-7),"CierreCaja","b"),
            new("old-manual","m",100,now.AddDays(-10),"Manual","m"),new("old-close","c",100,now.AddDays(-10).AddHours(1),"CierreCaja","c"),
            new("no-close-earlier","n1",100,now.AddDays(-20),"Manual","n1"),new("no-close-later","n2",100,now.AddDays(-20).AddHours(1),"Manual","n2"),
            new("expired","e",100,now.AddDays(-31),"Manual","e")
        ]};var manager=new GestorRespaldosLocales("unused.db",Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N")),reloj:new RelojFijo(now));
        await manager.RetenerDriveAsync(drive);
        Assert.Equal(new[]{"expired","no-close-earlier","old-manual"},drive.Eliminados.OrderBy(x=>x,StringComparer.Ordinal));
    }

    [Fact]
    public async Task Retencion_drive_no_borra_si_listado_es_ambiguo()
    {
        var drive=new DriveFalso{FallarListado=true};var manager=new GestorRespaldosLocales("unused.db",Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N")));
        await Assert.ThrowsAsync<IOException>(()=>manager.RetenerDriveAsync(drive));Assert.Empty(drive.Eliminados);
    }

    [Fact]
    public async Task Retencion_local_conserva_ultimos_siete_dias_pendientes_y_ultima_copia()
    {
        var root=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var now=new DateTimeOffset(2026,10,5,12,0,0,TimeSpan.Zero);var db=Path.Combine(root,"db.sqlite");await new MigradorBaseDatos(db).MigrarAsync(MigracionesF01.Todas);
            var manager=new GestorRespaldosLocales(db,Path.Combine(root,"Backups"),reloj:new RelojFijo(now));var code=manager.CrearCodigoRecuperacion();manager.ConfirmarCodigoRecuperacion(code);var template=await manager.CrearAsync(MotivoRespaldo.Manual);
            async Task Seed(string id,int age,EstadoRemotoRespaldo remote)
            {
                var local=Path.Combine(manager.DirectorioLocal,id+".libackup");File.Copy(template.Manifiesto.RutaLocal,local);
                var item=template.Manifiesto with{BackupId=id,CreadoUtc=now.AddDays(-age).ToString("O"),RutaLocal=local,EstadoRemoto=remote,RemoteFileId=remote==EstadoRemotoRespaldo.Confirmado?"remote-"+id:null};
                await File.WriteAllTextAsync(Path.Combine(manager.DirectorioCola,id+".json"),System.Text.Json.JsonSerializer.Serialize(item));
            }
            await Seed("recent",3,EstadoRemotoRespaldo.Confirmado);await Seed("recent-boundary",7,EstadoRemotoRespaldo.Confirmado);await Seed("old-pending",20,EstadoRemotoRespaldo.Pendiente);await Seed("old-latest",10,EstadoRemotoRespaldo.Confirmado);await Seed("old-confirmed",12,EstadoRemotoRespaldo.Confirmado);
            File.Delete(template.Manifiesto.RutaLocal);File.Delete(Path.Combine(manager.DirectorioCola,template.Manifiesto.BackupId+".json"));
            await manager.RetenerLocalAsync();var remaining=(await manager.ListarAsync()).Select(x=>x.BackupId).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("recent",remaining);Assert.Contains("recent-boundary",remaining);Assert.Contains("old-pending",remaining);Assert.DoesNotContain("old-latest",remaining);Assert.DoesNotContain("old-confirmed",remaining);
            var secondDb=Path.Combine(root,"only-latest.db");await new MigradorBaseDatos(secondDb).MigrarAsync(MigracionesF01.Todas);var latestOnly=new GestorRespaldosLocales(secondDb,Path.Combine(root,"BackupsOnlyLatest"),reloj:new RelojFijo(now));var secondCode=latestOnly.CrearCodigoRecuperacion();latestOnly.ConfirmarCodigoRecuperacion(secondCode);var only=await latestOnly.CrearAsync(MotivoRespaldo.Manual);var oldManifest=only.Manifiesto with{CreadoUtc=now.AddDays(-10).ToString("O")};await File.WriteAllTextAsync(Path.Combine(latestOnly.DirectorioCola,oldManifest.BackupId+".json"),System.Text.Json.JsonSerializer.Serialize(oldManifest));await latestOnly.RetenerLocalAsync();Assert.Single(await latestOnly.ListarAsync());
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }

    [Fact]
    public async Task E2E_venta_caja_cliente_cuenta_y_cambio_sobreviven_backup_y_restauracion_a_otra_base()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var client=await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cliente E2E F08",null,null),e.Sesion);await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(client,50_000,e.Sesion);
        var (product,v1,v2)=await e.CatalogoConStockAsync();var p1=await e.PrecioDeAsync(v1);var p2=await e.PrecioDeAsync(v2);
        var sale=Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.Servicios.CasoConfirmarVenta.EjecutarAsync([new(v1,1,p1)],[new(MediosPago.Efectivo,3_000,3_000)],"e2e-f08-venta",e.Sesion,client,null,true));
        var detail=(await e.Servicios.ConsultasVentas.ObtenerFichaAsync(sale.VentaId,e.Sesion))!;var line=Assert.Single(detail.Lineas);
        var change=Assert.IsType<ResultadoCambio.Registrado>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(new DatosCambio(sale.VentaId,[new(line.Id,1,true,null)],[new(v2,1,p2)],[new(MediosPago.Efectivo,p2-p1,p2-p1)],null,null,"e2e-f08-cambio",e.Sesion)));
        Assert.Equal("C-000001",change.Numero);Assert.Equal(p1-3_000, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(client,e.Sesion))!.SaldoMinor);
        var root=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var manager=new GestorRespaldosLocales(e.Servicios.RutaBaseDatos,Path.Combine(root,"Backups"));var code=manager.CrearCodigoRecuperacion();manager.ConfirmarCodigoRecuperacion(code);
            var backup=await manager.CrearAsync(MotivoRespaldo.Manual);var staged=await manager.PrepararRestauracionAsync(backup.Manifiesto.RutaLocal,code);
            var dbB=await new LocalInfantil.Infrastructure.Persistence.Respaldos.ServicioRespaldos(new RelojSistema()).RestaurarEnsayoAsync(staged,Path.Combine(root,"DatabaseB"),Enumerable.Range(1,19).ToArray());
            await using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=dbB,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());await db.OpenAsync();
            async Task<long> Scalar(string sql){using var cmd=db.CreateCommand();cmd.CommandText=sql;return Convert.ToInt64(await cmd.ExecuteScalarAsync());}
            Assert.Equal(1,await Scalar("SELECT COUNT(*) FROM producto WHERE id='"+product+"';"));Assert.Equal(1,await Scalar("SELECT COUNT(*) FROM variante WHERE id='"+v1+"' AND producto_id='"+product+"';"));
            Assert.Equal(1,await Scalar("SELECT COUNT(*) FROM venta WHERE id='"+sale.VentaId+"' AND numero='T-000001';"));
            Assert.Equal(1,await Scalar("SELECT COUNT(*) FROM pago WHERE venta_id='"+sale.VentaId+"';"));Assert.Equal(1,await Scalar("SELECT COUNT(*) FROM caja_sesion WHERE estado='abierta';"));
            Assert.Equal(1,await Scalar("SELECT COUNT(*) FROM cliente WHERE id='"+client+"';"));Assert.Equal(p1-3_000,await Scalar("SELECT SUM(importe_minor) FROM cuenta_corriente_movimiento WHERE cliente_id='"+client+"';"));
            Assert.Equal(1,await Scalar("SELECT COUNT(*) FROM cambio WHERE id='"+change.CambioId+"' AND numero='C-000001' AND venta_id='"+sale.VentaId+"';"));
            using var integrity=db.CreateCommand();integrity.CommandText="PRAGMA integrity_check;";Assert.Equal("ok",Convert.ToString(await integrity.ExecuteScalarAsync()));
            using var fk=db.CreateCommand();fk.CommandText="PRAGMA foreign_key_check;";await using var fks=await fk.ExecuteReaderAsync();Assert.False(await fks.ReadAsync());
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }

    private sealed class DriveFalso:IDriveRespaldos
    {
        public bool Configurado=>true;public bool Conectado=>true;public int Reservas{get;private set;}public int Subidas{get;private set;}public List<ClienteGoogleDriveRespaldos.ArchivoDrive> Archivos{get;init;}=[];public List<string> Eliminados{get;}=[];public bool FallarListado{get;init;}
        public Task ConectarAsync(CancellationToken ct=default)=>Task.CompletedTask;
        public Task<string> ReservarIdArchivoAsync(string backupId,CancellationToken ct=default){Reservas++;return Task.FromResult($"remote-id-{Reservas}");}
        public Task<string> SubirAsync(ManifiestoRespaldo manifest,CancellationToken ct=default){Subidas++;if(Subidas==1)throw new IOException("timeout");Archivos.Add(new(manifest.RemoteReservedId!,Path.GetFileName(manifest.RutaLocal),new FileInfo(manifest.RutaLocal).Length,DateTimeOffset.Parse(manifest.CreadoUtc),manifest.Motivo,manifest.BackupId));return Task.FromResult(manifest.RemoteReservedId!);}
        public Task<IReadOnlyList<ClienteGoogleDriveRespaldos.ArchivoDrive>> ListarAsync(CancellationToken ct=default)=>FallarListado?Task.FromException<IReadOnlyList<ClienteGoogleDriveRespaldos.ArchivoDrive>>(new IOException("timeout")):Task.FromResult<IReadOnlyList<ClienteGoogleDriveRespaldos.ArchivoDrive>>(Archivos);
        public Task DescargarAsync(string fileId,string destino,CancellationToken ct=default)=>throw new NotImplementedException();
        public Task EliminarAsync(string fileId,CancellationToken ct=default){Eliminados.Add(fileId);return Task.CompletedTask;}
        public void Dispose(){}
    }

    private sealed class ProtectorFalso:IProtectorSecretos
    {
        public byte[] Proteger(ReadOnlySpan<byte> secreto,ReadOnlySpan<byte> entropia)=>secreto.ToArray().Select(x=>(byte)(x^0xA5)).ToArray();
        public byte[] Desproteger(ReadOnlySpan<byte> protegido,ReadOnlySpan<byte> entropia)=>protegido.ToArray().Select(x=>(byte)(x^0xA5)).ToArray();
    }
}
