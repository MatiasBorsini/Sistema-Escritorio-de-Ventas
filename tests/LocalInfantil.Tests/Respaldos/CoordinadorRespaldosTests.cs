using System.Collections.Concurrent;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Respaldos;
using LocalInfantil.Infrastructure.Tiempo;
using LocalInfantil.Tests.Fakes;
using LocalInfantil.Tests.Ventas;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Tests.Respaldos;

public sealed class CoordinadorRespaldosTests
{
    [Fact]
    public async Task Scheduler_espera_dos_horas_y_serializa_programado_manual_y_cierre()
    {
        var root=CrearRoot();var db=Path.Combine(root,"db.sqlite");var backups=Path.Combine(root,"Backups");var clock=new RelojFijo(new DateTimeOffset(2026,10,5,12,0,0,TimeSpan.Zero));
        try
        {
            await new MigradorBaseDatos(db).MigrarAsync(MigracionesF01.Todas);var seed=new GestorRespaldosLocales(db,backups,reloj:clock);var code=seed.CrearCodigoRecuperacion();seed.ConfirmarCodigoRecuperacion(code);await seed.CrearAsync(MotivoRespaldo.Manual);
            var snapshots=new SnapshotControlado(new ServicioRespaldos(new RelojSistema()),bloquearPrimero:true);var manager=new GestorRespaldosLocales(db,backups,snapshots,clock);var delay=new DelayManual();await using var coordinator=new CoordinadorRespaldos(manager,new DriveFalso(),new AutorizadorFalso(),clock,delay.EsperarAsync);
            var cierreProcesado=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);coordinator.EstadoCambiado+=(_,estado)=>{if(estado=="Copia local lista — pendiente de subir"&&manager.SolicitudesCierrePendientes().Count==0)cierreProcesado.TrySetResult();};
            await coordinator.IniciarAsync();await delay.EsperaSiguienteAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var manual=coordinator.SolicitarAsync(MotivoRespaldo.Manual);await snapshots.PrimeraIniciada.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Avanzar(TimeSpan.FromHours(1)+TimeSpan.FromMinutes(59));delay.Pulsar();await delay.EsperaSiguienteAsync().WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(1,snapshots.Inicios);
            clock.Avanzar(TimeSpan.FromMinutes(1));delay.Pulsar();await delay.EsperaSiguienteAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.SolicitarPostCierreCajaAsync("close-f08-1");await coordinator.SolicitarPostCierreCajaAsync("close-f08-1");
            snapshots.LiberarPrimera.TrySetResult();await manual.WaitAsync(TimeSpan.FromSeconds(20));await cierreProcesado.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(3,snapshots.Inicios);Assert.Equal(1,snapshots.MaximoEnParalelo);Assert.Empty(manager.SolicitudesCierrePendientes());
            Assert.True(await coordinator.DetenerAsync(TimeSpan.FromSeconds(5)));
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }

    [Fact]
    public async Task Inicio_vencido_solicita_copia_y_fallo_no_detiene_worker()
    {
        var root=CrearRoot();var db=Path.Combine(root,"db.sqlite");var clock=new RelojFijo(new DateTimeOffset(2026,10,5,16,0,0,TimeSpan.Zero));
        try
        {
            await new MigradorBaseDatos(db).MigrarAsync(MigracionesF01.Todas);var snapshots=new SnapshotControlado(new ServicioRespaldos(new RelojSistema()),fallarPrimero:true);var manager=new GestorRespaldosLocales(db,Path.Combine(root,"Backups"),snapshots,clock);var code=manager.CrearCodigoRecuperacion();manager.ConfirmarCodigoRecuperacion(code);
            var delay=new DelayManual();await using var coordinator=new CoordinadorRespaldos(manager,new DriveFalso(),new AutorizadorFalso(),clock,delay.EsperarAsync);var falloObservado=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);coordinator.EstadoCambiado+=(_,state)=>{if(state=="Error de copia local")falloObservado.TrySetResult();};await coordinator.IniciarAsync();await falloObservado.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("Error de copia local",coordinator.Estado);var retry=coordinator.SolicitarAsync(MotivoRespaldo.Manual);await retry.WaitAsync(TimeSpan.FromSeconds(20));Assert.Equal(2,snapshots.Inicios);Assert.Equal(2,snapshots.Terminadas);
            Assert.True(await coordinator.DetenerAsync(TimeSpan.FromSeconds(5)));
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }

    [Fact]
    public async Task Detener_cancela_trabajo_activo_y_finaliza_solicitudes_en_cola_sin_dejar_promesas_pendientes()
    {
        var root=CrearRoot();var db=Path.Combine(root,"db.sqlite");var clock=new RelojFijo(new DateTimeOffset(2026,10,5,12,0,0,TimeSpan.Zero));
        try
        {
            await new MigradorBaseDatos(db).MigrarAsync(MigracionesF01.Todas);var seed=new GestorRespaldosLocales(db,Path.Combine(root,"Backups"),reloj:clock);var code=seed.CrearCodigoRecuperacion();seed.ConfirmarCodigoRecuperacion(code);await seed.CrearAsync(MotivoRespaldo.Manual);
            var snapshots=new SnapshotControlado(new ServicioRespaldos(new RelojSistema()),bloquearPrimero:true);var manager=new GestorRespaldosLocales(db,seed.DirectorioRaiz,snapshots,clock);var delay=new DelayManual();await using var coordinator=new CoordinadorRespaldos(manager,new DriveFalso(),new AutorizadorFalso(),clock,delay.EsperarAsync);
            await coordinator.IniciarAsync();await delay.EsperaSiguienteAsync().WaitAsync(TimeSpan.FromSeconds(5));var active=coordinator.SolicitarAsync(MotivoRespaldo.Manual);await snapshots.PrimeraIniciada.Task.WaitAsync(TimeSpan.FromSeconds(5));var queued=coordinator.SolicitarAsync(MotivoRespaldo.Manual);
            Assert.True(await coordinator.DetenerAsync(TimeSpan.FromSeconds(5)));await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>active);await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>queued);
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }

    [Fact]
    public async Task Falla_backup_post_cierre_no_revierte_cierre_y_reinicio_recupera_solicitud()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var actual=(await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;var cierreId="close-f08-post-commit";var root=CrearRoot();var clock=new RelojFijo(new DateTimeOffset(2026,10,5,12,0,0,TimeSpan.Zero));
        try
        {
            var backups=Path.Combine(root,"Backups");var seed=new GestorRespaldosLocales(e.Servicios.RutaBaseDatos,backups,reloj:clock);var key=seed.CrearCodigoRecuperacion();seed.ConfirmarCodigoRecuperacion(key);await seed.CrearAsync(MotivoRespaldo.Manual);
            var snapshots=new SnapshotControlado(new ServicioRespaldos(new RelojSistema()),fallarPrimero:true);var manager=new GestorRespaldosLocales(e.Servicios.RutaBaseDatos,backups,snapshots,clock);var delay=new DelayManual();await using(var coordinator=new CoordinadorRespaldos(manager,new DriveFalso(),new AutorizadorFalso(),clock,delay.EsperarAsync))
            {
                var failed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);coordinator.EstadoCambiado+=(_,state)=>{if(state=="Error de copia local")failed.TrySetResult();};await coordinator.IniciarAsync();await delay.EsperaSiguienteAsync().WaitAsync(TimeSpan.FromSeconds(5));
                var arqueo=await e.Servicios.CasoCerrarCaja.PrevisualizarAsync(actual.EfectivoEsperadoMinor??0,e.Sesion);await e.Servicios.CasoCerrarCaja.EjecutarAsync(arqueo.SesionId,arqueo.EfectivoEsperadoMinor,arqueo.EfectivoEsperadoMinor,null,cierreId,e.Sesion);
                await coordinator.SolicitarPostCierreCajaAsync(cierreId);await failed.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Null(await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion));Assert.Equal(new[]{cierreId},manager.SolicitudesCierrePendientes());Assert.True(await coordinator.DetenerAsync(TimeSpan.FromSeconds(5)));
            }
            var restartedManager=new GestorRespaldosLocales(e.Servicios.RutaBaseDatos,backups,snapshots,clock);var restartedDelay=new DelayManual();await using var restarted=new CoordinadorRespaldos(restartedManager,new DriveFalso(),new AutorizadorFalso(),clock,restartedDelay.EsperarAsync);var restored=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);restarted.EstadoCambiado+=(_,state)=>{if(state=="Copia local lista — pendiente de subir"&&restartedManager.SolicitudesCierrePendientes().Count==0)restored.TrySetResult();};await restarted.IniciarAsync();await restored.Task.WaitAsync(TimeSpan.FromSeconds(20));Assert.Null(await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion));Assert.Empty(restartedManager.SolicitudesCierrePendientes());
            var duplicateHandled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);restarted.EstadoCambiado+=(_,state)=>{if(state=="Copia local lista — pendiente de subir"&&restartedManager.SolicitudesCierrePendientes().Count==0)duplicateHandled.TrySetResult();};await restarted.SolicitarPostCierreCajaAsync(cierreId);await restarted.SolicitarPostCierreCajaAsync(cierreId);await duplicateHandled.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Single((await restartedManager.ListarAsync()).Where(x=>x.OperacionOrigen==cierreId));Assert.True(await restarted.DetenerAsync(TimeSpan.FromSeconds(5)));
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }

    private static string CrearRoot(){var p=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}

    private sealed class DelayManual
    {
        private readonly object _sync=new();private TaskCompletionSource _pulse=Nuevo();private TaskCompletionSource _esperando=Nuevo();
        public async Task EsperarAsync(TimeSpan intervalo,CancellationToken ct){Task pulse;lock(_sync){_esperando.TrySetResult();pulse=_pulse.Task;}await pulse.WaitAsync(ct);}
        public Task EsperaSiguienteAsync(){lock(_sync)return _esperando.Task;}
        public void Pulsar(){lock(_sync){_pulse.TrySetResult();_pulse=Nuevo();_esperando=Nuevo();}}
        private static TaskCompletionSource Nuevo()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SnapshotControlado(IServicioRespaldos real,bool bloquearPrimero=false,bool fallarPrimero=false):IServicioRespaldos
    {
        private int _inicios,_activos,_maximos,_terminadas;private readonly object _sync=new();private readonly Dictionary<int,TaskCompletionSource> _terminacion=[];
        public int Inicios=>Volatile.Read(ref _inicios);public int Terminadas=>Volatile.Read(ref _terminadas);public int MaximoEnParalelo=>Volatile.Read(ref _maximos);public TaskCompletionSource PrimeraIniciada{get;}=Nuevo();public TaskCompletionSource LiberarPrimera{get;}=Nuevo();
        public async Task<RespaldoVerificado> CrearRespaldoAsync(string rutaOrigen,string directorioDestino,IReadOnlyList<int>? versionesEsperadas,string prefijo="respaldo",CancellationToken cancellationToken=default)
        {
            var call=Interlocked.Increment(ref _inicios);var active=Interlocked.Increment(ref _activos);UpdateMax(active);if(call==1)PrimeraIniciada.TrySetResult();
            try
            {
                if(call==1&&bloquearPrimero)await LiberarPrimera.Task.WaitAsync(cancellationToken);
                if(call==1&&fallarPrimero)throw new IOException("Fallo controlado de snapshot.");
                return await real.CrearRespaldoAsync(rutaOrigen,directorioDestino,versionesEsperadas,prefijo,cancellationToken);
            }
            finally{Interlocked.Decrement(ref _activos);var count=Interlocked.Increment(ref _terminadas);lock(_sync)if(_terminacion.TryGetValue(count,out var signal))signal.TrySetResult();}
        }
        public Task<VerificacionRespaldo> VerificarAsync(string rutaRespaldo,IReadOnlyList<int>? versionesEsperadas,CancellationToken cancellationToken=default)=>real.VerificarAsync(rutaRespaldo,versionesEsperadas,cancellationToken);
        public Task<string> RestaurarEnsayoAsync(string rutaRespaldo,string directorioTemporal,IReadOnlyList<int>? versionesEsperadas,CancellationToken cancellationToken=default)=>real.RestaurarEnsayoAsync(rutaRespaldo,directorioTemporal,versionesEsperadas,cancellationToken);
        public Task EsperarTerminadasAsync(int count){if(Terminadas>=count)return Task.CompletedTask;lock(_sync){if(Terminadas>=count)return Task.CompletedTask;if(!_terminacion.TryGetValue(count,out var signal))_terminacion[count]=signal=Nuevo();return signal.Task;}}
        private void UpdateMax(int value){int old;do{old=Volatile.Read(ref _maximos);if(value<=old)return;}while(Interlocked.CompareExchange(ref _maximos,value,old)!=old);}
        private static TaskCompletionSource Nuevo()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class AutorizadorFalso:IAutorizador
    {
        public Task<UsuarioRegistrado> AutorizarAsync(string idSesion,string permiso,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<IReadOnlySet<string>> ObtenerPermisosEfectivosAsync(string idSesion,IReadOnlyCollection<string> permisos,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }

    private sealed class DriveFalso:IDriveRespaldos
    {
        public bool Configurado=>false;public bool Conectado=>false;public Task ConectarAsync(CancellationToken ct=default)=>Task.CompletedTask;public Task<string> ReservarIdArchivoAsync(string backupId,CancellationToken ct=default)=>throw new NotSupportedException();public Task<string> SubirAsync(ManifiestoRespaldo manifest,CancellationToken ct=default)=>throw new NotSupportedException();public Task<IReadOnlyList<ClienteGoogleDriveRespaldos.ArchivoDrive>> ListarAsync(CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<ClienteGoogleDriveRespaldos.ArchivoDrive>>([]);public Task DescargarAsync(string fileId,string destino,CancellationToken ct=default)=>throw new NotSupportedException();public Task EliminarAsync(string fileId,CancellationToken ct=default)=>throw new NotSupportedException();public void Dispose(){}
    }
}
