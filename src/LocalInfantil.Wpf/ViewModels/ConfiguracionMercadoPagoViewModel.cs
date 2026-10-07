using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

/// <summary>
/// Configuración MP solo Dueña: estado, credencial (nunca se muestra),
/// terminal con modo amigable, buscar/seleccionar/activar/probar/
/// desconectar. Sin IDs técnicos como información primaria.
/// </summary>
public sealed class ConfiguracionMercadoPagoViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private bool _ocupado;
    private string? _mensaje;
    private string? _error;
    private string _credencialTexto = string.Empty;
    private ConfigMercadoPago? _estado;
    private bool _conectado;
    private IReadOnlyList<TerminalPoint> _terminales = Array.Empty<TerminalPoint>();
    private TerminalPoint? _terminalSeleccionada;

    public ConfiguracionMercadoPagoViewModel(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        BuscarTerminalesCommand = new AsyncRelayCommand(BuscarTerminalesAsync);
        ConectarCommand = new AsyncRelayCommand(ConectarAsync);
        SeleccionarCommand = new AsyncRelayCommand(SeleccionarAsync);
        ActivarModoCommand = new AsyncRelayCommand(ActivarModoAsync);
        ProbarCommand = new AsyncRelayCommand(ProbarAsync);
        DesconectarCommand = new AsyncRelayCommand(DesconectarAsync);
    }

    public bool Ocupado { get => _ocupado; private set => SetProperty(ref _ocupado, value); }
    public string? Mensaje { get => _mensaje; private set => SetProperty(ref _mensaje, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public string CredencialTexto { get => _credencialTexto; set => SetProperty(ref _credencialTexto, value); }
    public bool Conectado { get => _conectado; private set { if (SetProperty(ref _conectado, value)) { OnPropertyChanged(nameof(EstadoTexto)); } } }
    public string EstadoTexto => Conectado ? "Configurado" : "No configurado";
    public ConfigMercadoPago? Estado
    {
        get => _estado;
        private set
        {
            if (SetProperty(ref _estado, value))
            {
                OnPropertyChanged(nameof(TerminalTexto));
                OnPropertyChanged(nameof(ModoTexto));
                OnPropertyChanged(nameof(SucursalTexto));
                OnPropertyChanged(nameof(UltimaComprobacionTexto));
            }
        }
    }
    public string TerminalTexto => Estado?.TerminalId is null ? "—" : "Terminal vinculada";
    public string TerminalDetalle => Estado?.TerminalId ?? "—";
    public string ModoTexto => Estado?.ModoTerminal ?? "—";
    public string SucursalTexto => Estado?.SucursalId is null ? "—" : $"Sucursal {Estado.SucursalId}";
    public string UltimaComprobacionTexto => Estado?.UltimaComprobacionUtc ?? "—";
    public IReadOnlyList<TerminalPoint> Terminales { get => _terminales; private set => SetProperty(ref _terminales, value); }
    public TerminalPoint? TerminalSeleccionada
    {
        get => _terminalSeleccionada;
        set { if (SetProperty(ref _terminalSeleccionada, value)) OnPropertyChanged(nameof(PuedeActivarModo)); }
    }
    public bool PuedeActivarModo => TerminalSeleccionada is not null && TerminalSeleccionada.ModoOperativo != "PDV";

    public AsyncRelayCommand BuscarTerminalesCommand { get; }
    public AsyncRelayCommand ConectarCommand { get; }
    public AsyncRelayCommand SeleccionarCommand { get; }
    public AsyncRelayCommand ActivarModoCommand { get; }
    public AsyncRelayCommand ProbarCommand { get; }
    public AsyncRelayCommand DesconectarCommand { get; }

    public async Task CargarAsync()
    {
        if (Ocupado) return;
        Ocupado = true; Error = null;
        try
        {
            Estado = await _servicios.CasoConfigurarMercadoPago.ObtenerEstadoAsync(_idSesion());
            Conectado = Estado.TerminalId is not null || await _servicios.CredencialMercadoPago.ExisteAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private async Task ConectarAsync()
    {
        if (Ocupado) return;
        Ocupado = true; Mensaje = null; Error = null;
        try
        {
            await _servicios.CasoConfigurarMercadoPago.ConectarAsync(CredencialTexto, _idSesion());
            CredencialTexto = string.Empty;
            Mensaje = "Credencial configurada.";
            await CargarAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private async Task BuscarTerminalesAsync()
    {
        if (Ocupado) return;
        Ocupado = true; Mensaje = null; Error = null;
        try
        {
            Terminales = await _servicios.CasoDescubrirTerminales.EjecutarAsync(_idSesion());
            if (Terminales.Count == 0) Mensaje = "No hay terminales en la cuenta.";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private async Task SeleccionarAsync()
    {
        if (Ocupado || TerminalSeleccionada is null) return;
        Ocupado = true; Mensaje = null; Error = null;
        try
        {
            await _servicios.CasoConfigurarMercadoPago.SeleccionarTerminalAsync(TerminalSeleccionada.Id, _idSesion());
            Mensaje = "Terminal seleccionada.";
            await CargarAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private async Task ActivarModoAsync()
    {
        if (Ocupado || TerminalSeleccionada is null) return;
        Ocupado = true; Mensaje = null; Error = null;
        try
        {
            var terminal = await _servicios.CasoConfigurarMercadoPago.ActivarModoIntegradoAsync(TerminalSeleccionada.Id, _idSesion());
            Mensaje = terminal.ModoOperativo == "PDV" ? "Modo integrado activado." : $"Modo actual: {terminal.ModoOperativo}.";
            await BuscarTerminalesAsync();
            await CargarAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private async Task ProbarAsync()
    {
        if (Ocupado) return;
        Ocupado = true; Mensaje = null; Error = null;
        try
        {
            await _servicios.CasoConfigurarMercadoPago.ProbarConexionAsync(_idSesion());
            Mensaje = "Conexión verificada.";
            await CargarAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private async Task DesconectarAsync()
    {
        if (Ocupado) return;
        Ocupado = true; Mensaje = null; Error = null;
        try
        {
            await _servicios.CasoConfigurarMercadoPago.DesconectarAsync(_idSesion());
            Terminales = Array.Empty<TerminalPoint>();
            TerminalSeleccionada = null;
            Mensaje = "Mercado Pago desconectado.";
            await CargarAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }
}
