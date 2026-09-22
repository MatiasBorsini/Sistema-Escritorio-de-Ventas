using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class RecepcionesConsultaViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private RecepcionRegistrada? _seleccionada;
    private IReadOnlyList<LineaRecepcion> _lineas = Array.Empty<LineaRecepcion>();
    private bool _estaRevertida;
    private string _motivo = string.Empty;
    private string _operacionIdReversion = string.Empty;
    private bool _confirmacionVisible;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private int _desplazamiento;
    private const int LimiteValor = 20;
    private string? _ultimaReversionRecepcionId;
    private string? _ultimoMotivoReversion;
    private string? _ultimoOperacionIdReversion;

    public RecepcionesConsultaViewModel(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        Recepciones = new ObservableCollection<RecepcionRegistrada>();
        _operacionIdReversion = servicios.GeneradorIds.NuevoId();
        CargarCommand = new AsyncRelayCommand(CargarAsync);
        SiguienteCommand = new AsyncRelayCommand(SiguienteAsync);
        AnteriorCommand = new AsyncRelayCommand(AnteriorAsync);
        VerDetalleCommand = new AsyncRelayCommand(VerDetalleAsync);
        SolicitarReversionCommand = new AsyncRelayCommand(SolicitarReversionAsync);
        ConfirmarReversionCommand = new AsyncRelayCommand(ConfirmarReversionAsync);
        CancelarReversionCommand = new AsyncRelayCommand(CancelarReversionAsync);
    }

    public ObservableCollection<RecepcionRegistrada> Recepciones { get; }
    public RecepcionRegistrada? Seleccionada
    {
        get => _seleccionada;
        set { if (SetProperty(ref _seleccionada, value) && value is not null) _ = VerDetalleAsync(); }
    }
    public IReadOnlyList<LineaRecepcion> Lineas { get => _lineas; set => SetProperty(ref _lineas, value); }
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
    public AsyncRelayCommand SolicitarReversionCommand { get; }
    public AsyncRelayCommand ConfirmarReversionCommand { get; }
    public AsyncRelayCommand CancelarReversionCommand { get; }
    public int Desplazamiento => _desplazamiento;
    public int LimitePage => LimiteValor;

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
            var lista = await _servicios.ConsultasRecepciones.ListarAsync(LimiteValor, _desplazamiento, _idSesion());
            Recepciones.Clear();
            foreach (var r in lista) Recepciones.Add(r);
            Mensaje = $"{lista.Count} recepción(es) (página {_desplazamiento / LimiteValor + 1}, desplazamiento {_desplazamiento}).";
            OnPropertyChanged(nameof(Desplazamiento));
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    public async Task SiguienteAsync()
    {
        _desplazamiento += LimiteValor;
        await CargarAsync();
    }

    public async Task AnteriorAsync()
    {
        if (_desplazamiento >= LimiteValor) _desplazamiento -= LimiteValor;
        else _desplazamiento = 0;
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
            Lineas = await _servicios.ConsultasRecepciones.ListarLineasAsync(Seleccionada.Id, _idSesion());
            EstaRevertida = await _servicios.ConsultasRecepciones.EstaRevertidaAsync(Seleccionada.Id, _idSesion());
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
