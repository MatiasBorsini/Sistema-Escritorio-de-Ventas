using System.Threading.Channels;
using System.IO;
using LocalInfantil.Infrastructure.Persistence.Respaldos;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Features.Respaldos;
using LocalInfantil.Infrastructure.Tiempo;

namespace LocalInfantil.Wpf.Infra;

/// <summary>Un consumidor serializa solicitud manual, programada y posterior a cierre.</summary>
public sealed class CoordinadorRespaldos : IAsyncDisposable
{
    private readonly GestorRespaldosLocales _gestor;
    private readonly IDriveRespaldos _drive;
    private readonly IAutorizador _autorizador;
    private readonly IReloj _reloj;
    private readonly Func<TimeSpan,CancellationToken,Task> _esperar;
    private readonly Channel<Solicitud> _cola=Channel.CreateUnbounded<Solicitud>(new UnboundedChannelOptions{SingleReader=true,SingleWriter=false});
    private readonly CancellationTokenSource _cancelar=new();
    private readonly HashSet<string> _cierresEnCola=new(StringComparer.Ordinal);
    private Task? _worker; private Task? _scheduler; private DateTimeOffset? _ultimaProgramada;
    public event EventHandler<string>? EstadoCambiado;
    public string Estado {get;private set;}="Copia local sin configurar";
    public GestorRespaldosLocales Gestor=>_gestor;
    public IDriveRespaldos Drive=>_drive;
    public CoordinadorRespaldos(GestorRespaldosLocales gestor,IDriveRespaldos drive,IAutorizador autorizador,IReloj? reloj=null,Func<TimeSpan,CancellationToken,Task>? esperar=null){_gestor=gestor;_drive=drive;_autorizador=autorizador;_reloj=reloj??new RelojSistema();_esperar=esperar??Task.Delay;}
    public async Task ConectarDriveAsync(string? session,CancellationToken ct=default){if(session is null)throw new InvalidOperationException("La sesión no es válida.");await _autorizador.AutorizarAsync(session,PermisosRespaldos.Configurar,ct);await _drive.ConectarAsync(ct);if(_worker is null)await IniciarAsync(ct);await _gestor.SincronizarDriveAsync(_drive,ct);}

    public async Task IniciarAsync(CancellationToken ct=default)
    {
        if(_worker is not null)return;
        try{await _gestor.RecuperarAsync(ct);}
        catch(OperationCanceledException){throw;}
        catch{Estado="La clave local requiere atención; importá el código de recuperación";EstadoCambiado?.Invoke(this,Estado);}
        if(!_gestor.Configurado){Estado="Configurar el código de recuperación";EstadoCambiado?.Invoke(this,Estado);return;}
        _worker=Task.Run(ProcesarAsync);
        foreach(var operacionId in _gestor.SolicitudesCierrePendientes())EncolarCierre(operacionId);
        var listado=await _gestor.ListarAsync(ct); var last=listado.Where(x=>x.EstadoLocal=="Verificado").Select(x=>DateTimeOffset.TryParse(x.CreadoUtc,out var dt)?dt:(DateTimeOffset?)null).Where(x=>x.HasValue).Max();
        var now=_reloj.UtcNow().ToUniversalTime(); _ultimaProgramada=FloorDosHoras(now);
        if(!last.HasValue||now-last.Value>=TimeSpan.FromHours(2)) Encolar(MotivoRespaldo.Programado);
        _scheduler=Task.Run(ProgramarAsync);
    }

    public async Task<GestorRespaldosLocales.RespaldoCreado> CrearManualAsync(string? session,CancellationToken ct=default){if(session is null)throw new InvalidOperationException("La sesión no es válida.");await _autorizador.AutorizarAsync(session,PermisosRespaldos.Crear,ct);return await SolicitarAsync(MotivoRespaldo.Manual,ct);}
    public async Task SolicitarPostCierreCajaAsync(string operacionId)
    {
        try
        {
            // La operación comercial ya confirmó. Un fallo del sidecar solo requiere atención.
            await _gestor.SolicitarCierreCajaAsync(operacionId);
            if(_worker is not null)EncolarCierre(operacionId);
            else { Estado="Cierre confirmado; el respaldo se procesará al configurar la clave local";EstadoCambiado?.Invoke(this,Estado); }
        }
        catch
        {
            Estado="Cierre confirmado; no se pudo registrar la solicitud de respaldo y requiere atención";
            EstadoCambiado?.Invoke(this,Estado);
        }
    }
    public Task<GestorRespaldosLocales.RespaldoCreado> SolicitarAsync(MotivoRespaldo motivo,CancellationToken ct=default)
    {
        if(_worker is null||_cancelar.IsCancellationRequested) throw new InvalidOperationException("El servicio de respaldos no está iniciado o se está deteniendo.");
        var solicitud=new Solicitud(motivo,new TaskCompletionSource<GestorRespaldosLocales.RespaldoCreado>(TaskCreationOptions.RunContinuationsAsynchronously),null);
        if(!_cola.Writer.TryWrite(solicitud)) throw new IOException("No se pudo poner la copia en cola.");
        return solicitud.Completada!.Task.WaitAsync(ct);
    }

    private void Encolar(MotivoRespaldo motivo)=>_cola.Writer.TryWrite(new Solicitud(motivo,null,null));
    private void EncolarCierre(string operacionId)
    {
        lock(_cierresEnCola)
        {
            if(!_cierresEnCola.Add(operacionId))return;
            if(!_cola.Writer.TryWrite(new Solicitud(MotivoRespaldo.CierreCaja,null,operacionId)))_cierresEnCola.Remove(operacionId);
        }
    }
    private async Task ProcesarAsync()
    {
        try
        {
            await foreach(var solicitud in _cola.Reader.ReadAllAsync(_cancelar.Token))
            {
                try
                {
                    Estado="Generando copia";EstadoCambiado?.Invoke(this,Estado);
                    var backup=await Task.Run(()=>_gestor.CrearAsync(solicitud.Motivo,_cancelar.Token,solicitud.OperacionOrigen),_cancelar.Token);
                    if(solicitud.OperacionOrigen is not null)await _gestor.CompletarSolicitudCierreAsync(solicitud.OperacionOrigen,_cancelar.Token);
                    Estado="Copia local lista — pendiente de subir";EstadoCambiado?.Invoke(this,Estado);
                    if(_drive.Configurado&&_drive.Conectado)
                    {
                        try{await _gestor.SincronizarDriveAsync(_drive,_cancelar.Token);}
                        catch(OperationCanceledException){throw;}
                        catch{Estado="Copia local lista — Drive requiere atención";EstadoCambiado?.Invoke(this,Estado);}
                    }
                    solicitud.Completada?.TrySetResult(backup);
                }
                catch(OperationCanceledException ex){solicitud.Completada?.TrySetCanceled(ex.CancellationToken);}
                catch(Exception ex){Estado="Error de copia local"; EstadoCambiado?.Invoke(this,Estado); solicitud.Completada?.TrySetException(ex);}
                finally{if(solicitud.OperacionOrigen is not null)lock(_cierresEnCola)_cierresEnCola.Remove(solicitud.OperacionOrigen);}
            }
        }
        catch(OperationCanceledException) when(_cancelar.IsCancellationRequested) { }
        finally
        {
            while(_cola.Reader.TryRead(out var pendiente))pendiente.Completada?.TrySetCanceled(_cancelar.Token);
            lock(_cierresEnCola)_cierresEnCola.Clear();
        }
    }
    private async Task ProgramarAsync()
    {
        while(true)
        {
            await _esperar(TimeSpan.FromMinutes(1),_cancelar.Token);
            var now=_reloj.UtcNow().ToUniversalTime(); if(_ultimaProgramada.HasValue&&now>=_ultimaProgramada.Value.AddHours(2)){_ultimaProgramada=FloorDosHoras(now);Encolar(MotivoRespaldo.Programado);}
            if(_drive.Configurado&&_drive.Conectado)try{await _gestor.SincronizarDriveAsync(_drive,_cancelar.Token);}catch(OperationCanceledException){throw;}catch{Estado="Copias locales listas — Drive requiere atención";EstadoCambiado?.Invoke(this,Estado);}
        }
    }
    private static DateTimeOffset FloorDosHoras(DateTimeOffset now)=>new(now.Year,now.Month,now.Day,(now.Hour/2)*2,0,0,TimeSpan.Zero);
    public async Task<bool> DetenerAsync(TimeSpan espera)
    {
        _cancelar.Cancel(); _cola.Writer.TryComplete(); var tasks=new[]{_worker??Task.CompletedTask,_scheduler??Task.CompletedTask};
        try{await Task.WhenAll(tasks).WaitAsync(espera);return true;}catch(TimeoutException){return false;}catch(OperationCanceledException){return true;}
    }
    public async ValueTask DisposeAsync(){var stopped=await DetenerAsync(TimeSpan.FromSeconds(5));if(stopped)_cancelar.Dispose();_drive.Dispose();}
    private sealed record Solicitud(MotivoRespaldo Motivo,TaskCompletionSource<GestorRespaldosLocales.RespaldoCreado>? Completada,string? OperacionOrigen);
}
