using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

/// <summary>
/// Fila densa del historial de ventas: número estable, fecha, total y estado.
/// Anulada es mutable solo para reflejar una anulación recién confirmada; el
/// dato canónico sigue en la consulta.
/// </summary>
public sealed class FilaHistorialVenta : ViewModelBase
{
    private bool _anulada;

    public FilaHistorialVenta(ResumenVenta resumen)
    {
        Resumen = resumen;
        _anulada = resumen.Anulada;
    }

    public ResumenVenta Resumen { get; }

    public bool Anulada
    {
        get => _anulada;
        set
        {
            if (SetProperty(ref _anulada, value))
                OnPropertyChanged(nameof(EstadoTexto));
        }
    }

    public string EstadoTexto => Anulada ? "Anulada" : "Confirmada";
    public string FechaTexto => FechaCorta(Resumen.CreadaEn);
    public string TotalTexto => PosViewModel.FormatearArs(Resumen.TotalMinor);

    public static string FechaCorta(string creadoEn)
    {
        if (DateTimeOffset.TryParse(creadoEn, out var f))
            return f.ToLocalTime().ToString("dd/MM · HH:mm");
        return creadoEn;
    }
}

public static class FiltroVentas
{
    public const string Todas = "todas";
    public const string Confirmadas = "confirmadas";
    public const string Anuladas = "anuladas";
}

public sealed record OpcionFiltroVenta(string Valor, string Texto);

public sealed class VentasConsultaViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private readonly IDialogosVentas? _dialogos;
    private FichaVenta? _ficha;
    private FilaHistorialVenta? _filaSeleccionada;
    private string _motivo = string.Empty;
    private string _operacionIdAnulacion;
    private bool _confirmacionVisible;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private int _desplazamiento;
    private int _tamanoPagina = 20;
    private bool _haySiguiente;
    private bool _consultaRealizada;
    private string _textoBusqueda = string.Empty;
    private string _filtroEstado = FiltroVentas.Todas;
    private bool _seleccionAutomaticaEnCurso;
    private Task _cargaFichaSeleccionada = Task.CompletedTask;

    public VentasConsultaViewModel(ServiciosComerciales servicios, Func<string?> idSesion, IDialogosVentas? dialogos = null)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        _dialogos = dialogos;
        Historial = new ObservableCollection<FilaHistorialVenta>();
        Historial.CollectionChanged += (_, _) => OnPropertyChanged(nameof(MostrarEstadoVacio));
        _operacionIdAnulacion = servicios.GeneradorIds.NuevoId();
        CargarCommand = new AsyncRelayCommand(CargarAsync);
        SiguienteCommand = new AsyncRelayCommand(SiguienteAsync);
        AnteriorCommand = new AsyncRelayCommand(AnteriorAsync);
        VerTicketCommand = new AsyncRelayCommand(VerTicketAsync);
        SolicitarAnulacionCommand = new AsyncRelayCommand(SolicitarAnulacionAsync);
        ConfirmarAnulacionCommand = new AsyncRelayCommand(ConfirmarAnulacionAsync);
        CancelarAnulacionCommand = new AsyncRelayCommand(CancelarAnulacionAsync);
    }

    public ObservableCollection<FilaHistorialVenta> Historial { get; }
    public OpcionFiltroVenta[] FiltrosEstado { get; } =
    [
        new(FiltroVentas.Todas, "Todas"),
        new(FiltroVentas.Confirmadas, "Confirmadas"),
        new(FiltroVentas.Anuladas, "Anuladas"),
    ];
    public int[] TamanosPagina { get; } = [20, 50, 100];
    public string TextoBusqueda { get => _textoBusqueda; set => SetProperty(ref _textoBusqueda, value); }
    public string FiltroEstado { get => _filtroEstado; set => SetProperty(ref _filtroEstado, value); }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado
    {
        get => _ocupado;
        set
        {
            if (SetProperty(ref _ocupado, value))
                OnPropertyChanged(nameof(MostrarEstadoVacio));
        }
    }
    public bool ConfirmacionVisible { get => _confirmacionVisible; set => SetProperty(ref _confirmacionVisible, value); }
    public string Motivo { get => _motivo; set => SetProperty(ref _motivo, value); }
    public bool HaySiguiente { get => _haySiguiente; private set => SetProperty(ref _haySiguiente, value); }
    public bool HayAnterior => _desplazamiento > 0;
    public string PaginaTexto => $"Página {_desplazamiento / _tamanoPagina + 1}";
    public bool MostrarEstadoVacio => !Ocupado && _consultaRealizada && Historial.Count == 0;
    public bool MostrarEstadoSinSeleccion => Ficha is null;

    public FichaVenta? Ficha
    {
        get => _ficha;
        private set
        {
            if (SetProperty(ref _ficha, value))
            {
                OnPropertyChanged(nameof(HayFicha));
                OnPropertyChanged(nameof(MostrarEstadoSinSeleccion));
                OnPropertyChanged(nameof(EstadoFichaTexto));
                OnPropertyChanged(nameof(PuedeAnular));
            }
        }
    }

    public bool HayFicha => Ficha is not null;
    public string EstadoFichaTexto => Ficha?.Venta.Estado.Equals("anulada", StringComparison.OrdinalIgnoreCase) == true
        ? "Anulada"
        : "Confirmada";
    public bool PuedeAnular => Ficha is not null && !Ficha.Venta.Estado.Equals("anulada", StringComparison.Ordinal);

    public FilaHistorialVenta? FilaSeleccionada
    {
        get => _filaSeleccionada;
        set
        {
            if (SetProperty(ref _filaSeleccionada, value) && value is not null && !_seleccionAutomaticaEnCurso)
                _cargaFichaSeleccionada = CargarFichaAsync(value.Resumen.Id);
        }
    }

    public int TamanoPagina
    {
        get => _tamanoPagina;
        set
        {
            if (value < 1 || value > 100 || value == _tamanoPagina) return;
            _tamanoPagina = value;
            _desplazamiento = 0;
            OnPropertyChanged(nameof(TamanoPagina));
            OnPropertyChanged(nameof(PaginaTexto));
            _ = CargarAsync();
        }
    }

    public AsyncRelayCommand CargarCommand { get; }
    public AsyncRelayCommand SiguienteCommand { get; }
    public AsyncRelayCommand AnteriorCommand { get; }
    public AsyncRelayCommand VerTicketCommand { get; }
    public AsyncRelayCommand SolicitarAnulacionCommand { get; }
    public AsyncRelayCommand ConfirmarAnulacionCommand { get; }
    public AsyncRelayCommand CancelarAnulacionCommand { get; }

    public Task CargarAsyncPublic() => CargarAsync();
    public Task SiguienteAsyncPublic() => SiguienteAsync();
    public Task AnteriorAsyncPublic() => AnteriorAsync();
    public Task ConfirmarAnulacionAsyncPublic() => ConfirmarAnulacionAsync();
    public Task VerTicketAsyncPublic() => VerTicketAsync();
    public Task CargarFichaAsyncPublic(string ventaId) => CargarFichaAsync(ventaId);
    public Task CargarFichaSeleccionadaAsyncPublic() => _cargaFichaSeleccionada;

    private async Task CargarAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            var lista = await _servicios.ConsultasVentas.BuscarResumenAsync(TextoBusqueda ?? string.Empty, FiltroEstado, _tamanoPagina, _desplazamiento, _idSesion());
            _consultaRealizada = true;
            OnPropertyChanged(nameof(MostrarEstadoVacio));
            HaySiguiente = lista.Count > _tamanoPagina;
            Historial.Clear();
            SetProperty(ref _filaSeleccionada, null, nameof(FilaSeleccionada));
            Ficha = null;
            foreach (var r in lista.Take(_tamanoPagina))
                Historial.Add(new FilaHistorialVenta(r));
            OnPropertyChanged(nameof(PaginaTexto));
            if (Historial.Count > 0)
            {
                _seleccionAutomaticaEnCurso = true;
                try { FilaSeleccionada = Historial[0]; }
                finally { _seleccionAutomaticaEnCurso = false; }
                _cargaFichaSeleccionada = CargarFichaAsync(Historial[0].Resumen.Id);
                await _cargaFichaSeleccionada;
            }
            OnPropertyChanged(nameof(HayAnterior));
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task SiguienteAsync()
    {
        if (Ocupado || !HaySiguiente) return;
        _desplazamiento += _tamanoPagina;
        OnPropertyChanged(nameof(PaginaTexto));
        await CargarAsync();
    }

    private async Task AnteriorAsync()
    {
        if (Ocupado || _desplazamiento <= 0) return;
        _desplazamiento = Math.Max(0, _desplazamiento - _tamanoPagina);
        OnPropertyChanged(nameof(PaginaTexto));
        await CargarAsync();
    }

    private async Task CargarFichaAsync(string ventaId)
    {
        Error = null;
        try
        {
            Ficha = await _servicios.ConsultasVentas.ObtenerFichaAsync(ventaId, _idSesion());
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    private async Task VerTicketAsync()
    {
        if (Ficha is null) { Error = "Seleccioná una venta."; return; }
        Error = null;
        if (_dialogos is null) { Error = "Vista de comprobante no disponible."; return; }
        _dialogos.MostrarTicket(Ficha.Venta.Id);
        await Task.CompletedTask;
    }

    private Task SolicitarAnulacionAsync()
    {
        if (Ficha is null) { Error = "Seleccioná una venta."; return Task.CompletedTask; }
        if (!PuedeAnular) { Error = "La venta ya está anulada."; return Task.CompletedTask; }
        Error = null; Mensaje = null;
        ConfirmacionVisible = true;
        return Task.CompletedTask;
    }

    private Task CancelarAnulacionAsync()
    {
        ConfirmacionVisible = false;
        return Task.CompletedTask;
    }

    private async Task ConfirmarAnulacionAsync()
    {
        if (Ficha is null || !ConfirmacionVisible) return;
        if (Ocupado) return;
        if (string.IsNullOrWhiteSpace(Motivo)) { Error = "El motivo es obligatorio."; return; }
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            var id = Ficha.Venta.Id;
            await _servicios.CasoAnularVenta.EjecutarAsync(id, Motivo.Trim(), _operacionIdAnulacion, _idSesion());
            _servicios.NotificadorCambios.Publicar(this, AreaDatos.Ventas, AreaDatos.Ventas, AreaDatos.Stock, AreaDatos.Caja, AreaDatos.CuentaCorriente);
            Mensaje = $"Venta {Ficha.Venta.Numero} anulada. Stock restituido, historial conservado.";
            ConfirmacionVisible = false;
            Motivo = string.Empty;
            OnPropertyChanged(nameof(Motivo));
            _operacionIdAnulacion = _servicios.GeneradorIds.NuevoId();
            await CargarFichaAsync(id);
            var fila = Historial.FirstOrDefault(f => f.Resumen.Id == id);
            if (fila is not null)
                fila.Anulada = true;
        }
        catch (Exception ex) { Error = MensajeError(ex, "anular ventas"); }
        finally { Ocupado = false; }
    }

    private static string MensajeError(Exception ex, string? accion = null) => ex switch
    {
        LocalInfantil.Application.Abstractions.AccesoDenegadoException when accion is not null
            => $"No tenés permiso para {accion}. La Dueña puede habilitarlo desde Administración → Permisos.",
        LocalInfantil.Application.Abstractions.AccesoDenegadoException
            => "Permiso denegado. La Dueña puede habilitar permisos desde Administración → Permisos.",
        LocalInfantil.Application.Abstractions.SesionInvalidaException
            => "Sesión inválida. Iniciá sesión nuevamente.",
        _ => ex.Message,
    };
}
