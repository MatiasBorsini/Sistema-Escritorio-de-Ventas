using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Clientes;
using LocalInfantil.Application.Features.CuentaCorriente;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

/// <summary>
/// Fila del carrito: variante resuelta con precio vigente congelado al
/// agregar y cantidad editable. Solo vive en la VM; cancelar no persiste.
/// </summary>
public sealed class FilaVentaViewModel : ViewModelBase
{
    private long _cantidad = 1;
    private long? _disponible;

    public string VarianteId { get; set; } = string.Empty;
    public string CodigoInterno { get; set; } = string.Empty;
    public string NombreProducto { get; set; } = string.Empty;
    public string? Talle { get; set; }
    public string? Color { get; set; }
    public long PrecioUnitarioMinor { get; set; }

    public string DetalleTalleColor => (Talle, Color) switch
    {
        (null, null) => "—",
        (null, var c) => c!,
        (var t, null) => t!,
        (var t, var c) => $"{t} · {c}",
    };

    public long Cantidad
    {
        get => _cantidad;
        set
        {
            var v = Math.Max(1, value);
            if (SetProperty(ref _cantidad, v))
            {
                OnPropertyChanged(nameof(CantidadTexto));
                OnPropertyChanged(nameof(SubtotalMinor));
            }
        }
    }

    /// <summary>Aviso hacia el POS cuando la cantidad excede el Disponible.</summary>
    public Action<string>? AvisoStock { get; set; }

    public string CantidadTexto
    {
        get => _cantidad.ToString();
        set
        {
            if (!long.TryParse(value.Trim(), out var c) || c <= 0)
            {
                OnPropertyChanged(nameof(CantidadTexto));
                return;
            }

            if (Disponible.HasValue && c > Disponible.Value)
            {
                AvisoStock?.Invoke($"Solo hay {Disponible.Value} unidades disponibles de esta variante.");
                OnPropertyChanged(nameof(CantidadTexto));
                return;
            }

            Cantidad = c;
        }
    }

    public long SubtotalMinor => Cantidad * PrecioUnitarioMinor;

    /// <summary>Disponible conocido al agregar (orientativo; la confirmación recalcula).</summary>
    public long? Disponible { get => _disponible; set => SetProperty(ref _disponible, value); }
}

/// <summary>
/// Fila de pago: un medio con su importe imputado; recibido solo efectivo.
/// </summary>
public sealed class FilaPagoViewModel : ViewModelBase
{
    private string _medio = MediosPago.Efectivo;
    private string _importeTexto = string.Empty;
    private string _recibidoTexto = string.Empty;
    private bool _estableciendoImportePrecargado;
    private bool _importeModificadoPorUsuario;

    public string Medio
    {
        get => _medio;
        set
        {
            if (SetProperty(ref _medio, value))
            {
                OnPropertyChanged(nameof(EsEfectivo));
                OnPropertyChanged(nameof(EsPoint));
                OnPropertyChanged(nameof(Presentacion));
                OnPropertyChanged(nameof(AyudaMedio));
                OnPropertyChanged(nameof(MostrarAyudaMedio));
                OnPropertyChanged(nameof(ImporteMinor));
                OnPropertyChanged(nameof(RecibidoMinor));
                OnPropertyChanged(nameof(VueltoMinor));
                OnPropertyChanged(nameof(VueltoPositivo));
                OnPropertyChanged(nameof(VueltoCero));
                OnPropertyChanged(nameof(EfectivoInsuficiente));
                OnPropertyChanged(nameof(SolicitaRecibido));
            }
        }
    }

    public bool EsEfectivo => _medio == MediosPago.Efectivo;
    public bool EsPoint => _medio == MediosPago.MercadoPago;
    public PresentacionMedioPagoPos Presentacion => PresentacionMedioPagoPos.Obtener(_medio);
    public string? AyudaMedio => Presentacion.Ayuda;
    public bool MostrarAyudaMedio => !string.IsNullOrWhiteSpace(AyudaMedio);

    public string ImporteTexto
    {
        get => _importeTexto;
        set
        {
            if (SetProperty(ref _importeTexto, value))
            {
                if (!_estableciendoImportePrecargado)
                    ImporteModificadoPorUsuario = true;
                OnPropertyChanged(nameof(ImporteMinor));
                OnPropertyChanged(nameof(VueltoMinor));
                OnPropertyChanged(nameof(VueltoPositivo));
                OnPropertyChanged(nameof(VueltoCero));
                OnPropertyChanged(nameof(EfectivoInsuficiente));
            }
        }
    }

    /// <summary>Evita que una precarga posterior reemplace una edición manual.</summary>
    public bool ImporteModificadoPorUsuario
    {
        get => _importeModificadoPorUsuario;
        private set => SetProperty(ref _importeModificadoPorUsuario, value);
    }

    internal void PrecargarImporte(string texto)
    {
        _estableciendoImportePrecargado = true;
        try { ImporteTexto = texto; }
        finally { _estableciendoImportePrecargado = false; }
    }

    public string RecibidoTexto
    {
        get => _recibidoTexto;
        set
        {
            if (SetProperty(ref _recibidoTexto, value))
            {
                OnPropertyChanged(nameof(RecibidoMinor));
                OnPropertyChanged(nameof(VueltoMinor));
                OnPropertyChanged(nameof(VueltoPositivo));
                OnPropertyChanged(nameof(VueltoCero));
                OnPropertyChanged(nameof(EfectivoInsuficiente));
                OnPropertyChanged(nameof(SolicitaRecibido));
            }
        }
    }

    public long? ImporteMinor => TryParseArs(_importeTexto, out var m) ? m : null;
    public long? RecibidoMinor => EsEfectivo && TryParseArs(_recibidoTexto, out var m) ? m : null;
    public long? VueltoMinor => ImporteMinor.HasValue && RecibidoMinor.HasValue ? RecibidoMinor.Value - ImporteMinor.Value : null;
    public bool VueltoPositivo => EsEfectivo && VueltoMinor > 0;
    public bool VueltoCero => EsEfectivo && VueltoMinor == 0;
    public bool EfectivoInsuficiente => EsEfectivo && ImporteMinor.HasValue && RecibidoMinor.HasValue && RecibidoMinor < ImporteMinor;

    /// <summary>Efectivo elegido pero sin recepción ingresada todavía: guía al usuario en vez de mostrar "Sin vuelto".</summary>
    public bool SolicitaRecibido => EsEfectivo && !RecibidoMinor.HasValue;

    /// <summary>
    /// Parsea ARS con el mismo criterio que Recepción: admite 1.234,50
    /// (punto miles, coma decimal), 1234,50 y 1234.50; rechaza el ambiguo
    /// 1,234.50 y más de dos decimales sin redondeo silencioso.
    /// </summary>
    public static bool TryParseArs(string texto, out long minor)
    {
        minor = 0;
        var raw = texto.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (raw.Contains('-')) return false;
        var t = raw.Replace("ARS", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(t)) return false;
        bool tieneComa = t.Contains(',');
        bool tienePunto = t.Contains('.');
        string normalizado;
        if (tieneComa && tienePunto)
        {
            if (t.LastIndexOf(',') < t.LastIndexOf('.')) return false;
            if (t.Count(c => c == ',') != 1) return false;
            normalizado = t.Replace(".", "").Replace(',', '.');
        }
        else if (tieneComa)
        {
            if (t.Count(c => c == ',') != 1) return false;
            normalizado = t.Replace(',', '.');
        }
        else
        {
            normalizado = t;
        }

        if (!decimal.TryParse(normalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec)) return false;
        if (dec < 0) return false;
        if (dec > (decimal)long.MaxValue / 100m) return false;
        var decRedondeado = Math.Round(dec, 2, MidpointRounding.AwayFromZero);
        if (dec != decRedondeado) return false;
        minor = (long)(dec * 100m);
        return true;
    }
}

/// <summary>Texto y guía propios del selector comercial del POS.</summary>
public sealed record PresentacionMedioPagoPos(string Valor, string Texto, string? Ayuda = null)
{
    private static readonly IReadOnlyList<PresentacionMedioPagoPos> Opciones = new[]
    {
        new PresentacionMedioPagoPos(MediosPago.Efectivo, "Efectivo"),
        new PresentacionMedioPagoPos(MediosPago.Transferencia, "Transferencia"),
        new PresentacionMedioPagoPos(MediosPago.Debito, "Débito — terminal externo", "Este cobro se realiza fuera de LocalInfantil. Aquí solo se registra el importe."),
        new PresentacionMedioPagoPos(MediosPago.Credito, "Crédito — terminal externo", "Este cobro se realiza fuera de LocalInfantil. Aquí solo se registra el importe."),
        new PresentacionMedioPagoPos(MediosPago.Billetera, "Billetera / QR — cobro externo", "Este cobro se realiza fuera de LocalInfantil. Aquí solo se registra el importe."),
        new PresentacionMedioPagoPos(MediosPago.MercadoPago, "Mercado Pago Point"),
    };

    public static IReadOnlyList<PresentacionMedioPagoPos> Todas => Opciones;

    public static PresentacionMedioPagoPos Obtener(string medio)
        => Opciones.FirstOrDefault(o => o.Valor == medio)
            ?? new PresentacionMedioPagoPos(medio, medio);
}

/// <summary>
/// Punto de venta: carrito en memoria + pagos + confirmación atómica.
/// El carrito nunca se persiste; cancelar solo limpia la VM (con
/// confirmación humana si tiene líneas). Tras confirmar, rota el
/// operacion_id y deja el foco en el buscador para la venta siguiente.
/// </summary>
public sealed class PosViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private readonly IDialogosVentas? _dialogos;
    private readonly Application.Features.MercadoPago.IniciarCobroPoint? _iniciarCobro;
    private readonly Application.Features.MercadoPago.ConsultarCobroPoint? _consultarCobro;
    private readonly Application.Features.MercadoPago.CancelarCobroPoint? _cancelarCobro;
    private readonly Application.Features.MercadoPago.RegistrarCobroManualPoint? _registrarManual;
    private Application.Features.MercadoPago.IniciarCobroPoint CasoIniciar => _iniciarCobro ?? _servicios.CasoIniciarCobroPoint;
    private Application.Features.MercadoPago.ConsultarCobroPoint CasoConsultar => _consultarCobro ?? _servicios.CasoConsultarCobroPoint;
    private Application.Features.MercadoPago.CancelarCobroPoint CasoCancelar => _cancelarCobro ?? _servicios.CasoCancelarCobroPoint;
    private Application.Features.MercadoPago.RegistrarCobroManualPoint CasoRegistrarManual => _registrarManual ?? _servicios.CasoRegistrarCobroManualPoint;
    private string _modoPoint;
    private bool _confirmacionManualPointAceptada;
    private string _referenciaComprobanteManual = string.Empty;
    private string _operacionId;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private bool _confirmacionCancelarVisible;
    private bool _cobroVisible;
    private bool _operacionPointIncierta;
    private bool _buscadorClienteVisible;
    private string? _ultimoNumero;
    private long? _ultimoTotal;
    private SugerenciaCliente? _clienteSeleccionado;
    private bool _esFiado;
    private bool _mostrarCobroParcialFiado;
    private long? _saldoClienteMinor;
    private long? _limiteClienteMinor;
    private bool _cargandoResumenCuenta;
    private bool _errorResumenCuenta;
    private int _versionResumenCuenta;
    private DateTime? _vencimiento;
    public bool PuedeAsociarCliente { get; }
    public bool PuedeVenderFiado { get; }
    public bool PuedeConsultarCuenta { get; }

    public PosViewModel(ServiciosComerciales servicios, Func<string?> idSesion, IDialogosVentas? dialogos = null, bool puedeAsociarCliente = true, bool puedeVenderFiado = false, bool puedeConsultarCuenta = false, Application.Features.MercadoPago.IniciarCobroPoint? iniciarCobro = null, Application.Features.MercadoPago.ConsultarCobroPoint? consultarCobro = null, Application.Features.MercadoPago.CancelarCobroPoint? cancelarCobro = null, Application.Features.MercadoPago.RegistrarCobroManualPoint? registrarManual = null)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        _dialogos = dialogos;
        _iniciarCobro = iniciarCobro;
        _consultarCobro = consultarCobro;
        _cancelarCobro = cancelarCobro;
        _registrarManual = registrarManual;
        _modoPoint = servicios.ObtenerModoPoint();
        PuedeAsociarCliente = puedeAsociarCliente;
        PuedeVenderFiado = puedeVenderFiado;
        PuedeConsultarCuenta = puedeConsultarCuenta;
        Filas = new ObservableCollection<FilaVentaViewModel>();
        Filas.CollectionChanged += FilasCambiadas;
        Pagos = new ObservableCollection<FilaPagoViewModel> { new FilaPagoViewModel() };
        Pagos.CollectionChanged += PagosCambiados;
        Recalcular();
        _operacionId = servicios.GeneradorIds.NuevoId();
        MotorSugerencias = new MotorSugerencias<SugerenciaVariante>((t, ct) =>
            _servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(t, 10, _idSesion(), ct),
            seleccionarPrimeraAlBuscar: false);
        MotorClientes = new MotorSugerencias<SugerenciaCliente>((t, ct) =>
            _servicios.ConsultasClientes.BuscarSugerenciasAsync(t, 10, _idSesion(), ct));
        SeleccionarClienteCommand = new RelayCommand(p => SeleccionarCliente(p as SugerenciaCliente));
        MostrarBuscadorClienteCommand = new RelayCommand(_ => { if (PuedeEditarVenta) BuscadorClienteVisible = true; });
        CambiarClienteCommand = new RelayCommand(_ => { if (PuedeEditarVenta) { ClienteSeleccionado = null; BuscadorClienteVisible = true; } });
        LimpiarClienteCommand = new RelayCommand(_ => { if (PuedeEditarVenta) { ClienteSeleccionado = null; BuscadorClienteVisible = false; } });
        SeleccionarSugerenciaCommand = new AsyncRelayCommand(p => AgregarSugerenciaAsync(p as SugerenciaVariante));
        BuscarDirectaCommand = new AsyncRelayCommand(() => AgregarDirectaAsync(MotorSugerencias.Texto));
        QuitarFilaCommand = new RelayCommand(p => { if (PuedeEditarVenta && p is FilaVentaViewModel f) { Filas.Remove(f); } });
        IncrementarCommand = new RelayCommand(p => { if (p is FilaVentaViewModel f) AjustarCantidad(f, 1); });
        DecrementarCommand = new RelayCommand(p => { if (p is FilaVentaViewModel f) AjustarCantidad(f, -1); });
        AgregarPagoCommand = new AsyncRelayCommand(AgregarPagoAsync);
        ElegirFiadoTotalCommand = new RelayCommand(_ => SeleccionarFiadoTotal());
        MostrarCobroParcialCommand = new RelayCommand(_ =>
        {
            if (!PuedeEditarVenta || !MostrarOpcionesFiado) return;
            EsFiado = true;
            _mostrarCobroParcialFiado = true;
            OnPropertyChanged(nameof(MostrarDistribucionPago));
            OnPropertyChanged(nameof(MostrarOpcionCobroParcial));
            NotificarTotales();
        });
        QuitarPagoCommand = new AsyncRelayCommand(p => QuitarPagoAsync(p as FilaPagoViewModel));
        ConfirmarCommand = new AsyncRelayCommand(ConfirmarAsync);
        CobrarEnPointCommand = new AsyncRelayCommand(CobrarEnPointAsync);
        RegistrarCobroManualCommand = new AsyncRelayCommand(RegistrarCobroManualAsync);
        CancelarCobroPointCommand = new AsyncRelayCommand(CancelarCobroPointAsync);
        SolicitarCancelarCommand = new RelayCommand(_ =>
        {
            if (CobroEnCurso) { Error = "Cancelá el cobro en curso antes de limpiar."; return; }
            if (Filas.Count == 0) return;
            Error = null; Mensaje = null;
            ConfirmacionCancelarVisible = true;
        });
        CancelarCancelarCommand = new RelayCommand(_ => ConfirmacionCancelarVisible = false);
        ConfirmarCancelarCommand = new RelayCommand(_ => { LimpiarVenta(); ConfirmacionCancelarVisible = false; });
        NuevaVentaCommand = new RelayCommand(_ =>
        {
            if (Filas.Count > 0) SolicitarCancelarCommand.Execute(null);
            else { LimpiarVenta(); UltimoNumero = null; UltimoTotal = null; }
            SolicitarFocoBuscador?.Invoke();
        });
        AbrirCobroCommand = new RelayCommand(_ =>
        {
            if (PuedeAbrirCobro)
            {
                Error = null;
                RefrescarModoPoint();
                foreach (var pago in Pagos) PrecargarSaldoEnFila(pago);
                CobroVisible = true;
            }
        });
        CerrarCobroCommand = new RelayCommand(_ =>
        {
            if (PuedeCerrarCobro) { CobroVisible = false; SolicitarFocoBuscador?.Invoke(); }
        });
        VerTicketCommand = new AsyncRelayCommand(VerTicketAsync);
    }

    /// <summary>La vista enfoca el buscador cuando se invoca.</summary>
    public event Action? SolicitarFocoBuscador;

    /// <summary>Motor del buscador asistido (modo Variante). El VM es dueño.</summary>
    public MotorSugerencias<SugerenciaVariante> MotorSugerencias { get; }
    public MotorSugerencias<SugerenciaCliente> MotorClientes { get; }
    private void SeleccionarFiadoTotal()
    {
        if (!PuedeEditarVenta || !MostrarOpcionesFiado) return;

        // La elección explícita de fiado total descarta la distribución anterior,
        // evitando que importes ocultos sigan computándose o vuelvan al confirmar.
        Pagos.Clear();
        Pagos.Add(new FilaPagoViewModel());
        _mostrarCobroParcialFiado = false;
        EsFiado = true;
        OnPropertyChanged(nameof(MostrarDistribucionPago));
        OnPropertyChanged(nameof(MostrarOpcionCobroParcial));
        NotificarTotales();
        Error = null;
    }

    public SugerenciaCliente? ClienteSeleccionado
    {
        get => _clienteSeleccionado;
        private set { if (SetProperty(ref _clienteSeleccionado, value)) { OnPropertyChanged(nameof(SinClienteSeleccionado)); OnPropertyChanged(nameof(MostrarBuscadorCliente)); OnPropertyChanged(nameof(MostrarBotonAsignarCliente)); OnPropertyChanged(nameof(PuedeQuitarCliente)); OnPropertyChanged(nameof(MostrarOpcionesFiado)); OnPropertyChanged(nameof(MostrarOpcionCobroParcial)); _ = CargarResumenCuentaAsync(); NotificarTotales(); } }
    }
    public bool EsFiado
    {
        get => _esFiado;
        set
        {
            if (!SetProperty(ref _esFiado, value)) return;
            if (!value) _mostrarCobroParcialFiado = false;
            OnPropertyChanged(nameof(MostrarDistribucionPago));
            OnPropertyChanged(nameof(MostrarOpcionCobroParcial));
            NotificarTotales();
        }
    }
    /// <summary>Estado de presentación para desplegar el cobro parcial; no altera importes.</summary>
    public bool MostrarDistribucionPago => !EsFiado || Pagado > 0 || _mostrarCobroParcialFiado;
    public bool MostrarOpcionCobroParcial => MostrarOpcionesFiado;
    public bool ModalidadFiadoTotalSeleccionada => EsFiado && !_mostrarCobroParcialFiado;
    public bool ModalidadFiadoParcialSeleccionada => EsFiado && _mostrarCobroParcialFiado;
    public bool PuedeEditarModalidad => PuedeEditarVenta;
    public RelayCommand MostrarCobroParcialCommand { get; private set; } = null!;
    public RelayCommand ElegirFiadoTotalCommand { get; private set; } = null!;
    public long? SaldoClienteMinor { get => _saldoClienteMinor; private set { if (SetProperty(ref _saldoClienteMinor, value)) NotificarTotales(); } }
    public long? LimiteClienteMinor { get => _limiteClienteMinor; private set { if (SetProperty(ref _limiteClienteMinor, value)) NotificarTotales(); } }
    public DateTime? Vencimiento { get => _vencimiento; set => SetProperty(ref _vencimiento, value); }
    public long DeudaNuevaMinor => EsFiado ? Math.Max(0, Restante) : 0;
    public long SaldoPosteriorMinor => (SaldoClienteMinor ?? 0) + DeudaNuevaMinor;
    public long? DisponibleCreditoMinor => LimiteClienteMinor.HasValue ? Math.Max(0, LimiteClienteMinor.Value - (SaldoClienteMinor ?? 0)) : null;
    public long? DisponibleCreditoPosteriorMinor => LimiteClienteMinor.HasValue ? LimiteClienteMinor.Value - SaldoPosteriorMinor : null;
    public long? DisponibleCreditoPosteriorMostradoMinor => DisponibleCreditoPosteriorMinor is long disponible
        ? Math.Max(0, disponible)
        : null;
    public bool MostrarResumenCuentaCobro => EsFiado && PuedeVenderFiado && PuedeConsultarCuenta
        && ClienteSeleccionado is not null && LimiteClienteMinor.HasValue && DeudaNuevaMinor > 0;
    public bool CargandoResumenCuenta => _cargandoResumenCuenta;
    public bool MostrarAvisoFiadoNoDisponible => !MostrarOpcionesFiado;
    public string MotivoFiadoNoDisponible => ClienteSeleccionado is null
        ? "Elegí un cliente para habilitar la venta a cuenta."
        : !PuedeVenderFiado || !PuedeConsultarCuenta
            ? "Tu usuario no tiene permiso para vender a cuenta."
            : CargandoResumenCuenta
                ? "Consultando el límite de crédito…"
                : _errorResumenCuenta
                    ? "No se pudo verificar la cuenta; no se puede habilitar fiado ahora."
                    : !LimiteClienteMinor.HasValue
                        ? "Fiado no habilitado: el cliente no tiene un límite autorizado."
                        : string.Empty;
    public string TextoModalidadCuenta => Pagado == 0
        ? "Toda la venta queda a cuenta."
        : "Pago parcial: el resto queda a cuenta.";
    public long CobradoAhoraMinor => Pagado;
    public string EstadoCuentaCorriente => !EsFiado ? string.Empty
        : !LimiteClienteMinor.HasValue ? "Fiado no habilitado: este cliente no tiene un límite autorizado."
        : Pagado > Total ? "Los importes cobrados superan el total. Ajustalos antes de continuar."
        : Restante <= 0 ? "No queda importe a financiar. Para una venta parcial, ajustá los importes a lo que cobrás ahora."
        : SaldoPosteriorMinor > LimiteClienteMinor.Value ? $"La deuda supera el crédito disponible por {FormatearArs(SaldoPosteriorMinor - LimiteClienteMinor.Value)}. Ajustá el importe a financiar."
        : Pagado == 0 ? "Toda la venta queda a cuenta. No ingreses un cobro ficticio; podés dejar vacíos los importes de pago."
        : $"Pago parcial: cobrás {FormatearArs(Pagado)} ahora y quedan {FormatearArs(DeudaNuevaMinor)} a cuenta.";
    public bool SinClienteSeleccionado => ClienteSeleccionado is null;
    public bool MostrarBotonAsignarCliente => SinClienteSeleccionado && !BuscadorClienteVisible;
    public bool BuscadorClienteVisible
    {
        get => _buscadorClienteVisible;
        private set
        {
            if (SetProperty(ref _buscadorClienteVisible, value))
            {
                OnPropertyChanged(nameof(MostrarBuscadorCliente));
                OnPropertyChanged(nameof(MostrarBotonAsignarCliente));
            }
        }
    }

    public bool MostrarBuscadorCliente => PuedeAsociarCliente && SinClienteSeleccionado && BuscadorClienteVisible;
    public bool PuedeQuitarCliente => PuedeAsociarCliente && ClienteSeleccionado is not null;
    public bool MostrarOpcionesFiado => PuedeVenderFiado && PuedeConsultarCuenta && ClienteSeleccionado is not null
        && !CargandoResumenCuenta && !_errorResumenCuenta && LimiteClienteMinor is > 0;
    public RelayCommand SeleccionarClienteCommand { get; }
    public RelayCommand LimpiarClienteCommand { get; }
    public RelayCommand MostrarBuscadorClienteCommand { get; }
    public RelayCommand CambiarClienteCommand { get; }

    public ObservableCollection<FilaVentaViewModel> Filas { get; }
    public ObservableCollection<FilaPagoViewModel> Pagos { get; }
    public bool TieneVariosPagos => Pagos.Count > 1;

    /// <summary>
    /// Medios manuales más Mercado Pago Point. MP no se confirma directo:
    /// requiere el flujo saga (Cobrar en Point) con order processed.
    /// </summary>
    public IReadOnlyList<PresentacionMedioPagoPos> MediosDisponibles => EsModoDesactivado
        ? PresentacionMedioPagoPos.Todas.Where(o => o.Valor != MediosPago.MercadoPago).ToArray()
        : PresentacionMedioPagoPos.Todas;

    /// <summary>Modalidad operativa local de Point (integrado/manual/desactivado).</summary>
    public string ModoPoint => _modoPoint;
    public bool EsModoIntegrado => _modoPoint == Application.Features.MercadoPago.ModosPoint.Integrado;
    public bool EsModoManual => _modoPoint == Application.Features.MercadoPago.ModosPoint.Manual;
    public bool EsModoDesactivado => _modoPoint == Application.Features.MercadoPago.ModosPoint.Desactivado;

    public bool TieneMercadoPago => Pagos.Any(p => p.Medio == MediosPago.MercadoPago);
    public bool MostrarAyudaPoint => TieneMercadoPago;
    public bool MostrarPanelManualPoint => EsModoManual && TieneMercadoPago;
    public bool MostrarAccionPointIntegrada => TieneMercadoPago && EsModoIntegrado;
    public bool MostrarAccionPointManual => TieneMercadoPago && EsModoManual;
    public string AyudaPoint => "El cobro se enviará a la terminal Point configurada. El cliente completa el pago en la terminal. Las opciones disponibles se completan en la terminal.";
    public string AyudaPointManual => "Cobrá este importe directamente en la terminal Point y confirmá la aprobación observada. El registro queda pendiente de conciliación: no reemplaza la verificación de Mercado Pago.";
    public string AyudaPointMostrada => EsModoManual ? AyudaPointManual : AyudaPoint;
    public string TextoAccionPoint => $"Cobrar {FormatearArs(Total)} en Point";
    public string TextoAccionManual => $"Registrar cobro manual de {FormatearArs(Total)}";

    /// <summary>La empleada declara que el Point aprobó este importe.</summary>
    public bool ConfirmacionManualPointAceptada
    {
        get => _confirmacionManualPointAceptada;
        set
        {
            if (SetProperty(ref _confirmacionManualPointAceptada, value))
            {
                OnPropertyChanged(nameof(PuedeRegistrarCobroManual));
                OnPropertyChanged(nameof(TextoEstadoPago));
            }
        }
    }

    /// <summary>Referencia opcional del comprobante de la terminal.</summary>
    public string ReferenciaComprobanteManual
    {
        get => _referenciaComprobanteManual;
        set => SetProperty(ref _referenciaComprobanteManual, value);
    }

    /// <summary>
    /// Habilita el cobro manual: modalidad manual, carrito válido, Σ medios ==
    /// total, exactamente un pago Point, sin fiado y con la declaración humana.
    /// </summary>
    public bool PuedeRegistrarCobroManual
    {
        get
        {
            if (!EsModoManual || !PuedeEditarVenta || Ocupado || CobroEnCurso || Filas.Count == 0 || Total <= 0 || EsFiado) return false;
            if (!ConfirmacionManualPointAceptada) return false;
            var filasPago = Pagos.Where(p => !string.IsNullOrWhiteSpace(p.ImporteTexto)).ToArray();
            if (filasPago.Length == 0) return false;
            if (filasPago.Count(p => p.Medio == MediosPago.MercadoPago) != 1) return false;
            foreach (var p in filasPago)
            {
                if (!p.ImporteMinor.HasValue || p.ImporteMinor <= 0) return false;
                if (p.EsEfectivo && (!p.RecibidoMinor.HasValue || p.RecibidoMinor < p.ImporteMinor)) return false;
            }
            return Pagado == Total;
        }
    }
    public bool OperacionPointIncierta
    {
        get => _operacionPointIncierta;
        private set
        {
            if (SetProperty(ref _operacionPointIncierta, value))
            {
                OnPropertyChanged(nameof(PuedeEditarVenta));
                OnPropertyChanged(nameof(PuedeEditarModalidad));
                OnPropertyChanged(nameof(PuedeQuitarPago));
                OnPropertyChanged(nameof(PuedeCancelarVenta));
            }
        }
    }
    public bool PuedeEditarVenta => !Ocupado && !CobroEnCurso && _operacionMpId is null && !OperacionPointIncierta;
    public bool PuedeQuitarPago => TieneVariosPagos && PuedeEditarVenta;
    public bool HayLineas => Filas.Count > 0;
    public long CantidadUnidades => Filas.Sum(f => f.Cantidad);
    public string ResumenCarrito => $"{Filas.Count} {(Filas.Count == 1 ? "variante" : "variantes")} · {CantidadUnidades} {(CantidadUnidades == 1 ? "unidad" : "unidades")}";
    public bool PuedeAbrirCobro => HayLineas && Total > 0 && !Ocupado && PuedeEditarVenta;
    public bool MostrarCtaCobrar => PuedeAbrirCobro && !CobroVisible;
    public bool PuedeCerrarCobro => CobroVisible && !CobroEnCurso;
    public bool PuedeCancelarVenta => HayLineas && !Ocupado && PuedeEditarVenta;
    public bool CobroVisible
    {
        get => _cobroVisible;
        private set
        {
            if (SetProperty(ref _cobroVisible, value))
            {
                OnPropertyChanged(nameof(PuedeCerrarCobro));
                OnPropertyChanged(nameof(MostrarCtaCobrar));
            }
        }
    }
    public string TextoEstadoPago => Total <= 0
        ? "Agregá productos para iniciar el cobro."
        : Pagos.Any(p => !string.IsNullOrWhiteSpace(p.ImporteTexto) && (!p.ImporteMinor.HasValue || p.ImporteMinor <= 0))
            ? "Revisá los importes ingresados."
        : Pagos.Any(p => p.EfectivoInsuficiente)
            ? $"Faltan {FormatearArs(Pagos.Where(p => p.EfectivoInsuficiente).Sum(p => Math.Max(0, (p.ImporteMinor ?? 0) - (p.RecibidoMinor ?? 0))))} de efectivo recibido. Ajustá el efectivo recibido o el importe cobrado; la diferencia no pasa automáticamente a cuenta."
        : Pagos.Any(p => p.EsEfectivo && p.ImporteMinor is > 0 && !p.RecibidoMinor.HasValue)
            ? "Ingresá el efectivo recibido por el cliente."
        : TieneMercadoPago && EsModoIntegrado && Pagado >= Total && !PuedeCobrarPoint
            ? "Revisá los importes ingresados."
        : TieneMercadoPago && EsModoManual && Pagado == Total && !ConfirmacionManualPointAceptada
            ? "Confirmá que el Point aprobó este importe."
        : Pagado > Total
            ? "Los importes superan el total. Revisá los medios de pago."
        : EsFiado && !LimiteClienteMinor.HasValue
            ? "Fiado no habilitado: el cliente no tiene un límite autorizado."
        : EsFiado && Restante <= 0
            ? "Cuenta corriente seleccionada, pero no queda saldo a financiar. Reducí los importes cobrados ahora."
        : EsFiado && SaldoPosteriorMinor > LimiteClienteMinor!.Value
            ? $"Crédito insuficiente. Tenés {FormatearArs(DisponibleCreditoMinor ?? 0)} disponibles y querés dejar {FormatearArs(DeudaNuevaMinor)} a cuenta. Cobrá al menos {FormatearArs(SaldoPosteriorMinor - LimiteClienteMinor.Value)} más."
        : EsFiado && Pagado == 0
            ? "Toda la venta queda a cuenta."
        : EsFiado
            ? "Pago parcial: el resto de la venta queda a cuenta."
            : Restante > 0
                ? $"Falta cobrar {FormatearArs(Restante)}"
                : "Pago completo";
    public string TextoVuelto => Pagos.Any(p => p.EfectivoInsuficiente)
        ? string.Empty
        : VueltoTotal is > 0 ? $"Vuelto {FormatearArs(VueltoTotal.Value)}"
        : VueltoTotal == 0 ? "Sin vuelto"
        : string.Empty;
    public bool CobroEnCurso { get => _cobroEnCurso; private set { if (SetProperty(ref _cobroEnCurso, value)) { OnPropertyChanged(nameof(PuedeEditarVenta)); OnPropertyChanged(nameof(PuedeEditarModalidad)); OnPropertyChanged(nameof(PuedeQuitarPago)); OnPropertyChanged(nameof(PuedeCobrarPoint)); OnPropertyChanged(nameof(PuedeCancelarCobro)); OnPropertyChanged(nameof(PuedeCerrarCobro)); OnPropertyChanged(nameof(PuedeCancelarVenta)); OnPropertyChanged(nameof(PuedeAbrirCobro)); OnPropertyChanged(nameof(MostrarCtaCobrar)); } } }
    private bool _cobroEnCurso;
    public string? EstadoCobroMp { get => _estadoCobroMp; private set => SetProperty(ref _estadoCobroMp, value); }
    private string? _estadoCobroMp;
    private string? _operacionMpId;
    private CancellationTokenSource? _pollCts;

    /// <summary>
    /// Habilita Cobrar en Point: carrito válido, Σ medios == total, sin
    /// fiado, una fila MP con importe, sin cobro en curso.
    /// </summary>
    public bool PuedeCobrarPoint
    {
        get
        {
            if (!EsModoIntegrado) return false;
            if (!PuedeEditarVenta || Ocupado || Filas.Count == 0 || Total <= 0 || EsFiado) return false;
            var filasPago = Pagos.Where(p => !string.IsNullOrWhiteSpace(p.ImporteTexto)).ToArray();
            if (filasPago.Length == 0) return false;
            if (filasPago.Count(p => p.Medio == MediosPago.MercadoPago) != 1) return false;
            foreach (var p in filasPago)
            {
                if (!p.ImporteMinor.HasValue || p.ImporteMinor <= 0) return false;
                if (p.EsEfectivo && (!p.RecibidoMinor.HasValue || p.RecibidoMinor < p.ImporteMinor)) return false;
            }
            return Pagado == Total;
        }
    }

    public bool PuedeCancelarCobro => CobroEnCurso;
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado
    {
        get => _ocupado;
        set
        {
            if (!SetProperty(ref _ocupado, value)) return;
            OnPropertyChanged(nameof(PuedeEditarVenta));
            OnPropertyChanged(nameof(PuedeEditarModalidad));
            OnPropertyChanged(nameof(PuedeQuitarPago));
            OnPropertyChanged(nameof(PuedeAbrirCobro));
            OnPropertyChanged(nameof(MostrarCtaCobrar));
            OnPropertyChanged(nameof(PuedeCancelarVenta));
            OnPropertyChanged(nameof(PuedeCobrarPoint));
        }
    }
    public bool ConfirmacionCancelarVisible { get => _confirmacionCancelarVisible; set => SetProperty(ref _confirmacionCancelarVisible, value); }
    public string OperacionId => _operacionId;

    /// <summary>Banner de éxito con el número estable de la última venta.</summary>
    public string? UltimoNumero { get => _ultimoNumero; set => SetProperty(ref _ultimoNumero, value); }
    public long? UltimoTotal { get => _ultimoTotal; set => SetProperty(ref _ultimoTotal, value); }

    public long Total => Filas.Sum(f => f.SubtotalMinor);
    public long Pagado => Pagos.Sum(p => p.ImporteMinor ?? 0);
    public long Restante => Total - Pagado;
    public long ImporteSinAsignarMinor => Math.Max(0, Restante);
    public long? VueltoTotal => Pagos.Any(p => p.VueltoMinor.HasValue) ? Pagos.Sum(p => p.VueltoMinor ?? 0) : null;

    /// <summary>
    /// Habilita Confirmar solo con carrito no vacío, pagos válidos e
    /// imputado == total (la confirmación revalida todo de todos modos).
    /// </summary>
    public bool PuedeConfirmar
    {
        get
        {
            if (Filas.Count == 0 || Total <= 0 || TieneMercadoPago) return false;
            var filasPago = Pagos.Where(p => !string.IsNullOrWhiteSpace(p.ImporteTexto)).ToArray();
            foreach (var p in filasPago)
            {
                if (!p.ImporteMinor.HasValue || p.ImporteMinor <= 0) return false;
                if (p.EsEfectivo && (!p.RecibidoMinor.HasValue || p.RecibidoMinor < p.ImporteMinor)) return false;
            }

            if (!EsFiado) return Pagado == Total && filasPago.Length > 0;
            return PuedeVenderFiado && PuedeConsultarCuenta && ClienteSeleccionado is not null
                && LimiteClienteMinor.HasValue && Restante > 0 && Pagado < Total && SaldoPosteriorMinor <= LimiteClienteMinor.Value;
        }
    }

    public AsyncRelayCommand SeleccionarSugerenciaCommand { get; }
    public AsyncRelayCommand BuscarDirectaCommand { get; }
    public RelayCommand QuitarFilaCommand { get; }
    public RelayCommand IncrementarCommand { get; }
    public RelayCommand DecrementarCommand { get; }
    public AsyncRelayCommand AgregarPagoCommand { get; }
    public AsyncRelayCommand QuitarPagoCommand { get; }
    public RelayCommand AbrirCobroCommand { get; }
    public RelayCommand CerrarCobroCommand { get; }
    public AsyncRelayCommand ConfirmarCommand { get; }
    public AsyncRelayCommand CobrarEnPointCommand { get; }
    public AsyncRelayCommand RegistrarCobroManualCommand { get; }
    public AsyncRelayCommand CancelarCobroPointCommand { get; }
    public RelayCommand SolicitarCancelarCommand { get; }
    public RelayCommand CancelarCancelarCommand { get; }
    public RelayCommand ConfirmarCancelarCommand { get; }
    public RelayCommand NuevaVentaCommand { get; }
    public AsyncRelayCommand VerTicketCommand { get; }

    public Task AgregarSugerenciaAsyncPublic(SugerenciaVariante? s) => AgregarSugerenciaAsync(s);
    public Task AgregarDirectaAsyncPublic(string? texto) => AgregarDirectaAsync(texto);
    public Task ConfirmarAsyncPublic() => ConfirmarAsync();
    public Task VerTicketAsyncPublic() => VerTicketAsync();

    public Task AgregarPagoAsync()
    {
        if (!PuedeEditarVenta) return Task.CompletedTask;
        var fila = new FilaPagoViewModel();
        var restante = Math.Max(0, Restante);
        if (restante > 0)
            fila.PrecargarImporte(FormatearImporteEditable(restante));
        Pagos.Add(fila);
        return Task.CompletedTask;
    }

    private void PrecargarSaldoEnFila(FilaPagoViewModel fila)
    {
        if (fila.ImporteModificadoPorUsuario || !Pagos.Contains(fila)) return;
        var restante = Math.Max(0, Total - Pagos.Where(p => !ReferenceEquals(p, fila)).Sum(p => p.ImporteMinor ?? 0));
        if (restante > 0)
            fila.PrecargarImporte(FormatearImporteEditable(restante));
    }

    private static string FormatearImporteEditable(long minor)
        => (minor / 100m).ToString("N2", new CultureInfo("es-AR"));

    public Task QuitarPagoAsync()
    {
        return QuitarPagoAsync(Pagos.LastOrDefault());
    }

    public Task QuitarPagoAsync(FilaPagoViewModel? fila)
    {
        if (!PuedeEditarVenta) return Task.CompletedTask;
        if (Pagos.Count > 1 && fila is not null && Pagos.Contains(fila)) Pagos.Remove(fila);
        return Task.CompletedTask;
    }

    private readonly HashSet<FilaVentaViewModel> _filasSuscriptas = new();
    private readonly HashSet<FilaPagoViewModel> _pagosSuscriptos = new();

    private void FilasCambiadas(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        var filas = e.NewItems is not null
            ? e.NewItems.Cast<FilaVentaViewModel>()
            : Filas;
        foreach (var f in filas)
        {
            if (_filasSuscriptas.Add(f))
            {
                f.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(FilaVentaViewModel.SubtotalMinor))
                    {
                        NotificarTotales();
                        // Con el cobro abierto y el carrito editado, los importes
                        // automáticos (no modificados por el operador) se
                        // recalculan; los manuales deliberados no se tocan.
                        if (CobroVisible)
                            foreach (var p in Pagos)
                                PrecargarSaldoEnFila(p);
                    }
                };
                f.AvisoStock = mensaje => Error = mensaje;
            }
        }

        NotificarTotales();
        if (CobroVisible)
            foreach (var p in Pagos)
                PrecargarSaldoEnFila(p);
    }

    private void PagosCambiados(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        var pagos = e.NewItems is not null
            ? e.NewItems.Cast<FilaPagoViewModel>()
            : Pagos;
        foreach (var p in pagos)
        {
            if (_pagosSuscriptos.Add(p))
            {
                p.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(FilaPagoViewModel.ImporteMinor)
                        || args.PropertyName == nameof(FilaPagoViewModel.RecibidoMinor)
                        || args.PropertyName == nameof(FilaPagoViewModel.Medio))
                    {
                        if (args.PropertyName == nameof(FilaPagoViewModel.Medio))
                        {
                            PrecargarSaldoEnFila(p);
                            OnPropertyChanged(nameof(TieneMercadoPago));
                            OnPropertyChanged(nameof(MostrarAyudaPoint));
                        }
                        NotificarTotales();
                    }
                };
            }
        }

        OnPropertyChanged(nameof(TieneVariosPagos));
        NotificarTotales();
    }

    private void Recalcular()
    {
        FilasCambiadas(this, new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        PagosCambiados(this, new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Relee la modalidad de Point desde la configuración. Si pasa a
    /// desactivado, convierte las filas Point pendientes a efectivo para no
    /// ofrecer un medio no disponible.
    /// </summary>
    private void RefrescarModoPoint()
    {
        var modo = _servicios.ObtenerModoPoint();
        if (string.Equals(modo, _modoPoint, StringComparison.Ordinal)) return;
        _modoPoint = modo;
        if (EsModoDesactivado)
        {
            foreach (var p in Pagos.Where(p => p.Medio == MediosPago.MercadoPago))
                p.Medio = MediosPago.Efectivo;
        }

        OnPropertyChanged(nameof(ModoPoint));
        OnPropertyChanged(nameof(EsModoIntegrado));
        OnPropertyChanged(nameof(EsModoManual));
        OnPropertyChanged(nameof(EsModoDesactivado));
        OnPropertyChanged(nameof(MediosDisponibles));
        NotificarTotales();
    }

    /// <summary>
    /// Actualiza solo la disponibilidad de Point y nunca transforma pagos de un
    /// borrador existente. Si hay un pago Point o una operación en curso, queda
    /// pendiente hasta que el flujo de cobro haga su propia revalidación.
    /// </summary>
    public void RefrescarModoPointSiSeguro()
    {
        if (CobroEnCurso || TieneMercadoPago) return;
        RefrescarModoPoint();
    }

    private void NotificarTotales()
    {
        if (!HayLineas && CobroVisible) CobroVisible = false;
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(HayLineas));
        OnPropertyChanged(nameof(CantidadUnidades));
        OnPropertyChanged(nameof(ResumenCarrito));
        OnPropertyChanged(nameof(PuedeAbrirCobro));
        OnPropertyChanged(nameof(MostrarCtaCobrar));
        OnPropertyChanged(nameof(PuedeCancelarVenta));
        OnPropertyChanged(nameof(PuedeEditarVenta));
        OnPropertyChanged(nameof(PuedeQuitarPago));
        OnPropertyChanged(nameof(PuedeCerrarCobro));
        OnPropertyChanged(nameof(Pagado));
        OnPropertyChanged(nameof(ImporteSinAsignarMinor));
        OnPropertyChanged(nameof(CobradoAhoraMinor));
        OnPropertyChanged(nameof(Restante));
        OnPropertyChanged(nameof(VueltoTotal));
        OnPropertyChanged(nameof(TextoEstadoPago));
        OnPropertyChanged(nameof(TextoVuelto));
        OnPropertyChanged(nameof(PuedeConfirmar));
        OnPropertyChanged(nameof(TieneMercadoPago));
        OnPropertyChanged(nameof(MostrarAyudaPoint));
        OnPropertyChanged(nameof(MostrarPanelManualPoint));
        OnPropertyChanged(nameof(MostrarAccionPointIntegrada));
        OnPropertyChanged(nameof(MostrarAccionPointManual));
        OnPropertyChanged(nameof(AyudaPointMostrada));
        OnPropertyChanged(nameof(PuedeQuitarPago));
        OnPropertyChanged(nameof(TextoAccionPoint));
        OnPropertyChanged(nameof(TextoAccionManual));
        OnPropertyChanged(nameof(PuedeCobrarPoint));
        OnPropertyChanged(nameof(PuedeRegistrarCobroManual));
        OnPropertyChanged(nameof(DeudaNuevaMinor));
        OnPropertyChanged(nameof(SaldoPosteriorMinor));
        OnPropertyChanged(nameof(DisponibleCreditoMinor));
        OnPropertyChanged(nameof(DisponibleCreditoPosteriorMinor));
        OnPropertyChanged(nameof(DisponibleCreditoPosteriorMostradoMinor));
        OnPropertyChanged(nameof(EstadoCuentaCorriente));
        OnPropertyChanged(nameof(MostrarResumenCuentaCobro));
        OnPropertyChanged(nameof(MostrarAvisoFiadoNoDisponible));
        OnPropertyChanged(nameof(MotivoFiadoNoDisponible));
        OnPropertyChanged(nameof(ModalidadFiadoTotalSeleccionada));
        OnPropertyChanged(nameof(ModalidadFiadoParcialSeleccionada));
        OnPropertyChanged(nameof(PuedeEditarModalidad));
        OnPropertyChanged(nameof(TextoModalidadCuenta));
        OnPropertyChanged(nameof(MostrarDistribucionPago));
        OnPropertyChanged(nameof(MostrarOpcionCobroParcial));
    }

    /// <summary>
    /// Salto desde sugerencia o código exacto: resuelve variante, precio
    /// vigente y Disponible; agrega o incrementa la fila (nunca duplica).
    /// </summary>
    private async Task AgregarSugerenciaAsync(SugerenciaVariante? sugerencia)
    {
        if (sugerencia is null || Ocupado || !PuedeEditarVenta) return;
        await AgregarVarianteAsync(sugerencia.VarianteId);
    }

    /// <summary>Enter agrega un código exacto o la única coincidencia inequívoca.</summary>
    private async Task AgregarDirectaAsync(string? texto)
    {
        if (Ocupado || !PuedeEditarVenta || string.IsNullOrWhiteSpace(texto)) return;
        Error = null;
        try
        {
            var codigo = texto.Trim();
            var variante = await _servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(codigo, _idSesion());
            if (variante is not null)
            {
                await AgregarVarianteAsync(variante.Id, devolverFoco: false);
                return;
            }

            if (!Regex.IsMatch(codigo, "^[A-Za-z]{2,5}-[0-9]{3}-[A-Za-z0-9-]+$", RegexOptions.CultureInvariant))
            {
                if (MotorSugerencias.Texto != codigo || MotorSugerencias.Ocupado || !MotorSugerencias.Abierto)
                    await MotorSugerencias.TextoCambiadoAsync(codigo);

                if (MotorSugerencias.Sugerencias.Count == 1)
                {
                    await AgregarVarianteAsync(MotorSugerencias.Sugerencias[0].VarianteId, devolverFoco: false);
                    return;
                }

                if (MotorSugerencias.Sugerencias.Count > 1)
                {
                    Error = "Hay varias coincidencias. Elegí una variante de la lista.";
                    return;
                }

                if (MotorSugerencias.SinCoincidencias)
                    Error = "No encontramos coincidencias para esa búsqueda.";
                return;
            }
            Error = $"No encontramos el código {codigo}.";
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally
        {
            if (Error is not null && Regex.IsMatch(texto.Trim(), "^[A-Za-z]{2,5}-[0-9]{3}-[A-Za-z0-9-]+$", RegexOptions.CultureInvariant))
                MotorSugerencias.Limpiar();
            SolicitarFocoBuscador?.Invoke();
        }
    }

    private async Task AgregarVarianteAsync(string varianteId, bool devolverFoco = true)
    {
        if (Ocupado || !PuedeEditarVenta) return;
        Error = null; Mensaje = null;
        if (TieneMercadoPago)
        {
            Error = EsModoManual
                ? "El medio Mercado Pago se registra con el panel de cobro manual."
                : "El medio Mercado Pago se cobra con Cobrar en Point.";
            return;
        }

        Ocupado = true;
        try
        {
            var variante = await _servicios.ConsultasCatalogo.ObtenerVarianteAsync(varianteId, _idSesion());
            if (variante is null || !variante.Activa)
            {
                Error = variante is null ? "No encontramos esta variante." : "Esta variante está inactiva.";
                return;
            }

            var fecha = DateTime.UtcNow.ToString("o");
            var ficha = await _servicios.ConsultasCatalogo.ObtenerFichaAsync(variante.ProductoId, fecha, _idSesion());
            var linea = ficha?.Variantes.FirstOrDefault(v => v.Id == varianteId);
            if (linea?.PrecioMinor is null)
            {
                Error = "Esta variante no tiene precio vigente.";
                return;
            }

            var stock = await _servicios.ConsultasInventario.ObtenerStockVendibleAsync(varianteId, _idSesion());
            var disponible = stock?.Disponible ?? 0;
            var existente = Filas.FirstOrDefault(f => f.VarianteId == varianteId);
            var pedidas = (existente?.Cantidad ?? 0) + 1;
            if (disponible < pedidas)
            {
                Error = disponible <= 0
                    ? "Sin stock disponible."
                    : $"Solo hay {disponible} unidades disponibles de esta variante.";
                return;
            }

            if (existente is not null)
            {
                existente.Cantidad = pedidas;
                existente.PrecioUnitarioMinor = linea.PrecioMinor.Value;
                OnPropertyChanged(nameof(Total));
                OnPropertyChanged(nameof(Pagado));
                OnPropertyChanged(nameof(Restante));
                OnPropertyChanged(nameof(PuedeConfirmar));
            }
            else
            {
                var producto = ficha?.Producto;
                var fila = new FilaVentaViewModel
                {
                    VarianteId = varianteId,
                    CodigoInterno = variante.CodigoInterno,
                    NombreProducto = producto?.Nombre ?? variante.CodigoInterno,
                    Talle = variante.Talle,
                    Color = variante.Color,
                    PrecioUnitarioMinor = linea.PrecioMinor.Value,
                    Disponible = disponible,
                };
                fila.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FilaVentaViewModel.SubtotalMinor))
                        NotificarTotales();
                };
                Filas.Add(fila);
            }

            MotorSugerencias.EstablecerTexto(string.Empty);
            MotorSugerencias.Limpiar();
            UltimoNumero = null;
            Mensaje = null;
            if (devolverFoco) SolicitarFocoBuscador?.Invoke();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally
        {
            Ocupado = false;
            if (Error is not null && devolverFoco) SolicitarFocoBuscador?.Invoke();
        }
    }

    private void AjustarCantidad(FilaVentaViewModel fila, int delta)
    {
        if (!PuedeEditarVenta) return;
        Error = null;
        var nueva = fila.Cantidad + delta;
        if (nueva < 1)
        {
            Filas.Remove(fila);
            return;
        }

        if (fila.Disponible.HasValue && nueva > fila.Disponible.Value)
        {
            Error = $"Solo hay {fila.Disponible.Value} unidades disponibles de esta variante.";
            return;
        }

        fila.Cantidad = nueva;
    }

    private async Task ConfirmarAsync()
    {
        if (Ocupado || CobroEnCurso) return;
        Error = null; Mensaje = null;
        if (TieneMercadoPago) { Error = "El medio Mercado Pago se cobra con Cobrar en Point."; return; }
        if (Filas.Count == 0) { Error = "El carrito está vacío."; return; }
        var lineas = new List<NuevaLineaVenta>();
        foreach (var f in Filas)
            lineas.Add(new NuevaLineaVenta(f.VarianteId, f.Cantidad, f.PrecioUnitarioMinor));

        var pagos = new List<NuevoPago>();
        foreach (var p in Pagos.Where(p => !string.IsNullOrWhiteSpace(p.ImporteTexto)))
        {
            if (!p.ImporteMinor.HasValue || p.ImporteMinor <= 0) { Error = "Revisá los importes de los pagos."; return; }
            if (p.EsEfectivo && (!p.RecibidoMinor.HasValue || p.RecibidoMinor < p.ImporteMinor))
            {
                Error = "El efectivo recibido es menor al importe correspondiente.";
                return;
            }

            pagos.Add(new NuevoPago(p.Medio, p.ImporteMinor.Value, p.EsEfectivo ? p.RecibidoMinor : null));
        }
        if (!EsFiado && pagos.Count == 0) { Error = "Cargá al menos un medio de pago."; return; }
        if (EsFiado && (!PuedeVenderFiado || !PuedeConsultarCuenta || ClienteSeleccionado is null || !LimiteClienteMinor.HasValue))
        { Error = "La cuenta corriente requiere permiso, cliente seleccionado y un límite configurado."; return; }
        if (Pagado > Total) { Error = "El importe de los pagos supera el total de la venta."; return; }
        if (EsFiado && (Restante <= 0 || SaldoPosteriorMinor > LimiteClienteMinor!.Value))
        { Error = "Esta operación supera el límite de cuenta corriente del cliente."; return; }

        Ocupado = true;
        try
        {
            var resultado = await _servicios.CasoConfirmarVenta.EjecutarAsync(lineas, pagos, _operacionId, _idSesion(), ClienteSeleccionado?.Id,
                EsFiado ? Vencimiento?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null, EsFiado);
            switch (resultado)
            {
                case ResultadoConfirmacionVenta.Confirmada ok:
                    NotificarVentaConfirmada();
                    UltimoNumero = ok.Numero;
                    UltimoTotal = ok.TotalMinor;
                    Mensaje = $"Venta {ok.Numero} confirmada.";
                    LimpiarVenta(operacionFinalizada: true);
                    _operacionId = _servicios.GeneradorIds.NuevoId();
                    OnPropertyChanged(nameof(OperacionId));
                    SolicitarFocoBuscador?.Invoke();
                    break;
                case ResultadoConfirmacionVenta.PrecioCambiado cambio:
                    foreach (var c in cambio.Cambios)
                    {
                        var fila = Filas.FirstOrDefault(f => f.VarianteId == c.VarianteId);
                        if (fila is not null)
                        {
                            fila.PrecioUnitarioMinor = c.PrecioVigenteMinor;
                            OnPropertyChanged(nameof(Total));
                        }
                    }

                    var primero = cambio.Cambios[0];
                    Error = $"El precio de {primero.NombreProducto} cambió de {FormatearArs(primero.PrecioEsperadoMinor)} a {FormatearArs(primero.PrecioVigenteMinor)}. Revisá la venta antes de continuar.";
                    break;
            }
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    /// <summary>
    /// Cobro manual de Point: registra la venta y el cobro declarado en una
    /// sola transacción. Nunca llama a la API de Mercado Pago. Exige la
    /// declaración humana y que no haya cobros integrados pendientes.
    /// </summary>
    private async Task RegistrarCobroManualAsync()
    {
        if (Ocupado || CobroEnCurso) return;
        Error = null; Mensaje = null;
        RefrescarModoPoint();
        if (!PuedeRegistrarCobroManual) { Error = "Confirmá que el Point aprobó este importe y revisá el carrito."; return; }
        var lineas = Filas.Select(f => new NuevaLineaVenta(f.VarianteId, f.Cantidad, f.PrecioUnitarioMinor)).ToList();
        var pagos = new List<NuevoPago>();
        foreach (var p in Pagos.Where(p => !string.IsNullOrWhiteSpace(p.ImporteTexto)))
        {
            if (!p.ImporteMinor.HasValue || p.ImporteMinor <= 0) { Error = "Revisá los importes de los pagos."; return; }
            if (p.EsEfectivo && (!p.RecibidoMinor.HasValue || p.RecibidoMinor < p.ImporteMinor))
            {
                Error = "El efectivo recibido es menor al importe correspondiente.";
                return;
            }

            pagos.Add(new NuevoPago(p.Medio, p.ImporteMinor.Value, p.EsEfectivo ? p.RecibidoMinor : null));
        }

        Ocupado = true;
        try
        {
            var resultado = await RegistrarManualConDedupAsync(lineas, pagos);
            if (resultado is null) return;
            switch (resultado)
            {
                case ResultadoConfirmacionVenta.Confirmada ok:
                    NotificarVentaConfirmada();
                    UltimoNumero = ok.Numero;
                    UltimoTotal = ok.TotalMinor;
                    Mensaje = $"Venta {ok.Numero} confirmada con cobro manual de Point.";
                    LimpiarVenta(operacionFinalizada: true);
                    _operacionId = _servicios.GeneradorIds.NuevoId();
                    OnPropertyChanged(nameof(OperacionId));
                    SolicitarFocoBuscador?.Invoke();
                    break;
                case ResultadoConfirmacionVenta.PrecioCambiado cambio:
                    foreach (var c in cambio.Cambios)
                    {
                        var fila = Filas.FirstOrDefault(f => f.VarianteId == c.VarianteId);
                        if (fila is not null)
                        {
                            fila.PrecioUnitarioMinor = c.PrecioVigenteMinor;
                            OnPropertyChanged(nameof(Total));
                        }
                    }

                    var primero = cambio.Cambios[0];
                    Error = $"El precio de {primero.NombreProducto} cambió de {FormatearArs(primero.PrecioEsperadoMinor)} a {FormatearArs(primero.PrecioVigenteMinor)}. Revisá la venta antes de continuar.";
                    break;
            }
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    /// <summary>
    /// Registra el cobro manual resolviendo el dedup (§5.4): si ya hay un
    /// cobro reciente del mismo importe/terminal, exige confirmación humana
    /// antes de reintentar. Devuelve null si el operador cancela.
    /// </summary>
    private async Task<ResultadoConfirmacionVenta?> RegistrarManualConDedupAsync(
        IReadOnlyList<NuevaLineaVenta> lineas, IReadOnlyList<NuevoPago> pagos)
    {
        try
        {
            return await CasoRegistrarManual.EjecutarAsync(lineas, pagos, _operacionId, ReferenciaComprobanteManual, _idSesion());
        }
        catch (Application.Features.MercadoPago.CobroManualDuplicadoSospechosoException dup)
        {
            if (_dialogos is null || !_dialogos.ConfirmarCobroManualDuplicado(DescribirCandidatoDuplicado(dup.Candidatos)))
            {
                Error = "No se registró el cobro: se detectó un posible duplicado de Point.";
                return null;
            }

            return await CasoRegistrarManual.EjecutarAsync(lineas, pagos, _operacionId, ReferenciaComprobanteManual, _idSesion(), confirmarPosibleDuplicado: true);
        }
    }

    private static string DescribirCandidatoDuplicado(IReadOnlyList<CobroManualMpRegistrado> candidatos)
        => $"Ya se registró un cobro manual de Point por {FormatearArs(candidatos[0].ImporteMinor)}.";

    /// <summary>
    /// Cobro Point con saga durable: una sola operación lógica aunque haya
    /// doble click/Enter (CobroEnCurso se activa antes del primer await).
    /// Polling moderado y cancelable; timeout ≠ rechazo.
    /// </summary>
    private async Task CobrarEnPointAsync()
    {
        if (Ocupado || CobroEnCurso) return;
        Error = null; Mensaje = null; EstadoCobroMp = null;
        if (!PuedeCobrarPoint) { Error = "Revisá el carrito y los medios antes de cobrar en Point."; return; }
        var lineas = Filas.Select(f => new NuevaLineaVenta(f.VarianteId, f.Cantidad, f.PrecioUnitarioMinor)).ToList();
        var pagos = new List<NuevoPago>();
        foreach (var p in Pagos.Where(p => !string.IsNullOrWhiteSpace(p.ImporteTexto)))
        {
            if (!p.ImporteMinor.HasValue || p.ImporteMinor <= 0) { Error = "Revisá los importes de los pagos."; return; }
            pagos.Add(new NuevoPago(p.Medio, p.ImporteMinor.Value, p.EsEfectivo ? p.RecibidoMinor : null));
        }

        CobroEnCurso = true;
        Ocupado = true;
        EstadoCobroMp = "Enviando cobro…";
        _pollCts = new CancellationTokenSource();
        try
        {
            _operacionMpId = await CasoIniciar.EjecutarAsync(lineas, pagos, _idSesion(), _pollCts.Token);
            await EsperarCobroAsync(_pollCts.Token);
        }
        catch (OperationCanceledException) when (_pollCts?.IsCancellationRequested == true)
        {
            EstadoCobroMp = "Cobro cancelado. La versión actual sigue sin cambios.";
        }
        catch (Exception ex)
        {
            Error = MensajeError(ex);
            if (ErrorCobroPointIncierto(ex.Message))
            {
                OperacionPointIncierta = true;
                EstadoCobroMp = "El resultado del cobro no está confirmado. No modifiques esta venta hasta verificarlo.";
            }
            else
            {
                EstadoCobroMp = null;
            }
        }
        finally
        {
            CobroEnCurso = false;
            Ocupado = false;
            _pollCts?.Dispose();
            _pollCts = null;
        }
    }

    private async Task EsperarCobroAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var operacion = await CasoConsultar.EjecutarAsync(_operacionMpId!, _idSesion(), ct);
            EstadoCobroMp = operacion.EstadoLocal switch
            {
                Application.Contracts.EstadosLocalesMp.EsperandoTerminal => operacion.EstadoMp == Application.Contracts.EstadosOrdenMp.AtTerminal
                    ? "Esperando pago en Point…"
                    : "Enviando cobro…",
                Application.Contracts.EstadosLocalesMp.ProcesadaRemota or Application.Contracts.EstadosLocalesMp.ConfirmandoLocal => "Pago aprobado. Confirmando venta…",
                Application.Contracts.EstadosLocalesMp.RequiereConciliacion => "Verificando el estado del cobro…",
                _ => EstadoCobroMp,
            };
            switch (operacion.EstadoLocal)
            {
                case Application.Contracts.EstadosLocalesMp.Confirmada:
                    await CerrarCobroExitosoAsync(operacion.OperacionId, ct);
                    return;
                case Application.Contracts.EstadosLocalesMp.Fallida:
                    Error = "Pago rechazado. No se registró venta.";
                    EstadoCobroMp = null;
                    _operacionMpId = null;
                    return;
                case Application.Contracts.EstadosLocalesMp.Cancelada:
                    Mensaje = "Cobro cancelado. No se registró venta.";
                    EstadoCobroMp = null;
                    _operacionMpId = null;
                    return;
                case Application.Contracts.EstadosLocalesMp.Expirada:
                    Error = "El cobro expiró. No se registró venta.";
                    EstadoCobroMp = null;
                    _operacionMpId = null;
                    return;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
    }

    private async Task CerrarCobroExitosoAsync(string operacionId, CancellationToken ct)
    {
        var venta = await _servicios.ConsultasVentas.BuscarPorOperacionAsync(operacionId, _idSesion());
        NotificarVentaConfirmada();
        UltimoNumero = venta?.Numero;
        UltimoTotal = venta?.TotalMinor;
        Mensaje = venta is null ? "Venta confirmada." : $"Venta {venta.Numero} confirmada.";
        EstadoCobroMp = "Pago aprobado.";
        _operacionMpId = null;
        Filas.Clear();
        ClienteSeleccionado = null;
        EsFiado = false;
        Pagos.Clear();
        Pagos.Add(new FilaPagoViewModel());
        ConfirmacionManualPointAceptada = false;
        ReferenciaComprobanteManual = string.Empty;
        _operacionId = _servicios.GeneradorIds.NuevoId();
        OnPropertyChanged(nameof(OperacionId));
        NotificarTotales();
        SolicitarFocoBuscador?.Invoke();
    }

    private async Task CancelarCobroPointAsync()
    {
        if (!CobroEnCurso || _operacionMpId is null)
        {
            _pollCts?.Cancel();
            return;
        }

        try
        {
            var operacion = await CasoCancelar.EjecutarAsync(_operacionMpId, _idSesion());
            if (operacion.EstadoLocal == Application.Contracts.EstadosLocalesMp.Cancelada)
            {
                Mensaje = "Cobro cancelado. No se registró venta.";
                EstadoCobroMp = null;
                _operacionMpId = null;
            }
            else
            {
                EstadoCobroMp = "Verificando el estado del cobro…";
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            _pollCts?.Cancel();
        }
    }

    private void LimpiarVenta(bool operacionFinalizada = false)
    {
        if (!operacionFinalizada && !PuedeEditarVenta) { Error = "No modifiques la venta hasta resolver el cobro en Point."; return; }
        Filas.Clear();
        ClienteSeleccionado = null;
        EsFiado = false; Vencimiento = null; SaldoClienteMinor = null; LimiteClienteMinor = null;
        MotorClientes.Limpiar();
        Pagos.Clear();
        Pagos.Add(new FilaPagoViewModel());
        ConfirmacionManualPointAceptada = false;
        ReferenciaComprobanteManual = string.Empty;
        Error = null;
        ConfirmacionCancelarVisible = false;
        CobroVisible = false;
        BuscadorClienteVisible = false;
        NotificarTotales();
    }

    private void SeleccionarCliente(SugerenciaCliente? cliente)
    {
        if (cliente is null || !PuedeEditarVenta) return;
        ClienteSeleccionado = cliente;
        BuscadorClienteVisible = false;
        MotorClientes.Limpiar();
    }

    private static bool ErrorCobroPointIncierto(string mensaje)
        => mensaje.Contains("Verificando el estado del cobro", StringComparison.OrdinalIgnoreCase)
           || mensaje.Contains("La terminal ya tiene un cobro pendiente", StringComparison.OrdinalIgnoreCase)
           || mensaje.Contains("Hay un cobro de Mercado Pago pendiente de resolver", StringComparison.OrdinalIgnoreCase)
           || mensaje.Contains("Mercado Pago no está disponible. Revisá la conexión.", StringComparison.OrdinalIgnoreCase);

    private async Task CargarResumenCuentaAsync()
    {
        var version = ++_versionResumenCuenta;
        var clienteId = ClienteSeleccionado?.Id;
        _errorResumenCuenta = false;
        SaldoClienteMinor = null;
        LimiteClienteMinor = null;
        SetCargandoResumenCuenta(clienteId is not null && PuedeConsultarCuenta);
        if (clienteId is null || !PuedeConsultarCuenta) return;
        try
        {
            var resumen = await _servicios.ConsultasCuentaCorriente.ObtenerAsync(clienteId, _idSesion());
            if (version != _versionResumenCuenta || ClienteSeleccionado?.Id != clienteId) return;
            if (resumen is null) { _errorResumenCuenta = true; return; }
            SaldoClienteMinor = resumen.SaldoMinor; LimiteClienteMinor = resumen.LimiteMinor;
        }
        catch
        {
            if (version == _versionResumenCuenta && ClienteSeleccionado?.Id == clienteId)
                _errorResumenCuenta = true;
            /* Falla cerrada: ConfirmarVenta vuelve a validar permiso, límite y saldo. */
        }
        finally
        {
            if (version == _versionResumenCuenta)
                SetCargandoResumenCuenta(false);
        }
    }

    private void SetCargandoResumenCuenta(bool value)
    {
        if (_cargandoResumenCuenta == value) return;
        _cargandoResumenCuenta = value;
        OnPropertyChanged(nameof(CargandoResumenCuenta));
        NotificarTotales();
    }

    private async Task VerTicketAsync()
    {
        if (string.IsNullOrWhiteSpace(UltimoNumero)) { Error = "Todavía no hay venta para mostrar."; return; }
        Error = null;
        try
        {
            var venta = await _servicios.ConsultasVentas.BuscarPorNumeroAsync(UltimoNumero, _idSesion());
            if (venta is null) { Error = "No se encontró la venta."; return; }
            if (_dialogos is null) { Error = "Vista de comprobante no disponible."; return; }
            _dialogos.MostrarTicket(venta.Id);
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    public static string FormatearArs(long minor)
    {
        var dec = (decimal)minor / 100m;
        return $"ARS {dec.ToString("N2", new CultureInfo("es-AR"))}";
    }

    private void NotificarVentaConfirmada()
        => _servicios.NotificadorCambios.Publicar(this, AreaDatos.Ventas, AreaDatos.Ventas, AreaDatos.Stock, AreaDatos.Caja, AreaDatos.CuentaCorriente);

    private static string MensajeError(Exception ex) => ex switch
    {
        LocalInfantil.Application.Abstractions.AccesoDenegadoException
            => "No tenés permiso para vender. La Dueña puede habilitarlo desde Administración → Permisos.",
        LocalInfantil.Application.Abstractions.SesionInvalidaException
            => "Sesión inválida. Iniciá sesión nuevamente.",
        _ => ex.Message,
    };
}
