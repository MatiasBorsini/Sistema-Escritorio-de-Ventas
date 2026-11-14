using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Respaldos;
using LocalInfantil.Infrastructure.Tiempo;
using LocalInfantil.Tests.Fakes;

namespace LocalInfantil.Tests.Respaldos;

public sealed class BloqueoInterprocesoRespaldosTests
{
    [Fact]
    public async Task Bloquea_misma_base_entre_instancias_pero_no_otra_base_y_se_libera_al_finalizar()
    {
        var root=Path.Combine(Path.GetTempPath(),"LocalInfantil.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var dbA=Path.Combine(root,"a.db");var dbB=Path.Combine(root,"b.db");await new MigradorBaseDatos(dbA).MigrarAsync(MigracionesF01.Todas);await new MigradorBaseDatos(dbB).MigrarAsync(MigracionesF01.Todas);
            var backups=Path.Combine(root,"Backups");var clock=new RelojFijo(new DateTimeOffset(2026,10,5,12,0,0,TimeSpan.Zero));var gate=new SnapshotConGate();
            var first=new GestorRespaldosLocales(dbA,backups,gate,clock);var code=first.CrearCodigoRecuperacion();first.ConfirmarCodigoRecuperacion(code);
            var secondSameDb=new GestorRespaldosLocales(dbA,backups,reloj:clock);var otherDb=new GestorRespaldosLocales(dbB,backups,reloj:clock);
            var firstTask=first.CrearAsync(MotivoRespaldo.Manual);await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var blocked=await Assert.ThrowsAsync<IOException>(()=>secondSameDb.CrearAsync(MotivoRespaldo.Manual));Assert.Contains("otra instancia",blocked.Message,StringComparison.OrdinalIgnoreCase);
            var other=await otherDb.CrearAsync(MotivoRespaldo.Manual);Assert.True(File.Exists(other.Manifiesto.RutaLocal));
            gate.Release.TrySetResult();var completed=await firstTask;Assert.True(File.Exists(completed.Manifiesto.RutaLocal));
            var afterRelease=await secondSameDb.CrearAsync(MotivoRespaldo.Manual);Assert.True(File.Exists(afterRelease.Manifiesto.RutaLocal));Assert.Equal(1,gate.Invocations);
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }

    private sealed class SnapshotConGate:IServicioRespaldos
    {
        private readonly ServicioRespaldos _inner=new(new RelojSistema());private int _invocations;
        public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public int Invocations=>Volatile.Read(ref _invocations);
        public async Task<RespaldoVerificado> CrearRespaldoAsync(string rutaOrigen,string directorioDestino,IReadOnlyList<int>? versionesEsperadas,string prefijo="respaldo",CancellationToken cancellationToken=default)
        {if(Interlocked.Increment(ref _invocations)==1){Entered.TrySetResult();await Release.Task.WaitAsync(cancellationToken);}return await _inner.CrearRespaldoAsync(rutaOrigen,directorioDestino,versionesEsperadas,prefijo,cancellationToken);}
        public Task<VerificacionRespaldo> VerificarAsync(string rutaRespaldo,IReadOnlyList<int>? versionesEsperadas,CancellationToken cancellationToken=default)=>_inner.VerificarAsync(rutaRespaldo,versionesEsperadas,cancellationToken);
        public Task<string> RestaurarEnsayoAsync(string rutaRespaldo,string directorioTemporal,IReadOnlyList<int>? versionesEsperadas,CancellationToken cancellationToken=default)=>_inner.RestaurarEnsayoAsync(rutaRespaldo,directorioTemporal,versionesEsperadas,cancellationToken);
    }
}
