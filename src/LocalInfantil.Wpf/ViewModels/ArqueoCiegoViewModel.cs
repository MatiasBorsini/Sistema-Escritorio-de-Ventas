using System.Globalization;
using System.Windows.Input;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

/// <summary>La vista de primer paso recibe solo identificación y contado; el esperado
/// recién aparece en Resultado después de solicitar la comparación.</summary>
public sealed class ArqueoCiegoViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private readonly string _cajaSesionId;
    private readonly Func<Task> _volver;
    private readonly Func<Task> _cerrado;
    private string _efectivoContadoTexto = string.Empty;
    private string _observacion = string.Empty;
    private string _error = string.Empty;
    private string _mensaje = string.Empty;
    private ArqueoCaja? _resultado;
    private bool _ocupado;

    public ArqueoCiegoViewModel(ServiciosComerciales servicios, Func<string?> idSesion, string cajaSesionId,
        string usuarioApertura, string abiertaEn, Func<Task> volver, Func<Task> cerrado)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        _cajaSesionId = cajaSesionId;
        _volver = volver;
        _cerrado = cerrado;
        UsuarioApertura = usuarioApertura;
        AbiertaEn = DateTimeOffset.TryParse(abiertaEn, out var fecha) ? fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-AR")) : abiertaEn;
        ContinuarCommand = new AsyncRelayCommand(RevelarAsync);
        ConfirmarCommand = new AsyncRelayCommand(ConfirmarAsync);
        VolverCommand = new AsyncRelayCommand(_ => _volver());
    }

    public string UsuarioApertura { get; }
    public string AbiertaEn { get; }
    public ICommand ContinuarCommand { get; }
    public ICommand ConfirmarCommand { get; }
    public ICommand VolverCommand { get; }
    public bool Ocupado { get => _ocupado; private set => SetProperty(ref _ocupado, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Mensaje { get => _mensaje; private set => SetProperty(ref _mensaje, value); }
    public string EfectivoContadoTexto
    {
        get => _efectivoContadoTexto;
        set
        {
            if (SetProperty(ref _efectivoContadoTexto, value) && Resultado is not null)
            {
                Resultado = null;
                OnPropertyChanged(nameof(HayResultado));
            }
        }
    }
    public string Observacion { get => _observacion; set => SetProperty(ref _observacion, value); }
    public ArqueoCaja? Resultado
    {
        get => _resultado;
        private set
        {
            if (SetProperty(ref _resultado, value))
            {
                OnPropertyChanged(nameof(HayResultado));
                OnPropertyChanged(nameof(EfectivoEsperadoTexto));
                OnPropertyChanged(nameof(EfectivoContadoResultadoTexto));
                OnPropertyChanged(nameof(DiferenciaTexto));
                OnPropertyChanged(nameof(RequiereObservacion));
            }
        }
    }
    public bool HayResultado => Resultado is not null;
    public string EfectivoEsperadoTexto => Resultado is null ? string.Empty : PosViewModel.FormatearArs(Resultado.EfectivoEsperadoMinor);
    public string EfectivoContadoResultadoTexto => Resultado is null ? string.Empty : PosViewModel.FormatearArs(Resultado.EfectivoContadoMinor);
    public string DiferenciaTexto => Resultado is null ? string.Empty : PosViewModel.FormatearArs(Resultado.DiferenciaMinor);
    public bool RequiereObservacion => Resultado?.DiferenciaMinor != 0 && Resultado is not null;

    public async Task RevelarAsyncPublic() => await RevelarAsync();
    public async Task ConfirmarAsyncPublic() => await ConfirmarAsync();

    private async Task RevelarAsync()
    {
        Error = string.Empty; Mensaje = string.Empty;
        if (!ImporteArs.TryParse(EfectivoContadoTexto, out var contado))
        { Error = "Ingresá un efectivo contado válido en pesos."; return; }
        Ocupado = true;
        try
        {
            Resultado = await _servicios.CasoCerrarCaja.PrevisualizarAsync(contado, _idSesion());
        }
        catch (Exception ex) { Error = ex is LocalInfantil.Application.Abstractions.AccesoDenegadoException ? "No tenés permiso para cerrar la caja." : ex.Message; }
        finally { Ocupado = false; }
    }

    private async Task ConfirmarAsync()
    {
        Error = string.Empty; Mensaje = string.Empty;
        if (Resultado is null) { Error = "Primero revisá el resultado del arqueo."; return; }
        if (Resultado.DiferenciaMinor != 0 && string.IsNullOrWhiteSpace(Observacion))
        { Error = "Indicá una observación para explicar la diferencia de caja."; return; }
        Ocupado = true;
        try
        {
            var operacionId = _servicios.GeneradorIds.NuevoId();
            await _servicios.CasoCerrarCaja.EjecutarAsync(
                Resultado.SesionId,
                Resultado.EfectivoContadoMinor,
                Resultado.EfectivoEsperadoMinor,
                Observacion,
                operacionId,
                _idSesion());
            // El cierre comercial ya fue confirmado; el respaldo es posterior y nunca condiciona el cierre.
            await _servicios.CoordinadorRespaldos.SolicitarPostCierreCajaAsync(operacionId);
            await _cerrado();
        }
        catch (ArqueoCajaCambioException ex)
        {
            Mensaje = ex.Message;
            try { Resultado = await _servicios.CasoCerrarCaja.PrevisualizarAsync(Resultado.EfectivoContadoMinor, _idSesion()); }
            catch (Exception recarga) { Error = recarga.Message; }
        }
        catch (Exception ex) { Error = ex is LocalInfantil.Application.Abstractions.AccesoDenegadoException ? "No tenés permiso para cerrar la caja." : ex.Message; }
        finally { Ocupado = false; }
    }
}
