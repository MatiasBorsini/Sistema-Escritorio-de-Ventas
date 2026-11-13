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
    private DateTime? _desde;
    private DateTime? _hasta;
    private CancellationTokenSource? _busquedaActual;
    private long _versionBusqueda;
    private long _versionFicha;
    private Task _busquedaProgramada = Task.CompletedTask;
    private bool _suspenderFiltros;
    private readonly Func<string, string, int, int, string?, DateOnly?, DateOnly?, CancellationToken, Task<IReadOnlyList<ResumenVenta>>> _buscarResumen;
    private readonly Func<TimeSpan, CancellationToken, Task> _esperaDebounce;
    private Task _cargaFichaSeleccionada = Task.CompletedTask;

    public VentasConsultaViewModel(ServiciosComerciales servicios, Func<string?> idSesion, IDialogosVentas? dialogos = null)
        : this(servicios, idSesion, dialogos, null, null)
    {
    }

    internal VentasConsultaViewModel(
        ServiciosComerciales servicios,
        Func<string?> idSesion,
        IDialogosVentas? dialogos,
        Func<string, string, int, int, string?, DateOnly?, DateOnly?, CancellationToken, Task<IReadOnlyList<ResumenVenta>>>? buscarResumen,
        Func<TimeSpan, CancellationToken, Task>? esperaDebounce = null)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        _dialogos = dialogos;
        _buscarResumen = buscarResumen ?? ((texto, estado, limite, desplazamiento, sesion, desde, hasta, ct) =>
            servicios.ConsultasVentas.BuscarResumenAsync(texto, estado, limite, desplazamiento, sesion, ct, desde, hasta));
        _esperaDebounce = esperaDebounce ?? ((demora, ct) => Task.Delay(demora, ct));
        Historial = new ObservableCollection<FilaHistorialVenta>();
        Historial.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(MostrarEstadoVacio));
            OnPropertyChanged(nameof(TextoEstadoVacio));
            OnPropertyChanged(nameof(TextoEstadoSinSeleccion));
        };
        _operacionIdAnulacion = servicios.GeneradorIds.NuevoId();
        CargarCommand = new AsyncRelayCommand(CargarAsync);
        LimpiarFiltrosCommand = new AsyncRelayCommand(LimpiarFiltrosAsync);
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
    public string TextoBusqueda
    {
        get => _textoBusqueda;
        set
        {
            if (SetProperty(ref _textoBusqueda, value ?? string.Empty))
                FiltrosCambiaron(conDebounce: true);
        }
    }
    public string FiltroEstado
    {
        get => _filtroEstado;
        set
        {
            if (value != FiltroVentas.Todas && value != FiltroVentas.Confirmadas && value != FiltroVentas.Anuladas) return;
            if (SetProperty(ref _filtroEstado, value))
                FiltrosCambiaron(conDebounce: false);
        }
    }
    public DateTime? Desde
    {
        get => _desde;
        set
        {
            if (SetProperty(ref _desde, value?.Date))
                FiltrosCambiaron(conDebounce: false);
        }
    }
    public DateTime? Hasta
    {
        get => _hasta;
        set
        {
            if (SetProperty(ref _hasta, value?.Date))
                FiltrosCambiaron(conDebounce: false);
        }
    }
    public string? Error
    {
        get => _error;
        set
        {
            if (!SetProperty(ref _error, value)) return;
            OnPropertyChanged(nameof(MostrarEstadoVacio));
            OnPropertyChanged(nameof(MostrarEstadoSinSeleccion));
        }
    }
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
    public bool MostrarEstadoVacio => !Ocupado && Error is null && _consultaRealizada && Historial.Count == 0;
    public string TextoEstadoVacio => HayFiltrosAplicados
        ? "No hay ventas que coincidan con estos filtros."
        : "Todavía no hay ventas registradas.";
    public bool MostrarEstadoSinSeleccion => Ficha is null && Error is null;
    public string TextoEstadoSinSeleccion => Historial.Count == 0
        ? "No hay una venta seleccionada."
        : "Elegí una venta del historial para consultar el detalle.";
    private bool HayFiltrosAplicados => !string.IsNullOrWhiteSpace(TextoBusqueda)
        || Desde.HasValue || Hasta.HasValue || FiltroEstado != FiltroVentas.Todas;

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
            if (SetProperty(ref _filaSeleccionada, value) && value is not null)
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
            OnPropertyChanged(nameof(TamanoPagina));
            FiltrosCambiaron(conDebounce: false);
        }
    }

    public AsyncRelayCommand CargarCommand { get; }
    public AsyncRelayCommand LimpiarFiltrosCommand { get; }
    public AsyncRelayCommand SiguienteCommand { get; }
    public AsyncRelayCommand AnteriorCommand { get; }
    public AsyncRelayCommand VerTicketCommand { get; }
    public AsyncRelayCommand SolicitarAnulacionCommand { get; }
    public AsyncRelayCommand ConfirmarAnulacionCommand { get; }
    public AsyncRelayCommand CancelarAnulacionCommand { get; }

    public Task CargarAsyncPublic() => CargarAsync();
    public Task RefrescarAsyncPublic() => CargarAsync();
    public Task EsperarBusquedaAsyncPublic() => _busquedaProgramada;
    public Task LimpiarFiltrosAsyncPublic() => LimpiarFiltrosAsync();
    public Task SiguienteAsyncPublic() => SiguienteAsync();
    public Task AnteriorAsyncPublic() => AnteriorAsync();
    public Task ConfirmarAnulacionAsyncPublic() => ConfirmarAnulacionAsync();
    public Task VerTicketAsyncPublic() => VerTicketAsync();
    public Task CargarFichaAsyncPublic(string ventaId) => CargarFichaAsync(ventaId);
    public Task CargarFichaSeleccionadaAsyncPublic() => _cargaFichaSeleccionada;

    private Task CargarAsync()
    {
        return IniciarBusqueda(conDebounce: false, reiniciarPagina: false, conservarSeleccion: true);
    }

    private async Task SiguienteAsync()
    {
        if (!HaySiguiente) return;
        _desplazamiento += _tamanoPagina;
        OnPropertyChanged(nameof(PaginaTexto));
        await CargarAsync();
    }

    private async Task AnteriorAsync()
    {
        if (_desplazamiento <= 0) return;
        _desplazamiento = Math.Max(0, _desplazamiento - _tamanoPagina);
        OnPropertyChanged(nameof(PaginaTexto));
        await CargarAsync();
    }

    private async Task CargarFichaAsync(string ventaId)
    {
        var version = ++_versionFicha;
        Error = null;
        try
        {
            var ficha = await _servicios.ConsultasVentas.ObtenerFichaAsync(ventaId, _idSesion());
            if (version == _versionFicha && FilaSeleccionada?.Resumen.Id == ventaId)
                Ficha = ficha;
        }
        catch (Exception ex) { if (version == _versionFicha) Error = MensajeError(ex); }
    }

    private void FiltrosCambiaron(bool conDebounce)
    {
        if (_suspenderFiltros) return;
        OnPropertyChanged(nameof(TextoEstadoVacio));
        ReiniciarPagina();
        _busquedaProgramada = IniciarBusqueda(conDebounce, reiniciarPagina: false, conservarSeleccion: false);
    }

    private Task IniciarBusqueda(bool conDebounce, bool reiniciarPagina, bool conservarSeleccion)
    {
        if (reiniciarPagina) ReiniciarPagina();
        try { _busquedaActual?.Cancel(); } catch (ObjectDisposedException) { }
        _busquedaActual?.Dispose();
        var cts = new CancellationTokenSource();
        _busquedaActual = cts;
        var version = ++_versionBusqueda;
        _busquedaProgramada = EjecutarBusquedaAsync(version, cts, conDebounce, conservarSeleccion);
        return _busquedaProgramada;
    }

    private async Task EjecutarBusquedaAsync(long version, CancellationTokenSource cts, bool conDebounce, bool conservarSeleccion)
    {
        var token = cts.Token;
        var texto = TextoBusqueda.Trim();
        var estado = FiltroEstado;
        var desplazamiento = _desplazamiento;
        var tamano = _tamanoPagina;
        var desde = Desde is { } fechaDesde ? DateOnly.FromDateTime(fechaDesde) : (DateOnly?)null;
        var hasta = Hasta is { } fechaHasta ? DateOnly.FromDateTime(fechaHasta) : (DateOnly?)null;
        Error = null;
        Mensaje = null;
        Ocupado = true;
        try
        {
            if (conDebounce) await _esperaDebounce(TimeSpan.FromMilliseconds(275), token);
            if (version != _versionBusqueda || token.IsCancellationRequested) return;

            if (desde.HasValue && hasta.HasValue && desde.Value > hasta.Value)
            {
                Error = "La fecha Desde no puede ser posterior a la fecha Hasta.";
                AplicarResultados(Array.Empty<ResumenVenta>(), null, tamano);
                return;
            }
            if (hasta == DateOnly.MaxValue)
            {
                Error = "La fecha Hasta debe permitir calcular el día siguiente.";
                AplicarResultados(Array.Empty<ResumenVenta>(), null, tamano);
                return;
            }

            var lista = await _buscarResumen(texto, estado, tamano, desplazamiento, _idSesion(), desde, hasta, token);
            if (version != _versionBusqueda || token.IsCancellationRequested) return;
            AplicarResultados(lista, conservarSeleccion ? FilaSeleccionada?.Resumen.Id : null, tamano);
            var fila = FilaSeleccionada;
            if (fila is not null)
            {
                _cargaFichaSeleccionada = CargarFichaAsync(fila.Resumen.Id);
                await _cargaFichaSeleccionada;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (version == _versionBusqueda)
            {
                Error = MensajeError(ex);
                AplicarResultados(Array.Empty<ResumenVenta>(), null, tamano);
            }
        }
        finally
        {
            if (version == _versionBusqueda)
                Ocupado = false;
            if (ReferenceEquals(_busquedaActual, cts))
            {
                _busquedaActual = null;
                cts.Dispose();
            }
        }
    }

    private void AplicarResultados(IReadOnlyList<ResumenVenta> lista, string? ventaSeleccionadaId, int tamano)
    {
        var anterior = ventaSeleccionadaId is null ? null : lista.FirstOrDefault(r => r.Id == ventaSeleccionadaId);
        var seleccionada = anterior ?? lista.FirstOrDefault();
        _consultaRealizada = true;
        HaySiguiente = lista.Count > tamano;
        Historial.Clear();
        foreach (var resumen in lista.Take(tamano))
            Historial.Add(new FilaHistorialVenta(resumen));
        SetProperty(ref _filaSeleccionada,
            seleccionada is null ? null : Historial.FirstOrDefault(f => f.Resumen.Id == seleccionada.Id),
            nameof(FilaSeleccionada));
        Ficha = null;
        _versionFicha++;
        OnPropertyChanged(nameof(MostrarEstadoVacio));
        OnPropertyChanged(nameof(TextoEstadoVacio));
        OnPropertyChanged(nameof(TextoEstadoSinSeleccion));
        OnPropertyChanged(nameof(PaginaTexto));
        OnPropertyChanged(nameof(HayAnterior));
        OnPropertyChanged(nameof(MostrarEstadoSinSeleccion));
    }

    private Task LimpiarFiltrosAsync()
    {
        _suspenderFiltros = true;
        try
        {
            TextoBusqueda = string.Empty;
            Desde = null;
            Hasta = null;
            FiltroEstado = FiltroVentas.Todas;
        }
        finally { _suspenderFiltros = false; }
        ReiniciarPagina();
        _busquedaProgramada = IniciarBusqueda(conDebounce: false, reiniciarPagina: false, conservarSeleccion: false);
        return _busquedaProgramada;
    }

    private void ReiniciarPagina()
    {
        if (_desplazamiento == 0) return;
        _desplazamiento = 0;
        OnPropertyChanged(nameof(PaginaTexto));
        OnPropertyChanged(nameof(HayAnterior));
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
