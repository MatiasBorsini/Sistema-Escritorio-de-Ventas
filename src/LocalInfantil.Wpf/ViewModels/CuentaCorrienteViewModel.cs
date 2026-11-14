using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.CuentaCorriente;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed record FilaMovimientoCuenta(MovimientoCuentaCorriente Movimiento)
{
    public string Tipo => Movimiento.Tipo switch
    {
        "cargo_venta" => "Cargo por venta",
        "abono" => "Abono",
        "ajuste_positivo" => "Ajuste que aumenta deuda",
        "ajuste_negativo" => "Ajuste que reduce deuda",
        "credito_devolucion" => "Crédito de devolución",
        "cancelacion_deuda" => "Cancelación de deuda",
        _ => "Movimiento de cuenta",
    };
    public string Referencia => Movimiento.Referencia ?? "—";
    public string Fecha => DateTimeOffset.TryParse(Movimiento.Fecha, out var dt) ? dt.ToLocalTime().ToString("g") : Movimiento.Fecha;
    public long CargoMinor => Movimiento.ImporteMinor > 0 ? Movimiento.ImporteMinor : 0;
    public long AbonoMinor => Movimiento.ImporteMinor < 0 ? -Movimiento.ImporteMinor : 0;
}

public sealed class FilaPagoAbonoViewModel : ViewModelBase
{
    private string _medio = MediosPago.Efectivo;
    private string _importe = string.Empty;
    private string _recibido = string.Empty;
    public string Medio { get => _medio; set => SetProperty(ref _medio, value); }
    public string ImporteTexto { get => _importe; set => SetProperty(ref _importe, value); }
    public string RecibidoTexto { get => _recibido; set => SetProperty(ref _recibido, value); }
    public bool EsEfectivo => Medio == MediosPago.Efectivo;
    public long? ImporteMinor => FilaPagoViewModel.TryParseArs(ImporteTexto, out var n) ? n : null;
    public long? RecibidoMinor => EsEfectivo && FilaPagoViewModel.TryParseArs(RecibidoTexto, out var n) ? n : null;
}

public sealed class FilaAplicacionAbono(CargoCuentaCorriente cargo, long importe)
{
    public string Referencia => cargo.NumeroVenta;
    public long ImporteMinor { get; } = importe;
    public long PendienteMinor => cargo.PendienteMinor - ImporteMinor;
}

public sealed record FilaClienteCuentaViewModel(ClienteCuentaCorrienteListado Cliente)
{
    public string Id => Cliente.Id;
    public string NombreCompleto => Cliente.NombreCompleto;
    public string Telefono => Cliente.Telefono ?? "Sin teléfono";
    public long SaldoMinor => Cliente.SaldoMinor;
    public long? LimiteMinor => Cliente.LimiteMinor;
    public string Estado => string.Join(" · ", new[]
    {
        Cliente.SaldoMinor > 0 ? "Con deuda" : null,
        !Cliente.Activo ? "Inactivo" : Cliente.LimiteMinor is null ? "Fiado no habilitado" : "Fiado habilitado",
    }.Where(x => x is not null));
}

public sealed class CuentaCorrienteViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _s;
    private readonly Func<string?> _sesion;
    private string _textoCliente = string.Empty;
    private SugerenciaCliente? _cliente;
    private ResumenCuentaCorriente? _resumen;
    private bool _ocupado;
    private string? _error;
    private string? _mensaje;
    private bool _formularioAbono;
    private bool _formularioAjuste;
    private string _importeAbono = string.Empty;
    private string _importeAjuste = string.Empty;
    private string _motivoAjuste = string.Empty;
    private bool _aumentarDeuda = true;
    private VistaPreviaAbono? _previewAbono;
    private VistaPreviaAjuste? _previewAjuste;
    private string _limiteTexto = string.Empty;
    private string? _ultimoAbonoId;
    private SugerenciaCliente? _clientePendiente;
    private bool _clientePendienteActualizarBuscador;
    private bool _confirmacionCambioClienteVisible;
    private int _versionCarga;
    private FiltroClientesCuentaCorriente _filtroClientes = FiltroClientesCuentaCorriente.Todos;
    private FilaClienteCuentaViewModel? _clienteListadoSeleccionado;
    private int _paginaClientes = 1;
    private bool _hayMasClientes;
    private bool _ocupadoListado;
    private CancellationTokenSource? _busquedaClientesCts;
    private CancellationTokenSource? _consultaClientesCts;
    private long _secuenciaBusquedaClientes;
    private bool _confirmacionCambioOperacionVisible;
    private string? _operacionPendiente;
    private string? _clienteOperacionPendiente;
    private bool _confirmacionHabilitarFiadoVisible;
    private bool _confirmacionDeshabilitarFiadoVisible;

    public CuentaCorrienteViewModel(ServiciosComerciales s, Func<string?> sesion, bool puedeConsultar, bool puedeAbonar, bool puedeAjustar, bool puedeConfigurarLimite)
    {
        _s = s; _sesion = sesion; PuedeConsultar = puedeConsultar; PuedeAbonar = puedeAbonar; PuedeAjustar = puedeAjustar; PuedeConfigurarLimite = puedeConfigurarLimite;
        Clientes = new(); Movimientos = new(); PagosAbono = new() { new FilaPagoAbonoViewModel() }; AplicacionesPrevias = new();
        CargarCommand = new AsyncRelayCommand(CargarAsync);
        GuardarLimiteCommand = new AsyncRelayCommand(GuardarLimiteAsync);
        AbrirAbonoCommand = new RelayCommand(_ => AbrirAbono());
        AgregarPagoCommand = new RelayCommand(_ => PagosAbono.Add(new FilaPagoAbonoViewModel()));
        QuitarPagoCommand = new RelayCommand(p => { if (p is FilaPagoAbonoViewModel f && PagosAbono.Count > 1) PagosAbono.Remove(f); });
        PrevisualizarAbonoCommand = new AsyncRelayCommand(PrevisualizarAbonoAsync);
        ConfirmarAbonoCommand = new AsyncRelayCommand(ConfirmarAbonoAsync);
        AbrirAjusteCommand = new RelayCommand(_ => AbrirAjuste());
        PrevisualizarAjusteCommand = new AsyncRelayCommand(PrevisualizarAjusteAsync);
        ConfirmarAjusteCommand = new AsyncRelayCommand(ConfirmarAjusteAsync);
        CancelarFormularioCommand = new RelayCommand(_ => CancelarOperacionActual());
        ConfirmarCambioClienteCommand = new RelayCommand(_ =>
        {
            var pendiente = _clientePendiente;
            var actualizarBuscador = _clientePendienteActualizarBuscador;
            _clientePendiente = null;
            _clientePendienteActualizarBuscador = false;
            ConfirmacionCambioClienteVisible = false;
            AplicarSeleccion(pendiente, actualizarBuscador);
        });
        CancelarCambioClienteCommand = new RelayCommand(_ =>
        {
            _clientePendiente = null;
            _clientePendienteActualizarBuscador = false;
            ConfirmacionCambioClienteVisible = false;
        });
        AbrirEdicionLimiteCommand = new RelayCommand(_ =>
        {
            if (!PuedeConfigurarLimite || Cliente is null) return;
            SolicitarOperacion("limite");
        });
        CerrarEdicionLimiteCommand = new RelayCommand(_ => CancelarOperacionActual());
        DeshabilitarFiadoCommand = new RelayCommand(_ =>
        {
            if (!PuedeConfigurarLimite || Cliente is null || NoHabilitadoFiado) return;
            if (LimiteTexto != TextoLimiteActual()) { Error = "Guardá o cancelá el cambio del límite antes de deshabilitar el fiado."; return; }
            ConfirmacionDeshabilitarFiadoVisible = true;
        });
        ConfirmarDeshabilitarFiadoCommand = new AsyncRelayCommand(DeshabilitarFiadoAsync);
        ConfirmarHabilitarFiadoCommand = new AsyncRelayCommand(ConfirmarHabilitarFiadoAsync);
        CancelarConfirmacionHabilitarCommand = new RelayCommand(_ => ConfirmacionHabilitarFiadoVisible = false);
        CancelarConfirmacionDeshabilitarCommand = new RelayCommand(_ => ConfirmacionDeshabilitarFiadoVisible = false);
        ClientesPaginaAnteriorCommand = new RelayCommand(_ => { if (PaginaClientes > 1) { PaginaClientes--; _ = CargarClientesAsync(); } });
        ClientesPaginaSiguienteCommand = new RelayCommand(_ => { if (HayMasClientes) { PaginaClientes++; _ = CargarClientesAsync(); } });
        CambiarOperacionCommand = new RelayCommand(p => SolicitarOperacion(p as string));
        ConfirmarCambioOperacionCommand = new RelayCommand(_ => ConfirmarCambioOperacion());
        CancelarCambioOperacionCommand = new RelayCommand(_ => { _operacionPendiente = null; _clienteOperacionPendiente = null; ConfirmacionCambioOperacionVisible = false; });
        _ = CargarClientesAsync();
    }

    public ObservableCollection<FilaClienteCuentaViewModel> Clientes { get; }
    public ObservableCollection<FilaMovimientoCuenta> Movimientos { get; }
    public ObservableCollection<FilaPagoAbonoViewModel> PagosAbono { get; }
    public ObservableCollection<FilaAplicacionAbono> AplicacionesPrevias { get; }
    public bool PuedeConsultar { get; }
    public bool PuedeAbonar { get; }
    public bool PuedeAjustar { get; }
    public bool PuedeConfigurarLimite { get; }
    public SugerenciaCliente? Cliente
    {
        get => _cliente;
        private set
        {
            if (!SetProperty(ref _cliente, value)) return;
            OnPropertyChanged(nameof(HayCliente));
            OnPropertyChanged(nameof(PuedeAjustarCuenta));
            // Se invalidan de inmediato los datos anteriores; ninguna carga
            // anterior puede dejar movimientos o saldos de otra persona.
            _versionCarga++;
            Resumen = null;
            Movimientos.Clear();
            UltimoAbonoId = null;
            Error = null;
            Mensaje = null;
            FormularioAbono = FormularioAjuste = false;
            PreviewAbono = null; PreviewAjuste = null;
            ConfirmacionCambioOperacionVisible = ConfirmacionHabilitarFiadoVisible = ConfirmacionDeshabilitarFiadoVisible = false;
            _operacionPendiente = null; _clienteOperacionPendiente = null;
            AplicacionesPrevias.Clear();
            PagosAbono.Clear();
            PagosAbono.Add(new FilaPagoAbonoViewModel());
            ImporteAbono = ImporteAjuste = MotivoAjuste = string.Empty;
            EdicionLimiteVisible = false;
            OnPropertyChanged(nameof(EdicionLimiteVisible));
            OnPropertyChanged(nameof(HayCambiosSinGuardar));
            if (value is not null) _ = CargarAsync();
        }
    }
    public bool HayCliente => Cliente is not null;
    public bool HayCambiosSinGuardar
    {
        get
        {
            if (EdicionLimiteVisible && LimiteTexto != TextoLimiteActual()) return true;
            if (FormularioAbono && (PreviewAbono is not null || !string.IsNullOrWhiteSpace(ImporteAbono)
                || PagosAbono.Any(p => p.Medio != MediosPago.Efectivo || !string.IsNullOrWhiteSpace(p.ImporteTexto) || !string.IsNullOrWhiteSpace(p.RecibidoTexto)))) return true;
            return FormularioAjuste && (PreviewAjuste is not null || !AumentarDeuda
                || !string.IsNullOrWhiteSpace(ImporteAjuste) || !string.IsNullOrWhiteSpace(MotivoAjuste));
        }
    }
    public bool ConfirmacionCambioClienteVisible { get => _confirmacionCambioClienteVisible; private set => SetProperty(ref _confirmacionCambioClienteVisible, value); }
    public RelayCommand ConfirmarCambioClienteCommand { get; }
    public RelayCommand CancelarCambioClienteCommand { get; }
    public ResumenCuentaCorriente? Resumen { get => _resumen; private set { if (SetProperty(ref _resumen, value)) ActualizarResumen(); } }
    public string TextoCliente { get => _textoCliente; set { if (SetProperty(ref _textoCliente, value)) { PaginaClientes = 1; ProgramarBusquedaClientes(); } } }
    public FiltroClientesCuentaCorriente FiltroClientes { get => _filtroClientes; set { if (SetProperty(ref _filtroClientes, value)) { OnPropertyChanged(nameof(FiltroClientesIndice)); PaginaClientes = 1; _ = CargarClientesAsync(); } } }
    public int FiltroClientesIndice { get => (int)FiltroClientes; set => FiltroClientes = Enum.IsDefined(typeof(FiltroClientesCuentaCorriente), value) ? (FiltroClientesCuentaCorriente)value : FiltroClientesCuentaCorriente.Relevantes; }
    public FilaClienteCuentaViewModel? ClienteListadoSeleccionado
    {
        get => _clienteListadoSeleccionado;
        set
        {
            if (!SetProperty(ref _clienteListadoSeleccionado, value) || value is null) return;
            Seleccionar(new SugerenciaCliente(value.Id, value.NombreCompleto, value.Cliente.Telefono), actualizarBuscador: false);
            if (ConfirmacionCambioClienteVisible) { _clienteListadoSeleccionado = null; OnPropertyChanged(); }
        }
    }
    public string TextoPaginaClientes => $"Página {PaginaClientes}";
    public int PaginaClientes { get => _paginaClientes; private set { if (SetProperty(ref _paginaClientes, value)) { OnPropertyChanged(nameof(TextoPaginaClientes)); OnPropertyChanged(nameof(PuedePaginaAnterior)); } } }
    public bool HayMasClientes { get => _hayMasClientes; private set => SetProperty(ref _hayMasClientes, value); }
    public bool PuedePaginaAnterior => PaginaClientes > 1;
    public bool OcupadoListado { get => _ocupadoListado; private set { if (SetProperty(ref _ocupadoListado, value)) OnPropertyChanged(nameof(SinClientesListado)); } }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; private set => SetProperty(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; private set => SetProperty(ref _ocupado, value); }
    public bool FormularioAbono { get => _formularioAbono; private set => SetProperty(ref _formularioAbono, value); }
    public bool FormularioAjuste { get => _formularioAjuste; private set => SetProperty(ref _formularioAjuste, value); }
    public string ImporteAbono { get => _importeAbono; set => SetProperty(ref _importeAbono, value); }
    public string ImporteAjuste { get => _importeAjuste; set => SetProperty(ref _importeAjuste, value); }
    public string MotivoAjuste { get => _motivoAjuste; set => SetProperty(ref _motivoAjuste, value); }
    public bool AumentarDeuda { get => _aumentarDeuda; set => SetProperty(ref _aumentarDeuda, value); }
    public VistaPreviaAbono? PreviewAbono { get => _previewAbono; private set { if (SetProperty(ref _previewAbono, value)) OnPropertyChanged(nameof(PuedeConfirmarAbono)); } }
    public VistaPreviaAjuste? PreviewAjuste { get => _previewAjuste; private set => SetProperty(ref _previewAjuste, value); }
    public string LimiteTexto { get => _limiteTexto; set => SetProperty(ref _limiteTexto, value); }
    public string? UltimoAbonoId { get => _ultimoAbonoId; private set => SetProperty(ref _ultimoAbonoId, value); }
    public long ImportePagosAbono => PagosAbono.Sum(p => p.ImporteMinor ?? 0);
    public string[] Medios { get; } = [MediosPago.Efectivo, MediosPago.Transferencia, MediosPago.Debito, MediosPago.Credito, MediosPago.Billetera];
    public bool PuedeConfirmarAbono => PreviewAbono is not null;
    public bool PuedeRegistrarAbono => PuedeAbonar && Cliente is not null && (Resumen?.SaldoMinor ?? 0) > 0;
    public bool PuedeAjustarCuenta => PuedeAjustar && Cliente is not null && Resumen?.LimiteMinor is not null;
    public bool PuedeHabilitarFiado => PuedeConfigurarLimite && NoHabilitadoFiado;
    public bool PuedeEditarLimiteActual => PuedeConfigurarLimite && !NoHabilitadoFiado;
    public bool SinClientesListado => Clientes.Count == 0 && !OcupadoListado;
    public bool ConfirmacionCambioOperacionVisible { get => _confirmacionCambioOperacionVisible; private set => SetProperty(ref _confirmacionCambioOperacionVisible, value); }
    public bool ConfirmacionHabilitarFiadoVisible { get => _confirmacionHabilitarFiadoVisible; private set => SetProperty(ref _confirmacionHabilitarFiadoVisible, value); }
    public bool ConfirmacionDeshabilitarFiadoVisible { get => _confirmacionDeshabilitarFiadoVisible; private set => SetProperty(ref _confirmacionDeshabilitarFiadoVisible, value); }
    public int AjusteTipoIndex { get => AumentarDeuda ? 0 : 1; set => AumentarDeuda = value == 0; }
    public bool NoHabilitadoFiado => Resumen?.LimiteMinor is null;
    public bool EdicionLimiteVisible { get; private set; }
    public RelayCommand AbrirEdicionLimiteCommand { get; }
    public RelayCommand CerrarEdicionLimiteCommand { get; }
    public RelayCommand DeshabilitarFiadoCommand { get; }
    public AsyncRelayCommand ConfirmarDeshabilitarFiadoCommand { get; }
    public AsyncRelayCommand ConfirmarHabilitarFiadoCommand { get; }
    public RelayCommand CancelarConfirmacionHabilitarCommand { get; }
    public RelayCommand CancelarConfirmacionDeshabilitarCommand { get; }
    public AsyncRelayCommand CargarCommand { get; }
    public AsyncRelayCommand GuardarLimiteCommand { get; }
    public RelayCommand AbrirAbonoCommand { get; }
    public RelayCommand AgregarPagoCommand { get; }
    public RelayCommand QuitarPagoCommand { get; }
    public AsyncRelayCommand PrevisualizarAbonoCommand { get; }
    public AsyncRelayCommand ConfirmarAbonoCommand { get; }
    public RelayCommand AbrirAjusteCommand { get; }
    public AsyncRelayCommand PrevisualizarAjusteCommand { get; }
    public AsyncRelayCommand ConfirmarAjusteCommand { get; }
    public RelayCommand CancelarFormularioCommand { get; }
    public RelayCommand ClientesPaginaAnteriorCommand { get; }
    public RelayCommand ClientesPaginaSiguienteCommand { get; }
    public RelayCommand CambiarOperacionCommand { get; }
    public RelayCommand ConfirmarCambioOperacionCommand { get; }
    public RelayCommand CancelarCambioOperacionCommand { get; }

    public Task<DocumentoTicketAbono?> ObtenerTicketAbonoAsync(string id) => _s.CasoGenerarTicketAbono.EjecutarAsync(id, _sesion());

    public async Task CargarAsync()
    {
        if (Cliente is null || !PuedeConsultar) return;
        var id = Cliente.Id; var version = ++_versionCarga; Error = null; Ocupado = true;
        try
        {
            var resumen = await _s.ConsultasCuentaCorriente.ObtenerAsync(id, _sesion());
            if (version != _versionCarga || Cliente?.Id != id || resumen is null) return;
            Resumen = resumen;
            var rows = await _s.ConsultasCuentaCorriente.ListarMovimientosAsync(id, 100, 0, _sesion());
            if (version != _versionCarga || Cliente?.Id != id) return;
            Movimientos.Clear(); foreach (var row in rows) Movimientos.Add(new(row));
        }
        catch (Exception ex) { if (version == _versionCarga && Cliente?.Id == id) Error = ex.Message; }
        finally { if (version == _versionCarga) Ocupado = false; }
    }

    public async Task CargarClientesAsync(CancellationToken cancellationToken = default)
    {
        if (!PuedeConsultar) return;
        _consultaClientesCts?.Cancel(); _consultaClientesCts?.Dispose();
        _consultaClientesCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _consultaClientesCts.Token;
        var sequence = Interlocked.Increment(ref _secuenciaBusquedaClientes);
        Error = null;
        OcupadoListado = true;
        try
        {
            var offset = (PaginaClientes - 1) * 20;
            var rows = await _s.ConsultasCuentaCorriente.BuscarClientesAsync(TextoCliente, FiltroClientes, 20, offset, _sesion(), token);
            if (sequence != _secuenciaBusquedaClientes) return;
            Clientes.Clear();
            foreach (var row in rows.Take(20)) Clientes.Add(new(row));
            HayMasClientes = rows.Count > 20;
            _clienteListadoSeleccionado = Cliente is null ? null : Clientes.FirstOrDefault(x => x.Id == Cliente.Id);
            OnPropertyChanged(nameof(ClienteListadoSeleccionado));
            OnPropertyChanged(nameof(SinClientesListado));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (sequence == _secuenciaBusquedaClientes)
            {
                System.Diagnostics.Trace.TraceError("Error al consultar el listado de Cuenta corriente: {0}", ex);
                Error = $"No se pudo cargar el listado de clientes: {ex.Message}";
            }
        }
        finally { if (sequence == _secuenciaBusquedaClientes) OcupadoListado = false; }
    }

    public Task EsperarBusquedaClientesAsyncPublic() => CargarClientesAsync();

    private void ProgramarBusquedaClientes()
    {
        _busquedaClientesCts?.Cancel(); _busquedaClientesCts?.Dispose();
        var cts = _busquedaClientesCts = new CancellationTokenSource();
        var sequence = Interlocked.Increment(ref _secuenciaBusquedaClientes);
        _ = BuscarConDebounceAsync(sequence, cts.Token);
    }

    private async Task BuscarConDebounceAsync(long sequence, CancellationToken ct)
    {
        try { await Task.Delay(250, ct); }
        catch (OperationCanceledException) { return; }
        if (sequence != _secuenciaBusquedaClientes || ct.IsCancellationRequested) return;
        await CargarClientesAsync(ct);
    }

    private void Seleccionar(SugerenciaCliente? cliente, bool actualizarBuscador = true)
    {
        if (cliente is null) return;
        if (Cliente?.Id != cliente.Id && HayCambiosSinGuardar)
        {
            _clientePendiente = cliente;
            _clientePendienteActualizarBuscador = actualizarBuscador;
            ConfirmacionCambioClienteVisible = true;
            return;
        }
        AplicarSeleccion(cliente, actualizarBuscador);
    }

    private void SolicitarOperacion(string? operacion)
    {
        if (Cliente is null || operacion is null) return;
        if ((operacion == "abono" && !PuedeRegistrarAbono)
            || (operacion == "ajuste" && !PuedeAjustarCuenta)
            || (operacion == "limite" && !PuedeConfigurarLimite)) return;
        var actual = FormularioAbono ? "abono" : FormularioAjuste ? "ajuste" : EdicionLimiteVisible ? "limite" : null;
        if (actual == operacion) return;
        if (actual is not null && HayCambiosSinGuardar)
        {
            _operacionPendiente = operacion;
            _clienteOperacionPendiente = Cliente.Id;
            ConfirmacionCambioOperacionVisible = true;
            return;
        }
        AbrirOperacion(operacion);
    }

    private void ConfirmarCambioOperacion()
    {
        var destino = _operacionPendiente;
        var clienteId = _clienteOperacionPendiente;
        _operacionPendiente = null;
        _clienteOperacionPendiente = null;
        ConfirmacionCambioOperacionVisible = false;
        if (destino is null || Cliente?.Id != clienteId) return;
        LimpiarOperacionActual();
        AbrirOperacion(destino);
    }

    private void AbrirOperacion(string operacion)
    {
        LimpiarOperacionActual();
        switch (operacion)
        {
            case "abono":
                FormularioAbono = true; PagosAbono.Add(new FilaPagoAbonoViewModel());
                break;
            case "ajuste":
                FormularioAjuste = true; AumentarDeuda = true;
                break;
            case "limite":
                EdicionLimiteVisible = true; LimiteTexto = TextoLimiteActual(); OnPropertyChanged(nameof(EdicionLimiteVisible));
                break;
        }
        Error = null;
        OnPropertyChanged(nameof(HayCambiosSinGuardar));
    }

    private void LimpiarOperacionActual()
    {
        FormularioAbono = FormularioAjuste = false;
        EdicionLimiteVisible = false; OnPropertyChanged(nameof(EdicionLimiteVisible));
        PreviewAbono = null; PreviewAjuste = null; AplicacionesPrevias.Clear();
        ImporteAbono = ImporteAjuste = MotivoAjuste = string.Empty;
        PagosAbono.Clear();
        LimiteTexto = TextoLimiteActual();
        ConfirmacionHabilitarFiadoVisible = ConfirmacionDeshabilitarFiadoVisible = false;
    }

    private void CancelarOperacionActual()
    {
        LimpiarOperacionActual();
        ConfirmacionCambioOperacionVisible = false;
        _operacionPendiente = null; _clienteOperacionPendiente = null;
        OnPropertyChanged(nameof(HayCambiosSinGuardar));
    }

    private void AplicarSeleccion(SugerenciaCliente? cliente, bool actualizarBuscador)
    {
        Cliente = cliente;
        if (actualizarBuscador) TextoCliente = cliente?.NombreCompleto ?? string.Empty;
        _clienteListadoSeleccionado = cliente is null ? null : Clientes.FirstOrDefault(x => x.Id == cliente.Id);
        OnPropertyChanged(nameof(ClienteListadoSeleccionado));
    }

    private string TextoLimiteActual()
        => Resumen?.LimiteMinor is long limite
            ? ((decimal)limite / 100m).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("es-AR"))
            : string.Empty;
    private void ActualizarResumen()
    {
        OnPropertyChanged(nameof(NoHabilitadoFiado));
        OnPropertyChanged(nameof(PuedeRegistrarAbono));
        OnPropertyChanged(nameof(PuedeAjustarCuenta));
        OnPropertyChanged(nameof(PuedeHabilitarFiado));
        OnPropertyChanged(nameof(PuedeEditarLimiteActual));
        LimiteTexto = Resumen?.LimiteMinor is long l ? ((decimal)l / 100m).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("es-AR")) : string.Empty;
    }
    private async Task GuardarLimiteAsync()
    {
        if (Cliente is null || !PuedeConfigurarLimite) return; Error = null;
        try
        {
            if (!FilaPagoViewModel.TryParseArs(LimiteTexto, out var amount) || amount < 0)
                throw new ArgumentException("Ingresá un límite válido. Para habilitar el fiado, el importe debe ser mayor que cero.");
            if (NoHabilitadoFiado)
            {
                if (amount <= 0) throw new ArgumentException("El límite para habilitar ventas a cuenta debe ser mayor que cero.");
                ConfirmacionHabilitarFiadoVisible = true;
                return;
            }
            await PersistirLimiteAsync(amount);
        }
        catch (Exception ex) { Error = ex.Message; }
    }
    private long LeerLimitePositivo()
    {
        if (!FilaPagoViewModel.TryParseArs(LimiteTexto, out var amount) || amount <= 0)
            throw new ArgumentException("Ingresá un límite mayor que cero para habilitar ventas a cuenta.");
        return amount;
    }
    private async Task ConfirmarHabilitarFiadoAsync()
    {
        try { await PersistirLimiteAsync(LeerLimitePositivo()); }
        catch (Exception ex) { Error = ex.Message; }
    }
    private async Task PersistirLimiteAsync(long? limit)
    {
        if (Cliente is null || !PuedeConfigurarLimite) return;
        try
        {
            var clienteId = Cliente.Id;
            Ocupado = true;
            await _s.CasoConfigurarLimiteCredito.EjecutarAsync(clienteId, limit, _sesion());
            if (Cliente?.Id != clienteId) return;
            ConfirmacionHabilitarFiadoVisible = ConfirmacionDeshabilitarFiadoVisible = false;
            NotificarCuentaActualizada();
            Mensaje = limit is null ? "Se deshabilitaron nuevas ventas a cuenta. Las deudas y movimientos anteriores se conservaron." : "El límite de crédito del cliente se actualizó.";
            EdicionLimiteVisible = false; OnPropertyChanged(nameof(EdicionLimiteVisible));
            await CargarAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }
    private Task DeshabilitarFiadoAsync() => PersistirLimiteAsync(null);
    private void AbrirAbono() { if (!PuedeRegistrarAbono) return; SolicitarOperacion("abono"); }
    private IReadOnlyList<NuevoPago> LeerPagosAbono()
    {
        var result = new List<NuevoPago>();
        foreach (var row in PagosAbono.Where(p => !string.IsNullOrWhiteSpace(p.ImporteTexto)))
        {
            if (row.ImporteMinor is null || row.ImporteMinor <= 0) throw new ArgumentException("Revisá los importes de los medios de pago.");
            if (row.EsEfectivo && (row.RecibidoMinor is null || row.RecibidoMinor < row.ImporteMinor)) throw new ArgumentException("El efectivo recibido es menor al importe correspondiente.");
            result.Add(new(row.Medio, row.ImporteMinor.Value, row.EsEfectivo ? row.RecibidoMinor : null));
        }
        return result;
    }
    private async Task PrevisualizarAbonoAsync()
    {
        if (Cliente is null) return; Error = null;
        try
        {
            if (!FilaPagoViewModel.TryParseArs(ImporteAbono, out var importe) || importe <= 0) throw new ArgumentException("Ingresá un importe de abono válido.");
            var pagos = LeerPagosAbono(); PreviewAbono = await _s.ConsultasCuentaCorriente.PrevisualizarAbonoAsync(Cliente.Id, importe, pagos, _sesion());
            AplicacionesPrevias.Clear();
            foreach (var group in PreviewAbono.Aplicaciones.GroupBy(a => a.CargoId))
            {
                var cargo = PreviewAbono.Cargos.First(c => c.MovimientoId == group.Key);
                AplicacionesPrevias.Add(new(cargo, group.Sum(a => a.ImporteMinor)));
            }
        }
        catch (Exception ex) { Error = ex.Message; }
    }
    private async Task ConfirmarAbonoAsync()
    {
        if (Cliente is null || PreviewAbono is null) { Error = "Previsualizá la aplicación antes de confirmar."; return; }
        try
        {
            Ocupado = true;
            var data = new DatosAbono(Cliente.Id, PreviewAbono.ImporteMinor, LeerPagosAbono(), PreviewAbono.Aplicaciones, PreviewAbono.SaldoAnteriorMinor, _s.GeneradorIds.NuevoId(), _sesion());
            UltimoAbonoId = await _s.CasoRegistrarAbono.EjecutarAsync(data); NotificarCuentaActualizada(afectaCaja: true); Mensaje = "El abono quedó registrado."; FormularioAbono = false; PreviewAbono = null; await CargarAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }
    private void AbrirAjuste() { if (!PuedeAjustarCuenta) return; SolicitarOperacion("ajuste"); }
    private async Task PrevisualizarAjusteAsync()
    {
        var cliente = Cliente;
        if (cliente is null) { Error = "Seleccioná un cliente para continuar."; return; }
        if (!PuedeAjustarCuenta) { Error = "No se puede ajustar la deuda: el fiado no está habilitado para este cliente."; return; }
        Error = null;
        try
        {
            if (!FilaPagoViewModel.TryParseArs(ImporteAjuste, out var importe) || importe <= 0) throw new ArgumentException("Ingresá un importe válido.");
            if (string.IsNullOrWhiteSpace(MotivoAjuste)) throw new ArgumentException("El motivo es obligatorio.");
            PreviewAjuste = await _s.CasoAjustarCuentaCorriente.PrevisualizarAsync(cliente.Id, AumentarDeuda, importe, _sesion());
        }
        catch (Exception ex) { Error = ex.Message; }
    }
    private async Task ConfirmarAjusteAsync()
    {
        var cliente = Cliente;
        if (cliente is null) { Error = "Seleccioná un cliente para continuar."; return; }
        if (!PuedeAjustarCuenta) { Error = "No se puede ajustar la deuda: el fiado no está habilitado para este cliente."; return; }
        if (PreviewAjuste is null) { Error = "Previsualizá el ajuste antes de confirmar."; return; }
        try
        {
            if (!FilaPagoViewModel.TryParseArs(ImporteAjuste, out var importe)) return;
            Ocupado = true; await _s.CasoAjustarCuentaCorriente.EjecutarAsync(new(cliente.Id, AumentarDeuda, importe, MotivoAjuste, PreviewAjuste.SaldoAnteriorMinor, _s.GeneradorIds.NuevoId(), _sesion())); NotificarCuentaActualizada();
            Mensaje = "El ajuste quedó registrado."; FormularioAjuste = false; PreviewAjuste = null; await CargarAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private void NotificarCuentaActualizada(bool afectaCaja = false)
        => _s.NotificadorCambios.Publicar(this, AreaDatos.CuentaCorriente,
            afectaCaja ? [AreaDatos.CuentaCorriente, AreaDatos.Caja] : [AreaDatos.CuentaCorriente]);
}
