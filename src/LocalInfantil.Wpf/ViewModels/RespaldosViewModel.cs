using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Respaldos;
using LocalInfantil.Infrastructure.Persistence.Respaldos;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class RespaldosViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _services; private readonly Func<string?> _session;
    private readonly string _userName;
    private string _error="",_mensaje="",_codigo="",_confirmacion="",_estado="Copia local sin configurar"; private bool _busy;
    private string _codigoRestauracion="",_contrasenaDuena="",_archivoRestauracion="";
    public ObservableCollection<ManifiestoRespaldo> Copias {get;}=[];
    public ObservableCollection<ClienteGoogleDriveRespaldos.ArchivoDrive> CopiasDrive {get;}=[];
    private ClienteGoogleDriveRespaldos.ArchivoDrive? _copiaDriveSeleccionada;
    public bool PuedeCrear{get;} public bool PuedeConfigurar{get;} public bool PuedeRestaurar{get;}
    public bool DriveConfigurado=>_services.CoordinadorRespaldos.Drive.Configurado; public bool DriveConectado=>_services.CoordinadorRespaldos.Drive.Conectado;
    public string Error{get=>_error;private set=>SetProperty(ref _error,value);} public string Mensaje{get=>_mensaje;private set=>SetProperty(ref _mensaje,value);}
    public string Codigo{get=>_codigo;private set{if(SetProperty(ref _codigo,value))OnPropertyChanged(nameof(HayCodigo));}} public bool HayCodigo=>!string.IsNullOrWhiteSpace(Codigo); public string Confirmacion{get=>_confirmacion;set=>SetProperty(ref _confirmacion,value);}
    public string Estado{get=>_estado;private set=>SetProperty(ref _estado,value);} public bool Ocupado{get=>_busy;private set=>SetProperty(ref _busy,value);}
    public bool Configurado=>_services.CoordinadorRespaldos.Gestor.Configurado;
    public string RutaLocal=>_services.CoordinadorRespaldos.Gestor.DirectorioLocal;
    public bool RutaPuedeSincronizarse=>RutaRespaldos.PareceSincronizada(RutaLocal);
    public string CodigoRestauracion{get=>_codigoRestauracion;set=>SetProperty(ref _codigoRestauracion,value);} public string ContrasenaDuena{get=>_contrasenaDuena;set=>SetProperty(ref _contrasenaDuena,value);} public string ArchivoRestauracion{get=>_archivoRestauracion;private set=>SetProperty(ref _archivoRestauracion,value);}
    public ClienteGoogleDriveRespaldos.ArchivoDrive? CopiaDriveSeleccionada{get=>_copiaDriveSeleccionada;set=>SetProperty(ref _copiaDriveSeleccionada,value);}
    public ICommand GenerarCodigoCommand{get;} public ICommand ConfirmarCodigoCommand{get;} public ICommand ProtegerCodigoExistenteCommand{get;} public ICommand CrearCopiaCommand{get;} public ICommand ActualizarCommand{get;} public ICommand ConectarDriveCommand{get;} public ICommand RestaurarCommand{get;} public ICommand SeleccionarArchivoCommand{get;} public ICommand ActualizarDriveCommand{get;} public ICommand DescargarDriveCommand{get;}

    public RespaldosViewModel(ServiciosComerciales services,Func<string?> session,IReadOnlySet<string> permisos,string userName)
    {
        _services=services;_session=session;_userName=userName;PuedeCrear=permisos.Contains(PermisosRespaldos.Crear);PuedeConfigurar=permisos.Contains(PermisosRespaldos.Configurar);PuedeRestaurar=permisos.Contains(PermisosRespaldos.Restaurar);
        GenerarCodigoCommand=new AsyncRelayCommand(_=>GenerarCodigoAsync());ConfirmarCodigoCommand=new AsyncRelayCommand(_=>ConfirmarCodigoAsync());ProtegerCodigoExistenteCommand=new AsyncRelayCommand(_=>ProtegerCodigoExistenteAsync());CrearCopiaCommand=new AsyncRelayCommand(_=>CrearAsync());ActualizarCommand=new AsyncRelayCommand(_=>CargarAsync());ConectarDriveCommand=new AsyncRelayCommand(_=>ConectarDriveAsync());RestaurarCommand=new AsyncRelayCommand(_=>RestaurarAsync());SeleccionarArchivoCommand=new RelayCommand(_=>SeleccionarArchivo());ActualizarDriveCommand=new AsyncRelayCommand(_=>CargarDriveAsync());DescargarDriveCommand=new AsyncRelayCommand(_=>DescargarDriveAsync());
        _services.CoordinadorRespaldos.EstadoCambiado+=(_,state)=>System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>Estado=state);
    }
    public async Task CargarAsync()
    {
        Error="";Copias.Clear();
        try
        {
            foreach(var item in await _services.CoordinadorRespaldos.Gestor.ListarAsync())Copias.Add(item);
            var ultima=Copias.Where(x=>x.EstadoLocal=="Verificado").Select(x=>DateTimeOffset.TryParse(x.CreadoUtc,out var parsed)?parsed:(DateTimeOffset?)null).Where(x=>x.HasValue).Max();
            if(!ultima.HasValue)Estado="No existe una copia local verificada";
            else if(DateTimeOffset.UtcNow-ultima.Value>TimeSpan.FromHours(4))Estado="Atención: la última copia local verificada tiene más de 4 horas";
            else Estado=Copias.First(x=>DateTimeOffset.TryParse(x.CreadoUtc,out var dt)&&dt==ultima.Value).EstadoRemoto==EstadoRemotoRespaldo.Confirmado?"Respaldada en Drive":"Copia local lista — pendiente de subir";
            OnPropertyChanged(nameof(RutaPuedeSincronizarse));
        }
        catch(Exception ex){Error="No se pudo consultar el estado de las copias: "+ex.Message;}
    }
    private async Task GenerarCodigoAsync()
    {
        Error="";Mensaje="";if(!PuedeConfigurar){Error="No tenés permiso para configurar respaldos.";return;}
        try{await _services.Autorizador.AutorizarAsync(_session()??throw new InvalidOperationException("La sesión no es válida."),PermisosRespaldos.Configurar);Codigo=_services.CoordinadorRespaldos.Gestor.CrearCodigoRecuperacion();Mensaje="Código de recuperación de respaldos. Guardalo fuera del equipo; se muestra una sola vez. Recomendación: conservar dos copias físicas, una fuera del local.";}
        catch(Exception ex){Error=ex.Message;}
    }
    private async Task ConfirmarCodigoAsync()
    {
        Error="";Mensaje="";if(!PuedeConfigurar){Error="No tenés permiso para configurar respaldos.";return;} if(string.IsNullOrWhiteSpace(Confirmacion)){Error="Volvé a ingresar el código para confirmar que quedó guardado.";return;}
        try{await _services.Autorizador.AutorizarAsync(_session()??throw new InvalidOperationException("La sesión no es válida."),PermisosRespaldos.Configurar);_services.CoordinadorRespaldos.Gestor.ConfirmarCodigoRecuperacion(Confirmacion);Codigo="";Confirmacion="";await _services.CoordinadorRespaldos.IniciarAsync();OnPropertyChanged(nameof(Configurado));await CargarAsync();Mensaje="La clave local quedó protegida con DPAPI para esta cuenta de Windows.";}
        catch(Exception ex){Error=ex.Message;}
    }
    private async Task ProtegerCodigoExistenteAsync()
    {
        Error="";Mensaje="";if(!PuedeConfigurar){Error="No tenés permiso para configurar respaldos.";return;}
        if(string.IsNullOrWhiteSpace(Confirmacion)){Error="Ingresá tu código de recuperación completo.";return;}
        var confirm=MessageBox.Show("Esto protegerá el código ingresado para esta cuenta de Windows y reemplazará la clave local de respaldos. Continuá solo si es el código original de esos respaldos.","Proteger código existente",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No);
        if(confirm!=MessageBoxResult.Yes)return;
        try{await _services.Autorizador.AutorizarAsync(_session()??throw new InvalidOperationException("La sesión no es válida."),PermisosRespaldos.Configurar);_services.CoordinadorRespaldos.Gestor.ProtegerCodigoExistente(Confirmacion);Confirmacion="";await _services.CoordinadorRespaldos.IniciarAsync();OnPropertyChanged(nameof(Configurado));Mensaje="El código existente quedó protegido con DPAPI en este Windows.";}
        catch(Exception ex){Error=ex.Message;}
    }
    private async Task CrearAsync()
    {
        Error="";Mensaje="";if(!PuedeCrear){Error="No tenés permiso para crear respaldos.";return;}if(!Configurado){Error="Configurá primero el código de recuperación.";return;} Ocupado=true;
        try{var result=await _services.CoordinadorRespaldos.CrearManualAsync(_session());await CargarAsync();Mensaje=$"Copia local verificada. Tamaño: {result.TamanoPaquete:N0} bytes.";}
        catch(Exception ex){Estado="Error de copia local";Error="No se pudo crear o verificar la copia local: "+ex.Message;}
        finally{Ocupado=false;}
    }
    private async Task ConectarDriveAsync()
    {
        Error="";Mensaje="";if(!PuedeConfigurar){Error="No tenés permiso para configurar respaldos.";return;}Ocupado=true;
        try{await _services.CoordinadorRespaldos.ConectarDriveAsync(_session());OnPropertyChanged(nameof(DriveConectado));await CargarAsync();Mensaje="Google Drive conectado. Las copias locales pendientes se procesarán en segundo plano.";}
        catch(Exception ex){Error=ex.Message;}
        finally{Ocupado=false;}
    }

    private async Task CargarDriveAsync()
    {
        Error="";if(!PuedeRestaurar){Error="No tenés permiso para consultar copias remotas.";return;}
        if(!DriveConectado){Error="Conectá Google Drive antes de consultar las copias remotas.";return;}
        Ocupado=true;
        try
        {
            await _services.Autorizador.AutorizarAsync(_session()??throw new InvalidOperationException("La sesión no es válida."),PermisosRespaldos.Consultar);
            CopiasDrive.Clear();foreach(var item in await _services.CoordinadorRespaldos.Drive.ListarAsync())CopiasDrive.Add(item);
            Mensaje=$"Copias remotas encontradas: {CopiasDrive.Count}.";
        }
        catch(Exception ex){Error="No se pudieron consultar las copias remotas: "+ex.Message;}
        finally{Ocupado=false;}
    }

    private async Task DescargarDriveAsync()
    {
        Error="";Mensaje="";if(!PuedeRestaurar){Error="No tenés permiso para preparar una restauración.";return;}
        if(CopiaDriveSeleccionada is null){Error="Seleccioná una copia remota.";return;}
        if(!DriveConectado){Error="Conectá Google Drive antes de descargar una copia.";return;}
        Ocupado=true;
        try
        {
            await _services.Autorizador.AutorizarAsync(_session()??throw new InvalidOperationException("La sesión no es válida."),PermisosRespaldos.Restaurar);
            var staging=_services.CoordinadorRespaldos.Gestor.DirectorioStaging;Directory.CreateDirectory(staging);
            var file=Path.Combine(staging,$"drive-{Guid.NewGuid():N}.libackup");
            try{await _services.CoordinadorRespaldos.Drive.DescargarAsync(CopiaDriveSeleccionada.Id,file);ArchivoRestauracion=file;Mensaje="Copia descargada a staging. Ingresá el código de recuperación para verificarla.";}
            catch{if(File.Exists(file))File.Delete(file);throw;}
        }
        catch(Exception ex){Error="No se pudo descargar la copia remota: "+ex.Message;}
        finally{Ocupado=false;}
    }

    private void SeleccionarArchivo()
    {
        if(!PuedeRestaurar)return;var dialog=new Microsoft.Win32.OpenFileDialog{Title="Seleccionar respaldo LocalInfantil",Filter="Respaldos LocalInfantil (*.libackup)|*.libackup",CheckFileExists=true,Multiselect=false};if(dialog.ShowDialog()==true)ArchivoRestauracion=dialog.FileName;
    }
    private async Task RestaurarAsync()
    {
        Error="";Mensaje="";if(!PuedeRestaurar){Error="No tenés permiso para restaurar respaldos.";return;}
        if(string.IsNullOrWhiteSpace(ArchivoRestauracion)||!File.Exists(ArchivoRestauracion)){Error="Seleccioná un archivo de respaldo válido.";return;}
        if(string.IsNullOrWhiteSpace(CodigoRestauracion)){Error="Ingresá el código de recuperación.";return;}
        if(string.IsNullOrWhiteSpace(ContrasenaDuena)){Error="Reautenticá a la Dueña antes de restaurar.";return;}
        Ocupado=true;string? staged=null;
        try
        {
            var id=_session()??throw new InvalidOperationException("La sesión no es válida.");
            await using(var tx=await _services.Fabrica.IniciarAsync())await _services.AutorizadorEnSesion.VerificarDuenaAsync(tx,_userName,ContrasenaDuena,PermisosRespaldos.Restaurar);
            if(await _services.ConsultasCaja.ObtenerActualAsync(id) is not null)throw new InvalidOperationException("Cerrá la caja antes de restaurar un respaldo.");
            staged=await _services.CoordinadorRespaldos.Gestor.PrepararRestauracionAsync(ArchivoRestauracion,CodigoRestauracion);
            await _services.CoordinadorRespaldos.Gestor.CrearAsync(MotivoRespaldo.Manual);
            var confirm=MessageBox.Show("Se verificó el respaldo y se creó una copia local de la base actual. La aplicación se cerrará para reemplazar la base; si falla la apertura, se restaurará automáticamente el archivo anterior. ¿Continuar?","Confirmar restauración",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No);
            if(confirm!=MessageBoxResult.Yes){File.Delete(staged);staged=null;Mensaje="Restauración cancelada. La base activa no se modificó.";return;}
            var active=_services.RutaBaseDatos;var prepared=staged;staged=null;ContrasenaDuena="";CodigoRestauracion="";IntercambiadorRestauracion.Iniciar(active,prepared);
        }
        catch(Exception ex){if(staged is not null&&File.Exists(staged))ArchivoRestauracion=staged;Error="No se pudo completar la preparación; la base activa no se reemplazó. "+ex.Message+(staged is not null&&File.Exists(staged)?" La copia descifrada quedó preservada en staging para revisión local segura.":string.Empty);}
        finally{Ocupado=false;ContrasenaDuena="";}
    }
}
