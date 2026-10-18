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

    public CuentaCorrienteViewModel(ServiciosComerciales s, Func<string?> sesion, bool puedeConsultar, bool puedeAbonar, bool puedeAjustar, bool puedeConfigurarLimite)
    {
        _s = s; _sesion = sesion; PuedeConsultar = puedeConsultar; PuedeAbonar = puedeAbonar; PuedeAjustar = puedeAjustar; PuedeConfigurarLimite = puedeConfigurarLimite;
        Clientes = new(); Movimientos = new(); PagosAbono = new() { new FilaPagoAbonoViewModel() }; AplicacionesPrevias = new();
        MotorClientes = new MotorSugerencias<SugerenciaCliente>((q, ct) => s.ConsultasClientes.BuscarSugerenciasAsync(q, 20, _sesion(), ct));
        SeleccionarClienteCommand = new RelayCommand(p => Seleccionar(p as SugerenciaCliente));
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
        CancelarFormularioCommand = new RelayCommand(_ => { FormularioAbono = FormularioAjuste = false; PreviewAbono = null; PreviewAjuste = null; });
    }

    public ObservableCollection<SugerenciaCliente> Clientes { get; }
    public ObservableCollection<FilaMovimientoCuenta> Movimientos { get; }
    public ObservableCollection<FilaPagoAbonoViewModel> PagosAbono { get; }
    public ObservableCollection<FilaAplicacionAbono> AplicacionesPrevias { get; }
    public MotorSugerencias<SugerenciaCliente> MotorClientes { get; }
    public bool PuedeConsultar { get; }
    public bool PuedeAbonar { get; }
    public bool PuedeAjustar { get; }
    public bool PuedeConfigurarLimite { get; }
    public SugerenciaCliente? Cliente { get => _cliente; private set { if (SetProperty(ref _cliente, value)) { OnPropertyChanged(nameof(HayCliente)); _ = CargarAsync(); } } }
    public bool HayCliente => Cliente is not null;
    public ResumenCuentaCorriente? Resumen { get => _resumen; private set { if (SetProperty(ref _resumen, value)) ActualizarResumen(); } }
    public string TextoCliente { get => _textoCliente; set { if (SetProperty(ref _textoCliente, value)) MotorClientes.EstablecerTexto(value); } }
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
    public int AjusteTipoIndex { get => AumentarDeuda ? 0 : 1; set => AumentarDeuda = value == 0; }
    public bool NoHabilitadoFiado => Resumen?.LimiteMinor is null;
    public RelayCommand SeleccionarClienteCommand { get; }
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

    public Task<DocumentoTicketAbono?> ObtenerTicketAbonoAsync(string id) => _s.CasoGenerarTicketAbono.EjecutarAsync(id, _sesion());

    public async Task CargarAsync()
    {
        if (Cliente is null || !PuedeConsultar) return;
        var id = Cliente.Id; Error = null; Ocupado = true;
        try
        {
            var resumen = await _s.ConsultasCuentaCorriente.ObtenerAsync(id, _sesion());
            if (Cliente?.Id != id || resumen is null) return;
            Resumen = resumen;
            var rows = await _s.ConsultasCuentaCorriente.ListarMovimientosAsync(id, 100, 0, _sesion());
            if (Cliente?.Id != id) return;
            Movimientos.Clear(); foreach (var row in rows) Movimientos.Add(new(row));
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private void Seleccionar(SugerenciaCliente? cliente) { if (cliente is null) return; Cliente = cliente; TextoCliente = cliente.NombreCompleto; MotorClientes.Limpiar(); }
    private void ActualizarResumen()
    {
        OnPropertyChanged(nameof(NoHabilitadoFiado));
        LimiteTexto = Resumen?.LimiteMinor is long l ? ((decimal)l / 100m).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("es-AR")) : string.Empty;
    }
    private async Task GuardarLimiteAsync()
    {
        if (Cliente is null) return; Error = null;
        try
        {
            long? limit = null;
            if (!string.IsNullOrWhiteSpace(LimiteTexto))
            {
                if (!FilaPagoViewModel.TryParseArs(LimiteTexto, out var m)) throw new ArgumentException("Ingresá un límite válido o dejalo vacío para deshabilitar el fiado.");
                limit = m;
            }
            Ocupado = true; await _s.CasoConfigurarLimiteCredito.EjecutarAsync(Cliente.Id, limit, _sesion()); NotificarCuentaActualizada(); Mensaje = "El límite del cliente se actualizó."; await CargarAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }
    private void AbrirAbono() { if (!PuedeAbonar || Cliente is null) return; FormularioAjuste = false; FormularioAbono = true; PreviewAbono = null; ImporteAbono = string.Empty; PagosAbono.Clear(); PagosAbono.Add(new()); }
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
    private void AbrirAjuste() { if (!PuedeAjustar || Cliente is null) return; FormularioAbono = false; FormularioAjuste = true; PreviewAjuste = null; ImporteAjuste = MotivoAjuste = string.Empty; }
    private async Task PrevisualizarAjusteAsync()
    {
        if (Cliente is null) return; Error = null;
        try
        {
            if (!FilaPagoViewModel.TryParseArs(ImporteAjuste, out var importe) || importe <= 0) throw new ArgumentException("Ingresá un importe válido.");
            if (string.IsNullOrWhiteSpace(MotivoAjuste)) throw new ArgumentException("El motivo es obligatorio.");
            PreviewAjuste = await _s.CasoAjustarCuentaCorriente.PrevisualizarAsync(Cliente.Id, AumentarDeuda, importe, _sesion());
        }
        catch (Exception ex) { Error = ex.Message; }
    }
    private async Task ConfirmarAjusteAsync()
    {
        if (Cliente is null || PreviewAjuste is null) { Error = "Previsualizá el ajuste antes de confirmar."; return; }
        try
        {
            if (!FilaPagoViewModel.TryParseArs(ImporteAjuste, out var importe)) return;
            Ocupado = true; await _s.CasoAjustarCuentaCorriente.EjecutarAsync(new(Cliente.Id, AumentarDeuda, importe, MotivoAjuste, PreviewAjuste.SaldoAnteriorMinor, _s.GeneradorIds.NuevoId(), _sesion())); NotificarCuentaActualizada();
            Mensaje = "El ajuste quedó registrado."; FormularioAjuste = false; PreviewAjuste = null; await CargarAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private void NotificarCuentaActualizada(bool afectaCaja = false)
        => _s.NotificadorCambios.Publicar(this, AreaDatos.CuentaCorriente,
            afectaCaja ? [AreaDatos.CuentaCorriente, AreaDatos.Caja] : [AreaDatos.CuentaCorriente]);
}
