using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class ApartadoViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private string _skuBusqueda = string.Empty;
    private string _cantidadTexto = string.Empty;
    private string _motivo = string.Empty;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private bool _confirmacionBajaVisible;
    private ExistenciaClasificada? _existencia;
    private string _accionSeleccionada = "Apartar";

    public ApartadoViewModel(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        Historial = new ObservableCollection<ApartadoHistorial>();
        BuscarCommand = new AsyncRelayCommand(BuscarAsync);
        MotorSugerencias = new MotorSugerencias<SugerenciaVariante>((t, ct) =>
            _servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(t, 10, _idSesion(), ct));
        MotorSugerencias.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MotorSugerencias<SugerenciaVariante>.Texto)
                && MotorSugerencias.Texto != _skuBusqueda)
            {
                _skuBusqueda = MotorSugerencias.Texto;
                OnPropertyChanged(nameof(SkuBusqueda));
            }
        };
        SeleccionarSugerenciaCommand = new AsyncRelayCommand(p => SeleccionarSugerenciaAsync(p as SugerenciaVariante));
        SeleccionarAccionCommand = new RelayCommand(SeleccionarAccion);
        ApartarCommand = new AsyncRelayCommand(ApartarAsync);
        ReincorporarCommand = new AsyncRelayCommand(ReincorporarAsync);
        SolicitarBajaCommand = new AsyncRelayCommand(SolicitarBajaAsync);
        ConfirmarBajaCommand = new AsyncRelayCommand(ConfirmarBajaAsync);
        CancelarBajaCommand = new AsyncRelayCommand(CancelarBajaAsync);
    }

    public ObservableCollection<ApartadoHistorial> Historial { get; }
    /// <summary>
    /// Texto de búsqueda (código interno exacto o parcial). Sincronizado con
    /// el motor del buscador: Enter sin sugerencia usa este texto tal cual,
    /// sin obligar a elegir (ingreso rápido / lector de código de barras).
    /// </summary>
    public string SkuBusqueda
    {
        get => _skuBusqueda;
        set
        {
            if (SetProperty(ref _skuBusqueda, value) && MotorSugerencias is not null && MotorSugerencias.Texto != value)
                MotorSugerencias.EstablecerTexto(value);
        }
    }
    public string CantidadTexto { get => _cantidadTexto; set => SetProperty(ref _cantidadTexto, value); }
    public string Motivo { get => _motivo; set => SetProperty(ref _motivo, value); }
    public ExistenciaClasificada? Existencia { get => _existencia; set => SetProperty(ref _existencia, value); }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; set => SetProperty(ref _ocupado, value); }
    public bool ConfirmacionBajaVisible { get => _confirmacionBajaVisible; set => SetProperty(ref _confirmacionBajaVisible, value); }

    public string AccionSeleccionada
    {
        get => _accionSeleccionada;
        private set
        {
            if (SetProperty(ref _accionSeleccionada, value))
            {
                OnPropertyChanged(nameof(PanelApartarVisible));
                OnPropertyChanged(nameof(PanelReincorporarVisible));
                OnPropertyChanged(nameof(PanelBajaVisible));
                OnPropertyChanged(nameof(AccionApartarActiva));
                OnPropertyChanged(nameof(AccionReincorporarActiva));
                OnPropertyChanged(nameof(AccionBajaActiva));
            }
        }
    }

    /// <summary>Estado del segmented: solo lectura del selector existente.</summary>
    public bool AccionApartarActiva => AccionSeleccionada == "Apartar";
    public bool AccionReincorporarActiva => AccionSeleccionada == "Reincorporar";
    public bool AccionBajaActiva => AccionSeleccionada == "Baja";

    public bool PanelApartarVisible => AccionSeleccionada == "Apartar";

    public bool PanelReincorporarVisible => AccionSeleccionada == "Reincorporar";

    public bool PanelBajaVisible => AccionSeleccionada == "Baja";

    private void SeleccionarAccion(object? accion)
    {
        if (accion is string nombre && (nombre == "Apartar" || nombre == "Reincorporar" || nombre == "Baja"))
        {
            AccionSeleccionada = nombre;
        }
    }

    public AsyncRelayCommand BuscarCommand { get; }

    /// <summary>Buscador asistido de variantes (código o nombre).</summary>
    public MotorSugerencias<SugerenciaVariante> MotorSugerencias { get; }
    public AsyncRelayCommand SeleccionarSugerenciaCommand { get; }

    public Task SeleccionarSugerenciaAsyncPublic(SugerenciaVariante? s) => SeleccionarSugerenciaAsync(s);

    /// <summary>
    /// Salto desde sugerencia: completa el código real y carga la variante con
    /// el flujo existente (sin segundo Buscar manual).
    /// </summary>
    private async Task SeleccionarSugerenciaAsync(SugerenciaVariante? sugerencia)
    {
        if (sugerencia is null) return;
        if (Ocupado) return;
        SkuBusqueda = sugerencia.CodigoInterno;
        await BuscarAsync();
    }
    public RelayCommand SeleccionarAccionCommand { get; }
    public AsyncRelayCommand ApartarCommand { get; }
    public AsyncRelayCommand ReincorporarCommand { get; }
    public AsyncRelayCommand SolicitarBajaCommand { get; }
    public AsyncRelayCommand ConfirmarBajaCommand { get; }
    public AsyncRelayCommand CancelarBajaCommand { get; }

    public async Task BuscarAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null;
        if (string.IsNullOrWhiteSpace(SkuBusqueda)) { Error = "Ingresá un código interno."; return; }
        Ocupado = true;
        try
        {
            var ex = await _servicios.ConsultasInventario.ObtenerClasificadaAsync(SkuBusqueda.Trim(), _idSesion());
            if (ex is null) { Error = "Código interno no encontrado."; return; }
            Existencia = ex;
            Historial.Clear();
            foreach (var h in await _servicios.ConsultasInventario.HistorialApartadosPorSkuAsync(SkuBusqueda.Trim(), 20, 0, _idSesion()))
                Historial.Add(h);
            Mensaje = $"Físico {ex.Fisico} = disponible {ex.Disponible} + apartado {ex.Apartado}.";
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task RefrescarAsync()
    {
        if (Existencia is null) return;
        try
        {
            Existencia = await _servicios.ConsultasInventario.ObtenerClasificadaAsync(Existencia.Sku, _idSesion());
            Historial.Clear();
            if (Existencia is not null)
                foreach (var h in await _servicios.ConsultasInventario.HistorialApartadosPorSkuAsync(Existencia.Sku, 20, 0, _idSesion()))
                    Historial.Add(h);
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    private bool ValidarFormulario(out long cantidad)
    {
        cantidad = 0;
        if (Existencia is null) { Error = "Buscá una variante por código primero."; return false; }
        if (!long.TryParse(CantidadTexto.Trim(), out cantidad) || cantidad <= 0) { Error = "Cantidad inválida: entero mayor a cero."; return false; }
        if (string.IsNullOrWhiteSpace(Motivo)) { Error = "El motivo es obligatorio."; return false; }
        return true;
    }

    public async Task ApartarAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null;
        if (!ValidarFormulario(out var cantidad)) return;
        Ocupado = true;
        try
        {
            await _servicios.CasoApartarStock.EjecutarAsync(Existencia!.VarianteId, cantidad, Motivo.Trim(), _servicios.GeneradorIds.NuevoId(), _idSesion());
            Mensaje = $"Apartadas {cantidad} unidades (no vendibles). El total físico no cambió.";
            await RefrescarAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex, "apartar prendas"); }
        finally { Ocupado = false; }
    }

    public async Task ReincorporarAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null;
        if (!ValidarFormulario(out var cantidad)) return;
        Ocupado = true;
        try
        {
            await _servicios.CasoReincorporarStock.EjecutarAsync(Existencia!.VarianteId, cantidad, Motivo.Trim(), _servicios.GeneradorIds.NuevoId(), _idSesion());
            Mensaje = $"Reincorporadas {cantidad} unidades a disponible.";
            await RefrescarAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex, "reincorporar prendas"); }
        finally { Ocupado = false; }
    }

    private Task SolicitarBajaAsync()
    {
        if (Existencia is null) { Error = "Buscá una variante por código primero."; return Task.CompletedTask; }
        if (!long.TryParse(CantidadTexto.Trim(), out var cantidad) || cantidad <= 0) { Error = "Cantidad inválida: entero mayor a cero."; return Task.CompletedTask; }
        if (string.IsNullOrWhiteSpace(Motivo)) { Error = "El motivo es obligatorio."; return Task.CompletedTask; }
        Error = null; Mensaje = null;
        ConfirmacionBajaVisible = true;
        return Task.CompletedTask;
    }

    private Task CancelarBajaAsync()
    {
        ConfirmacionBajaVisible = false;
        return Task.CompletedTask;
    }

    public async Task ConfirmarBajaAsync()
    {
        if (!ConfirmacionBajaVisible || Existencia is null) return;
        if (Ocupado) return;
        if (!long.TryParse(CantidadTexto.Trim(), out var cantidad) || cantidad <= 0) { Error = "Cantidad inválida."; return; }
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoBajaDefinitiva.EjecutarAsync(Existencia.VarianteId, cantidad, Motivo.Trim(), _servicios.GeneradorIds.NuevoId(), _idSesion());
            Mensaje = $"Baja definitiva de {cantidad} unidades apartadas. Ya no existen físicamente.";
            ConfirmacionBajaVisible = false;
            await RefrescarAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex, "autorizar bajas definitivas"); }
        finally { Ocupado = false; }
    }

    public Task BuscarAsyncPublic() => BuscarAsync();
    public Task ApartarAsyncPublic() => ApartarAsync();
    public Task ReincorporarAsyncPublic() => ReincorporarAsync();
    public Task SolicitarBajaAsyncPublic() => SolicitarBajaAsync();
    public Task CancelarBajaAsyncPublic() => CancelarBajaAsync();
    public Task ConfirmarBajaAsyncPublic() => ConfirmarBajaAsync();

    private static string MensajeError(Exception ex, string? accion = null) => ex switch
    {
        LocalInfantil.Application.Abstractions.AccesoDenegadoException when accion is not null
            => $"No tenés permiso para {accion}. La Dueña puede habilitarlo desde Administración → Permisos.",
        LocalInfantil.Application.Abstractions.AccesoDenegadoException
            => "Permiso denegado. La Dueña puede habilitar permisos comerciales desde Administración → Permisos.",
        LocalInfantil.Application.Abstractions.SesionInvalidaException
            => "Sesión inválida. Iniciá sesión nuevamente.",
        _ => ex.Message,
    };
}
