using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

/// <summary>
/// Fila densa del historial: resumen oficial por página (una sola lectura).
/// Revertida es mutable solo para reflejar una reversión recién confirmada;
/// el dato canónico sigue en la consulta.
/// </summary>
public sealed class FilaHistorialRecepcion : ViewModelBase
{
    private bool _revertida;

    public FilaHistorialRecepcion(ResumenRecepcion resumen)
    {
        Resumen = resumen;
        _revertida = resumen.Revertida;
    }

    public ResumenRecepcion Resumen { get; }

    public bool Revertida
    {
        get => _revertida;
        set
        {
            if (SetProperty(ref _revertida, value))
                OnPropertyChanged(nameof(EstadoTexto));
        }
    }

    public string Referencia => ReferenciaDe(Resumen);
    public string EstadoTexto => Revertida ? "Revertida" : "Confirmada";
    public string FechaTexto => FechaCorta(Resumen.CreadoEn);
    public string UnidadesTexto => $"{Resumen.Unidades} u.";

    /// <summary>
    /// Referencia humana: comprobante del proveedor si existe; si no,
    /// "Sin referencia" (proveedor + fecha dan contexto). Nunca se disfraza
    /// un ID técnico como referencia: el id interno solo se usa para
    /// navegación/consulta/diagnóstico. El dominio no tiene numeración.
    /// </summary>
    public static string ReferenciaDe(ResumenRecepcion r)
        => string.IsNullOrWhiteSpace(r.ComprobanteRef)
            ? "Sin referencia"
            : r.ComprobanteRef.Trim();

    public static string FechaCorta(string creadoEn)
    {
        if (DateTimeOffset.TryParse(creadoEn, out var f))
            return f.ToLocalTime().ToString("dd/MM · HH:mm");
        return creadoEn;
    }
}

public sealed class RecepcionesConsultaViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private readonly IDialogosRecepciones? _dialogos;
    private RecepcionRegistrada? _seleccionada;
    private FilaHistorialRecepcion? _filaSeleccionada;
    private IReadOnlyList<LineaRecepcion> _lineas = Array.Empty<LineaRecepcion>();
    private IReadOnlyList<LineaRecepcionDetalle> _lineasDetalle = Array.Empty<LineaRecepcionDetalle>();
    private bool _estaRevertida;
    private string _motivo = string.Empty;
    private string _operacionIdReversion = string.Empty;
    private bool _confirmacionVisible;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private int _desplazamiento;
    private int _tamanoPagina = 20;
    private bool _haySiguiente;
    private string _textoBusqueda = string.Empty;
    private string _filtroEstado = FiltroRecepciones.Todas;
    private string? _ultimaReversionRecepcionId;
    private string? _ultimoMotivoReversion;
    private string? _ultimoOperacionIdReversion;

    public RecepcionesConsultaViewModel(ServiciosComerciales servicios, Func<string?> idSesion, IDialogosRecepciones? dialogos = null)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        _dialogos = dialogos;
        Recepciones = new ObservableCollection<RecepcionRegistrada>();
        Historial = new ObservableCollection<FilaHistorialRecepcion>();
        _operacionIdReversion = servicios.GeneradorIds.NuevoId();
        CargarCommand = new AsyncRelayCommand(CargarAsync);
        SiguienteCommand = new AsyncRelayCommand(SiguienteAsync);
        AnteriorCommand = new AsyncRelayCommand(AnteriorAsync);
        VerDetalleCommand = new AsyncRelayCommand(VerDetalleAsync);
        AbrirDetalleCommand = new AsyncRelayCommand(AbrirDetalleAsync);
        SolicitarReversionCommand = new AsyncRelayCommand(SolicitarReversionAsync);
        ConfirmarReversionCommand = new AsyncRelayCommand(ConfirmarReversionAsync);
        CancelarReversionCommand = new AsyncRelayCommand(CancelarReversionAsync);
    }

    public ObservableCollection<RecepcionRegistrada> Recepciones { get; }
    public ObservableCollection<FilaHistorialRecepcion> Historial { get; }
    public string[] FiltrosEstado { get; } = [FiltroRecepciones.Todas, FiltroRecepciones.Confirmadas, FiltroRecepciones.Revertidas];
    public int[] TamanosPagina { get; } = [20, 50, 100];

    public RecepcionRegistrada? Seleccionada
    {
        get => _seleccionada;
        set { if (SetProperty(ref _seleccionada, value) && value is not null) _ = VerDetalleAsync(); }
    }

    public FilaHistorialRecepcion? FilaSeleccionada
    {
        get => _filaSeleccionada;
        set
        {
            if (SetProperty(ref _filaSeleccionada, value) && value is not null)
                Seleccionada = Recepciones.FirstOrDefault(r => r.Id == value.Resumen.Id);
        }
    }

    public string TextoBusqueda { get => _textoBusqueda; set => SetProperty(ref _textoBusqueda, value); }

    public string FiltroEstado
    {
        get => _filtroEstado;
        set
        {
            if (value != FiltroRecepciones.Todas && value != FiltroRecepciones.Confirmadas && value != FiltroRecepciones.Revertidas)
                return;
            if (SetProperty(ref _filtroEstado, value))
            {
                _desplazamiento = 0;
                OnPropertyChanged(nameof(Desplazamiento));
                _ = CargarAsync();
            }
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
            OnPropertyChanged(nameof(LimitePage));
            OnPropertyChanged(nameof(Desplazamiento));
            _ = CargarAsync();
        }
    }

    public bool HaySiguiente
    {
        get => _haySiguiente;
        private set => SetProperty(ref _haySiguiente, value);
    }

    public bool EsUltimaPagina => !HaySiguiente;
    public bool HayAnterior => _desplazamiento > 0;

    public string Resumen => Historial.Count == 0
        ? "Sin resultados."
        : $"{Historial.Count} recepción(es) · Página {_desplazamiento / _tamanoPagina + 1}" + (EsUltimaPagina ? " · Fin del listado" : string.Empty);

    public IReadOnlyList<LineaRecepcion> Lineas { get => _lineas; set => SetProperty(ref _lineas, value); }
    public IReadOnlyList<LineaRecepcionDetalle> LineasDetalle { get => _lineasDetalle; set => SetProperty(ref _lineasDetalle, value); }

    /// <summary>Detalle rápido: totales derivados de las líneas ya cargadas.</summary>
    public long UnidadesDetalle => LineasDetalle.Sum(l => l.Cantidad);

    public long? TotalDetalleExacto => LineasDetalle.Count == 0 || LineasDetalle.Any(l => !l.CostoUnitarioMinor.HasValue)
        ? null
        : LineasDetalle.Sum(l => l.Cantidad * l.CostoUnitarioMinor!.Value);

    public IReadOnlyList<LineaRecepcionDetalle> PrimerasLineas => LineasDetalle.Take(4).ToList();

    public string ResponsableDetalle => FilaSeleccionada?.Resumen.CreadoPorNombre ?? "—";

    public bool EstaRevertida { get => _estaRevertida; set => SetProperty(ref _estaRevertida, value); }
    public string Motivo { get => _motivo; set => SetProperty(ref _motivo, value); }
    public string OperacionIdReversion { get => _operacionIdReversion; set => SetProperty(ref _operacionIdReversion, value); }
    public bool ConfirmacionVisible { get => _confirmacionVisible; set => SetProperty(ref _confirmacionVisible, value); }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; set => SetProperty(ref _ocupado, value); }

    public AsyncRelayCommand CargarCommand { get; }
    public AsyncRelayCommand SiguienteCommand { get; }
    public AsyncRelayCommand AnteriorCommand { get; }
    public AsyncRelayCommand VerDetalleCommand { get; }
    public AsyncRelayCommand AbrirDetalleCommand { get; }
    public AsyncRelayCommand SolicitarReversionCommand { get; }
    public AsyncRelayCommand ConfirmarReversionCommand { get; }
    public AsyncRelayCommand CancelarReversionCommand { get; }
    public int Desplazamiento => _desplazamiento;
    public int LimitePage => _tamanoPagina;

    public Task AbrirDetalleAsyncPublic() => AbrirDetalleAsync();

    private Task AbrirDetalleAsync()
    {
        Error = null;
        if (FilaSeleccionada is null) { Error = "Seleccioná una recepción."; return Task.CompletedTask; }
        if (_dialogos is null) { Error = "Detalle completo no disponible."; return Task.CompletedTask; }
        _dialogos.MostrarDetalleRecepcion(FilaSeleccionada.Resumen);
        return Task.CompletedTask;
    }

    public async Task CargarAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            // Reconciliación: si hay reversión incierta pendiente, verificar antes de listar nueva página
            if (_ultimaReversionRecepcionId is not null)
            {
                try
                {
                    var existe = await _servicios.ConsultasRecepciones.ExisteReversionPorOperacionAsync(_ultimoOperacionIdReversion!, _idSesion());
                    var revertida = await _servicios.ConsultasRecepciones.EstaRevertidaAsync(_ultimaReversionRecepcionId, _idSesion());
                    if (existe || revertida)
                    {
                        Mensaje = $"Reconciliación: la reversión previa con operacion_id {_ultimoOperacionIdReversion} ya quedó registrada. Se limpia estado incierto.";
                        _ultimaReversionRecepcionId = null;
                        _ultimoMotivoReversion = null;
                        _ultimoOperacionIdReversion = null;
                        _operacionIdReversion = _servicios.GeneradorIds.NuevoId();
                        OnPropertyChanged(nameof(OperacionIdReversion));
                    }
                }
                catch { }
            }
            var lista = await _servicios.ConsultasRecepciones.BuscarResumenAsync(TextoBusqueda ?? string.Empty, _filtroEstado, _tamanoPagina, _desplazamiento, _idSesion());
            HaySiguiente = lista.Count > _tamanoPagina;
            Recepciones.Clear();
            Historial.Clear();
            SetProperty(ref _filaSeleccionada, null, nameof(FilaSeleccionada));
            foreach (var r in lista.Take(_tamanoPagina))
            {
                Recepciones.Add(new RecepcionRegistrada(r.Id, r.ProveedorId, r.OperacionId, r.ComprobanteRef, r.CreadoPorUsuarioId, r.CreadoEn));
                Historial.Add(new FilaHistorialRecepcion(r));
            }

            Mensaje = null;
            OnPropertyChanged(nameof(Resumen));
            OnPropertyChanged(nameof(EsUltimaPagina));
            OnPropertyChanged(nameof(HayAnterior));
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    public async Task SiguienteAsync()
    {
        if (!HaySiguiente) return;
        _desplazamiento += _tamanoPagina;
        OnPropertyChanged(nameof(Desplazamiento));
        OnPropertyChanged(nameof(HayAnterior));
        await CargarAsync();
    }

    public async Task AnteriorAsync()
    {
        if (_desplazamiento >= _tamanoPagina) _desplazamiento -= _tamanoPagina;
        else _desplazamiento = 0;
        OnPropertyChanged(nameof(Desplazamiento));
        OnPropertyChanged(nameof(HayAnterior));
        await CargarAsync();
    }

    public Task SiguienteAsyncPublic() => SiguienteAsync();
    public Task AnteriorAsyncPublic() => AnteriorAsync();

    private async Task VerDetalleAsync()
    {
        if (Seleccionada is null) return;
        Error = null; Ocupado = true;
        try
        {
            // 2 queries: detalle con variante resuelta + flag de reversión
            // fresca. LineasDetalle ya incluye cantidad/costo/moneda, por lo
            // que la lectura cruda por variante_id no se repite acá.
            LineasDetalle = await _servicios.ConsultasRecepciones.ListarLineasDetalleAsync(Seleccionada.Id, _idSesion());
            EstaRevertida = await _servicios.ConsultasRecepciones.EstaRevertidaAsync(Seleccionada.Id, _idSesion());
            if (FilaSeleccionada is not null && FilaSeleccionada.Resumen.Id == Seleccionada.Id)
                FilaSeleccionada.Revertida = EstaRevertida;
            OnPropertyChanged(nameof(UnidadesDetalle));
            OnPropertyChanged(nameof(TotalDetalleExacto));
            OnPropertyChanged(nameof(PrimerasLineas));
            OnPropertyChanged(nameof(ResponsableDetalle));
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    public Task SolicitarReversionAsyncPublic() => SolicitarReversionAsync();
    public Task CancelarReversionAsyncPublic() => CancelarReversionAsync();
    public Task ConfirmarReversionAsyncPublic() => ConfirmarReversionAsync();
    private Task SolicitarReversionAsync()
    {
        if (Seleccionada is null) { Error = "Seleccioná una recepción."; return Task.CompletedTask; }
        if (EstaRevertida) { Error = "Ya está revertida."; return Task.CompletedTask; }
        if (string.IsNullOrWhiteSpace(Motivo)) { Error = "Motivo obligatorio."; return Task.CompletedTask; }
        Error = null; Mensaje = null;
        ConfirmacionVisible = true;
        return Task.CompletedTask;
    }
    private Task CancelarReversionAsync() { ConfirmacionVisible = false; return Task.CompletedTask; }
    private async Task ConfirmarReversionAsync()
    {
        if (Seleccionada is null || !ConfirmacionVisible) return;
        if (string.IsNullOrWhiteSpace(Motivo)) { Error = "Motivo obligatorio."; return; }
        // Reconciliación: si hay reversión previa incierta, verificar antes de nueva
        if (_ultimaReversionRecepcionId is not null)
        {
            bool existeOp;
            bool yaRevertida;
            try
            {
                existeOp = await _servicios.ConsultasRecepciones.ExisteReversionPorOperacionAsync(_ultimoOperacionIdReversion!, _idSesion());
                yaRevertida = await _servicios.ConsultasRecepciones.EstaRevertidaAsync(_ultimaReversionRecepcionId, _idSesion());
            }
            catch (Exception ex)
            {
                Error = $"No se pudo verificar la reversión incierta (operacion_id {_ultimoOperacionIdReversion}): {MensajeError(ex)}. Conservando estado incierto.";
                return;
            }
            if (existeOp || yaRevertida)
            {
                var mismo = _ultimaReversionRecepcionId == Seleccionada.Id && _ultimoMotivoReversion == Motivo.Trim();
                if (mismo)
                {
                    Mensaje = $"Reconciliación: la reversión con operacion_id {_ultimoOperacionIdReversion} ya quedó registrada para esta recepción. No se crea duplicado.";
                    _ultimaReversionRecepcionId = null;
                    _ultimoMotivoReversion = null;
                    _ultimoOperacionIdReversion = null;
                    _operacionIdReversion = _servicios.GeneradorIds.NuevoId();
                    OnPropertyChanged(nameof(OperacionIdReversion));
                    ConfirmacionVisible = false;
                    return;
                }
                else
                {
                    Mensaje = $"Reconciliación: la reversión previa con operacion_id {_ultimoOperacionIdReversion} ya existe con otro contenido. Mostrando resultado anterior; nueva reversión requiere nueva confirmación explícita.";
                    _ultimaReversionRecepcionId = null;
                    _ultimoMotivoReversion = null;
                    _ultimoOperacionIdReversion = null;
                    _operacionIdReversion = _servicios.GeneradorIds.NuevoId();
                    OnPropertyChanged(nameof(OperacionIdReversion));
                    return;
                }
            }
        }
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoRevertirRecepcion.EjecutarAsync(Seleccionada.Id, Motivo.Trim(), _operacionIdReversion, _idSesion());
            Mensaje = "Reversión COMPLETA confirmada. Stock restado, historial conservado.";
            ConfirmacionVisible = false;
            _ultimaReversionRecepcionId = null;
            _ultimoMotivoReversion = null;
            _ultimoOperacionIdReversion = null;
            _operacionIdReversion = _servicios.GeneradorIds.NuevoId();
            OnPropertyChanged(nameof(OperacionIdReversion));
            await VerDetalleAsync();
            await CargarAsync();
        }
        catch (Exception ex)
        {
            _ultimaReversionRecepcionId = Seleccionada.Id;
            _ultimoMotivoReversion = Motivo.Trim();
            _ultimoOperacionIdReversion = _operacionIdReversion;
            var msg = ex.Message.Contains("ya fue revertida") || ex.Message.Contains("ya fue registrada")
                ? ex.Message
                : MensajeError(ex) + $" (reintentá con mismo operacion_id y mismo motivo: {_operacionIdReversion})";
            Error = msg;
        }
        finally { Ocupado = false; }
    }

    private static string MensajeError(Exception ex) => ex is LocalInfantil.Application.Abstractions.AccesoDenegadoException ? "Permiso denegado." : ex is LocalInfantil.Application.Abstractions.SesionInvalidaException ? "Sesión inválida." : ex.Message;
}
