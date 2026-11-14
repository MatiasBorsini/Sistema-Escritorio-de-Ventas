using System.Windows;
using System.Windows.Threading;
using System.ComponentModel;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Infra;

/// <summary>
/// Marca consultas de vistas reutilizadas como obsoletas y las vuelve a cargar
/// de forma selectiva al activarse. Una vista propietaria de la escritura se
/// excluye: su propio comando ya actualiza su estado local.
/// </summary>
public sealed class CoordinadorActualizacionVistas : IDisposable
{
    private sealed class Registro(
        ViewModelBase vista,
        IReadOnlySet<AreaDatos> dependencias,
        Func<Task> cargar,
        Func<bool> puedeCargar,
        Func<bool> tieneError)
    {
        public ViewModelBase Vista { get; } = vista;
        public IReadOnlySet<AreaDatos> Dependencias { get; } = dependencias;
        public Func<Task> Cargar { get; } = cargar;
        public Func<bool> PuedeCargar { get; } = puedeCargar;
        public Func<bool> TieneError { get; } = tieneError;
        public SemaphoreSlim Exclusivo { get; } = new(1, 1);
        public long Version { get; set; }
        public bool Desactualizada { get; set; }
        public bool RecargaEnCurso { get; set; }
        public PropertyChangedEventHandler? ObservadorEstado { get; set; }
    }

    private readonly NotificadorCambiosDatos _notificador;
    private readonly Dispatcher? _dispatcher;
    private readonly Dictionary<ViewModelBase, Registro> _registros = new();
    private ViewModelBase? _activa;
    private bool _disposed;

    public CoordinadorActualizacionVistas(NotificadorCambiosDatos notificador)
    {
        _notificador = notificador;
        var dispatcherActual = System.Windows.Application.Current?.Dispatcher;
        _dispatcher = dispatcherActual?.CheckAccess() == true ? dispatcherActual : null;
        _notificador.CambioConfirmado += CambioConfirmado;
    }

    public void Registrar(
        ViewModelBase vista,
        IEnumerable<AreaDatos> dependencias,
        Func<Task> cargar,
        Func<bool>? puedeCargar = null,
        Func<bool>? tieneError = null,
        bool iniciarDesactualizada = false)
    {
        ArgumentNullException.ThrowIfNull(vista);
        ArgumentNullException.ThrowIfNull(dependencias);
        ArgumentNullException.ThrowIfNull(cargar);
        if (_registros.Remove(vista, out var anterior) && anterior.ObservadorEstado is not null)
            anterior.Vista.PropertyChanged -= anterior.ObservadorEstado;
        var registro = new Registro(vista, new HashSet<AreaDatos>(dependencias), cargar,
            puedeCargar ?? (() => true), tieneError ?? (() => false));
        registro.Desactualizada = iniciarDesactualizada;
        registro.ObservadorEstado = (_, _) => EstadoVistaCambiado(registro);
        vista.PropertyChanged += registro.ObservadorEstado;
        _registros[vista] = registro;
    }

    public void Activar(ViewModelBase vista)
    {
        _activa = vista;
        if (_registros.TryGetValue(vista, out var registro) && registro.Desactualizada)
            SolicitarRecarga(registro);
    }

    public Task ActivarAsync(ViewModelBase vista)
    {
        _activa = vista;
        return _registros.TryGetValue(vista, out var registro) && registro.Desactualizada
            ? SolicitarRecarga(registro)
            : Task.CompletedTask;
    }

    private void CambioConfirmado(object? sender, CambioDatosConfirmadoEventArgs e)
    {
        EjecutarEnUi(() =>
        {
            foreach (var registro in _registros.Values)
            {
                if (ReferenceEquals(registro.Vista, e.Emisor) || !registro.Dependencias.Overlaps(e.AreasAfectadas)) continue;
                registro.Desactualizada = true;
                registro.Version++;
                if (ReferenceEquals(registro.Vista, _activa) && registro.PuedeCargar())
                    SolicitarRecarga(registro);
            }
        });
    }

    private void EstadoVistaCambiado(Registro registro)
    {
        if (!_disposed && registro.Desactualizada && ReferenceEquals(registro.Vista, _activa)
            && registro.PuedeCargar() && !registro.TieneError())
            SolicitarRecarga(registro);
    }

    private Task SolicitarRecarga(Registro registro)
    {
        if (_disposed || registro.RecargaEnCurso) return Task.CompletedTask;
        registro.RecargaEnCurso = true;
        return RecargarSiNecesarioAsync(registro);
    }

    private async Task RecargarSiNecesarioAsync(Registro registro)
    {
        await registro.Exclusivo.WaitAsync();
        try
        {
            while (!_disposed && registro.Desactualizada && registro.PuedeCargar())
            {
                var version = registro.Version;
                await registro.Cargar();
                if (registro.TieneError()) return; // conserva la invalidez para un reintento al volver a entrar
                if (version == registro.Version)
                {
                    registro.Desactualizada = false;
                    continue;
                }
                if (!registro.PuedeCargar()) return; // hubo otra escritura mientras cargaba y la vista está ocupada
            }
        }
        catch
        {
            // El ViewModel conserva el mensaje de error. No se interrumpe navegación.
        }
        finally
        {
            registro.Exclusivo.Release();
            registro.RecargaEnCurso = false;
        }
    }

    private void EjecutarEnUi(Action accion)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) accion();
        else dispatcher.Invoke(accion, DispatcherPriority.DataBind);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notificador.CambioConfirmado -= CambioConfirmado;
        foreach (var registro in _registros.Values)
            if (registro.ObservadorEstado is not null)
                registro.Vista.PropertyChanged -= registro.ObservadorEstado;
        _registros.Clear();
        _activa = null;
    }
}
