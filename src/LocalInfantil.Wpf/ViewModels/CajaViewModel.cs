using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Windows.Input;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed record OpcionCategoriaCaja(string Valor, string Texto);

public sealed class CajaViewModel : ViewModelBase
{
    private const int TamanoPagina = 30;
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private DetalleSesionCaja? _detalleActual;
    private DetalleSesionCaja? _detalleHistorico;
    private SesionCajaRegistrada? _sesionSeleccionada;
    private PagoCajaActual? _pagoSeleccionado;
    private VentaCorreccionSugeridaItemViewModel? _ventaCorreccionSeleccionada;
    private OpcionCategoriaCaja? _categoriaSeleccionada;
    private ArqueoCiegoViewModel? _arqueo;
    private string _fondoInicialTexto = "0";
    private string _tipoMovimiento = "Ingreso";
    private string _importeMovimientoTexto = string.Empty;
    private string _motivoMovimiento = string.Empty;
    private string _observacion = string.Empty;
    private string _busquedaPago = string.Empty;
    private string _medioNuevo = MediosPago.Efectivo;
    private string _motivoCorreccion = string.Empty;
    private string _error = string.Empty;
    private string _mensaje = string.Empty;
    private bool _ocupado;
    private bool _puedeAbrir;
    private bool _puedeConsultar;
    private bool _puedeMover;
    private bool _puedeCerrar;
    private bool _puedeRetirar;
    private bool _puedeCorregir;
    private bool _hayMasHistorial;
    private bool _buscandoVentas;
    private bool _sugerenciasVentasAbiertas;
    private bool _busquedaVentasInicializada;
    private string _estadoBusquedaVentas = "Escribí un ticket, producto o código para buscar ventas.";
    private CancellationTokenSource? _busquedaVentasCts;
    private int _versionBusquedaVentas;

    public CajaViewModel(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios ?? throw new ArgumentNullException(nameof(servicios));
        _idSesion = idSesion ?? throw new ArgumentNullException(nameof(idSesion));
        AbrirCommand = new AsyncRelayCommand(AbrirCajaAsync);
        RegistrarMovimientoCommand = new AsyncRelayCommand(RegistrarMovimientoAsync);
        IniciarArqueoCommand = new RelayCommand(_ => IniciarArqueo());
        BuscarPagosCommand = new AsyncRelayCommand(BuscarPagosAsync);
        SeleccionarVentaSugeridaCommand = new AsyncRelayCommand(SeleccionarVentaSugeridaAsync);
        LimpiarBusquedaVentasCommand = new RelayCommand(_ => LimpiarBusquedaVentas());
        CorregirMedioCommand = new AsyncRelayCommand(CorregirMedioAsync);
        CargarMasHistorialCommand = new AsyncRelayCommand(CargarMasHistorialAsync);
        ActualizarCategorias();
    }

    public ObservableCollection<MovimientoCajaViewModel> Movimientos { get; } = [];
    public ObservableCollection<MovimientoCajaViewModel> MovimientosHistoricos { get; } = [];
    public ObservableCollection<SesionCajaRegistrada> Historial { get; } = [];
    public ObservableCollection<PagoCajaActual> PagosCorregibles { get; } = [];
    public ObservableCollection<VentaCorreccionSugeridaItemViewModel> VentasSugeridas { get; } = [];
    public ObservableCollection<OpcionCategoriaCaja> CategoriasDisponibles { get; } = [];
    public IReadOnlyList<string> TiposMovimiento { get; } = ["Ingreso", "Egreso"];
    public IReadOnlyList<OpcionMedioCaja> MediosDisponibles { get; } =
    [
        new(MediosCaja.Efectivo, "Efectivo"),
        new(MediosCaja.Transferencia, "Transferencia"),
        new(MediosCaja.Debito, "Débito"),
        new(MediosCaja.Credito, "Crédito"),
        new(MediosCaja.Billetera, "Billetera virtual"),
    ];

    public ICommand AbrirCommand { get; }
    public ICommand RegistrarMovimientoCommand { get; }
    public ICommand IniciarArqueoCommand { get; }
    public ICommand BuscarPagosCommand { get; }
    public ICommand SeleccionarVentaSugeridaCommand { get; }
    public ICommand LimpiarBusquedaVentasCommand { get; }
    public ICommand CorregirMedioCommand { get; }
    public ICommand CargarMasHistorialCommand { get; }

    public DetalleSesionCaja? DetalleActual { get => _detalleActual; private set { if (SetProperty(ref _detalleActual, value)) NotificarEstado(); } }
    public DetalleSesionCaja? DetalleHistorico { get => _detalleHistorico; private set => SetProperty(ref _detalleHistorico, value); }
    public SesionCajaRegistrada? SesionSeleccionada
    {
        get => _sesionSeleccionada;
        set
        {
            if (SetProperty(ref _sesionSeleccionada, value) && value is not null)
                _ = CargarDetalleHistoricoAsync(value.Id);
        }
    }
    public PagoCajaActual? PagoSeleccionado
    {
        get => _pagoSeleccionado;
        set
        {
            if (value is not null && value.VentaId != VentaCorreccionSeleccionada?.VentaId) value = null;
            if (SetProperty(ref _pagoSeleccionado, value)) NotificarCorreccion();
        }
    }
    public VentaCorreccionSugeridaItemViewModel? VentaCorreccionSeleccionada
    {
        get => _ventaCorreccionSeleccionada;
        private set
        {
            if (!SetProperty(ref _ventaCorreccionSeleccionada, value)) return;
            OnPropertyChanged(nameof(TieneVentaCorreccionSeleccionada));
            OnPropertyChanged(nameof(VentaCorreccionSeleccionadaTexto));
        }
    }
    public bool TieneVentaCorreccionSeleccionada => VentaCorreccionSeleccionada is not null;
    public string VentaCorreccionSeleccionadaTexto => VentaCorreccionSeleccionada?.ResumenTexto ?? string.Empty;
    public bool BuscandoVentas { get => _buscandoVentas; private set => SetProperty(ref _buscandoVentas, value); }
    public bool SugerenciasVentasAbiertas { get => _sugerenciasVentasAbiertas; private set => SetProperty(ref _sugerenciasVentasAbiertas, value); }
    public bool HaySugerenciasVentas => VentasSugeridas.Count > 0;
    public string EstadoBusquedaVentas { get => _estadoBusquedaVentas; private set => SetProperty(ref _estadoBusquedaVentas, value); }
    public ArqueoCiegoViewModel? Arqueo
    {
        get => _arqueo;
        private set
        {
            if (SetProperty(ref _arqueo, value))
            {
                OnPropertyChanged(nameof(EnArqueo));
                OnPropertyChanged(nameof(FueraDeArqueo));
            }
        }
    }
    public bool EnArqueo => Arqueo is not null;
    public bool FueraDeArqueo => Arqueo is null;
    public bool CajaAbierta => DetalleActual is not null;
    public bool CajaCerrada => DetalleActual is null;
    public bool SinMovimientos => CajaAbierta && Movimientos.Count == 0;
    public bool SinHistorial => Historial.Count == 0;
    public bool SinPagosCorregibles => PagosCorregibles.Count == 0;
    public bool TienePagoSeleccionado => PagoSeleccionado is not null;
    public bool SinPagoSeleccionado => PagoSeleccionado is null;
    public bool PuedeConfirmarCorreccion => PuedeCorregir && CajaAbierta && PagoSeleccionado is not null
        && !Ocupado && string.IsNullOrEmpty(ErrorValidacionCorreccion)
        && MediosPago.Manuales.Contains(MedioNuevo, StringComparer.Ordinal)
        && MedioNuevo != PagoSeleccionado.MedioActual;
    public string ErrorValidacionCorreccion => PagoSeleccionado is null ? string.Empty
        : !CajaAbierta ? "Abrí una sesión para recibir el ajuste."
        : PagoSeleccionado.MedioOriginal == MediosPago.MercadoPago || PagoSeleccionado.MedioActual == MediosPago.MercadoPago
            ? "Mercado Pago Point requiere un flujo remoto y no se corrige aquí."
        : !MediosPago.Manuales.Contains(MedioNuevo, StringComparer.Ordinal) ? "Elegí una forma de cobro manual válida."
        : MedioNuevo == PagoSeleccionado.MedioActual ? "Elegí una forma distinta de la forma original."
        : string.IsNullOrWhiteSpace(MotivoCorreccion) ? "El motivo es obligatorio." : string.Empty;
    public string SesionOrigenTexto => PagoSeleccionado is null ? string.Empty
        : $"Sesión original: {NombreUsuario(PagoSeleccionado.UsuarioSesionOriginal)} · {FormatearFechaSesion(PagoSeleccionado.SesionOriginalAbiertaEn)}";
    public string SesionDestinoTexto => DetalleActual is null ? "No hay una sesión abierta para recibir el ajuste."
        : $"El ajuste se registrará en esta sesión: {NombreUsuario(DetalleActual.Sesion.UsuarioAperturaNombre)} · {FormatoFecha(DetalleActual.Sesion.AbiertaEn)}";
    public string ImpactoEfectivoTexto
    {
        get
        {
            if (PagoSeleccionado is null || DetalleActual?.EfectivoEsperadoMinor is not long esperado)
                return "El efectivo esperado no cambia hasta confirmar.";
            var delta = (MedioNuevo == MediosCaja.Efectivo ? PagoSeleccionado.ImporteMinor : 0)
                - (PagoSeleccionado.MedioActual == MediosCaja.Efectivo ? PagoSeleccionado.ImporteMinor : 0);
            var nuevo = esperado + delta;
            var impacto = delta == 0 ? "sin cambios" : delta > 0 ? $"aumenta {PosViewModel.FormatearArs(delta)}" : $"disminuye {PosViewModel.FormatearArs(-delta)}";
            return $"Efectivo esperado de la sesión abierta: {PosViewModel.FormatearArs(esperado)} → {PosViewModel.FormatearArs(nuevo)} ({impacto}).";
        }
    }
    public bool PuedeAbrir { get => _puedeAbrir; private set => SetProperty(ref _puedeAbrir, value); }
    public bool PuedeConsultar { get => _puedeConsultar; private set => SetProperty(ref _puedeConsultar, value); }
    public bool PuedeMover { get => _puedeMover; private set => SetProperty(ref _puedeMover, value); }
    public bool PuedeCerrar { get => _puedeCerrar; private set => SetProperty(ref _puedeCerrar, value); }
    public bool PuedeRetirar { get => _puedeRetirar; private set { if (SetProperty(ref _puedeRetirar, value)) ActualizarCategorias(); } }
    public bool PuedeCorregir { get => _puedeCorregir; private set { if (SetProperty(ref _puedeCorregir, value)) NotificarCorreccion(); } }
    public bool PuedeVer => PuedeAbrir || PuedeConsultar || PuedeMover || PuedeCerrar;
    public bool Ocupado { get => _ocupado; private set { if (SetProperty(ref _ocupado, value)) NotificarCorreccion(); } }
    public bool HayMasHistorial { get => _hayMasHistorial; private set => SetProperty(ref _hayMasHistorial, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Mensaje { get => _mensaje; private set => SetProperty(ref _mensaje, value); }

    public string FondoInicialTexto { get => _fondoInicialTexto; set => SetProperty(ref _fondoInicialTexto, value); }
    public string TipoMovimientoSeleccionado
    {
        get => _tipoMovimiento;
        set { if (SetProperty(ref _tipoMovimiento, value)) ActualizarCategorias(); }
    }
    public string ImporteMovimientoTexto { get => _importeMovimientoTexto; set => SetProperty(ref _importeMovimientoTexto, value); }
    public string MotivoMovimiento { get => _motivoMovimiento; set => SetProperty(ref _motivoMovimiento, value); }
    public string Observacion { get => _observacion; set => SetProperty(ref _observacion, value); }
    public string BusquedaPago
    {
        get => _busquedaPago;
        set
        {
            if (!SetProperty(ref _busquedaPago, value)) return;
            InvalidarVentaCorreccionSeleccionada();
            IniciarBusquedaVentas(value);
        }
    }
    public string MedioNuevo { get => _medioNuevo; set { if (SetProperty(ref _medioNuevo, value)) NotificarCorreccion(); } }
    public string MotivoCorreccion { get => _motivoCorreccion; set { if (SetProperty(ref _motivoCorreccion, value)) NotificarCorreccion(); } }
    public OpcionCategoriaCaja? CategoriaSeleccionada { get => _categoriaSeleccionada; set { if (SetProperty(ref _categoriaSeleccionada, value)) OnPropertyChanged(nameof(EnlaceCategoriaPropietarioVisible)); } }

    public string EstadoTexto => CajaAbierta ? "Abierta" : "Cerrada";
    public string SesionTexto => DetalleActual is null ? "No hay una sesión activa." : $"Abierta por {DetalleActual.Sesion.UsuarioAperturaNombre ?? "Usuario"} · {FormatoFecha(DetalleActual.Sesion.AbiertaEn)}";
    public string FondoInicialActualTexto => DetalleActual is null ? "ARS 0,00" : PosViewModel.FormatearArs(DetalleActual.Sesion.FondoInicialMinor);
    public string EfectivoEsperadoTexto => DetalleActual?.EfectivoEsperadoMinor is long n ? PosViewModel.FormatearArs(n) : "ARS 0,00";
    public string CobrosEfectivoTexto => FormatearSuma(TiposMovimientoCaja.Venta, MediosCaja.Efectivo);
    public string IngresosTexto => FormatearImporte(Math.Max(0, Suma(TiposMovimientoCaja.Ingreso)));
    public string EgresosTexto => FormatearImporte(Math.Abs(Math.Min(0, Suma(TiposMovimientoCaja.Egreso))));
    public string ReintegrosEfectivoTexto => FormatearImporte(Math.Abs(Math.Min(0, Suma(TiposMovimientoCaja.Reintegro, MediosCaja.Efectivo) + Suma(TiposMovimientoCaja.CambioReintegro, MediosCaja.Efectivo))));
    public string CompensacionesEfectivoTexto => FormatearImporte(Suma(TiposMovimientoCaja.Correccion, MediosCaja.Efectivo));
    public string TransferenciaTexto => FormatearImporte(SumaMedio(MediosCaja.Transferencia));
    public string DebitoTexto => FormatearImporte(SumaMedio(MediosCaja.Debito));
    public string CreditoTexto => FormatearImporte(SumaMedio(MediosCaja.Credito));
    public string BilleteraTexto => FormatearImporte(SumaMedio(MediosCaja.Billetera));
    public string MercadoPagoTexto => FormatearImporte(SumaMedio(MediosPago.MercadoPago));
    public bool EnlaceCategoriaPropietarioVisible => CategoriaSeleccionada?.Valor == CategoriasMovimientoCaja.RetiroPropietario && !PuedeRetirar;

    public async Task CargarAsyncPublic() => await CargarAsync();
    public async Task AbrirCajaAsyncPublic() => await AbrirCajaAsync();
    public async Task RegistrarMovimientoAsyncPublic() => await RegistrarMovimientoAsync();
    public async Task BuscarPagosAsyncPublic() => await BuscarPagosAsync();
    public async Task SeleccionarVentaSugeridaAsyncPublic(VentaCorreccionSugeridaItemViewModel venta) => await SeleccionarVentaSugeridaAsync(venta);
    public async Task CorregirMedioAsyncPublic() => await CorregirMedioAsync();
    public async Task CargarDetalleHistoricoAsyncPublic(string id) => await CargarDetalleHistoricoAsync(id);

    private async Task CargarAsync()
    {
        Error = string.Empty; Mensaje = string.Empty;
        var actualizarSugerencias = SugerenciasVentasAbiertas && VentaCorreccionSeleccionada is null && !BuscandoVentas;
        var versionBusquedaAlIniciar = Volatile.Read(ref _versionBusquedaVentas);
        try
        {
            var sesion = _idSesion() ?? throw new InvalidOperationException("La sesión de usuario no está disponible.");
            var permisos = await _servicios.Autorizador.ObtenerPermisosEfectivosAsync(sesion, PermisosCaja.Todos);
            PuedeAbrir = permisos.Contains(PermisosCaja.Abrir);
            PuedeConsultar = permisos.Contains(PermisosCaja.Consultar);
            PuedeMover = permisos.Contains(PermisosCaja.Movimiento);
            PuedeCerrar = permisos.Contains(PermisosCaja.Cerrar);
            PuedeRetirar = permisos.Contains(PermisosCaja.RetiroPropietario);
            PuedeCorregir = permisos.Contains(PermisosCaja.CorregirMedio);
            OnPropertyChanged(nameof(PuedeVer));
            if (!PuedeConsultar)
            {
                DetalleActual = null;
                return;
            }

            DetalleActual = await _servicios.ConsultasCaja.ObtenerActualAsync(sesion);
            Movimientos.Clear();
            if (DetalleActual is not null)
                foreach (var item in DetalleActual.Movimientos)
                    Movimientos.Add(new MovimientoCajaViewModel(item));
            OnPropertyChanged(nameof(SinMovimientos));
            NotificarImportes();
            if (PuedeConsultar) await CargarPaginaHistorialAsync(0, reemplazar: true);
            _busquedaVentasInicializada = false;
            if (actualizarSugerencias && PuedeCorregir && PuedeConsultar
                && versionBusquedaAlIniciar == Volatile.Read(ref _versionBusquedaVentas) && !BuscandoVentas)
                await EjecutarBusquedaVentasAsync(BusquedaPago, aplicarDemora: false);
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    private async Task AbrirCajaAsync()
    {
        Error = string.Empty; Mensaje = string.Empty;
        if (!ImporteArs.TryParse(FondoInicialTexto, out var fondo) || fondo < 0)
        { Error = "Ingresá un fondo inicial válido en pesos."; return; }
        Ocupado = true;
        try
        {
            await _servicios.CasoAbrirCaja.EjecutarAsync(fondo, _servicios.GeneradorIds.NuevoId(), _idSesion());
            NotificarCajaActualizada();
            Mensaje = "La caja quedó abierta.";
            await CargarAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task RegistrarMovimientoAsync()
    {
        Error = string.Empty; Mensaje = string.Empty;
        if (CategoriaSeleccionada is null) { Error = "Seleccioná una categoría."; return; }
        if (!ImporteArs.TryParse(ImporteMovimientoTexto, out var importe) || importe <= 0)
        { Error = "Ingresá un importe mayor que cero."; return; }
        if (string.IsNullOrWhiteSpace(MotivoMovimiento)) { Error = "El motivo es obligatorio."; return; }
        Ocupado = true;
        try
        {
            await _servicios.CasoRegistrarMovimientoCaja.EjecutarAsync(CategoriaSeleccionada.Valor, importe, MotivoMovimiento, _servicios.GeneradorIds.NuevoId(), _idSesion());
            NotificarCajaActualizada();
            ImporteMovimientoTexto = string.Empty; MotivoMovimiento = string.Empty;
            Mensaje = "El movimiento quedó registrado.";
            await CargarAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private void IniciarArqueo()
    {
        Error = string.Empty; Mensaje = string.Empty;
        if (!PuedeCerrar || DetalleActual is null) return;
        var sesion = DetalleActual.Sesion;
        Arqueo = new ArqueoCiegoViewModel(
            _servicios,
            _idSesion,
            sesion.Id,
            sesion.UsuarioAperturaNombre ?? "Usuario",
            sesion.AbiertaEn,
            VolverDeArqueoAsync,
            CierreCompletadoAsync);
    }

    private Task VolverDeArqueoAsync() { Arqueo = null; return Task.CompletedTask; }

    private async Task CierreCompletadoAsync()
    {
        Arqueo = null;
        Mensaje = "La caja quedó cerrada.";
        await CargarAsync();
        NotificarCajaActualizada();
    }

    private async Task BuscarPagosAsync()
    {
        await EjecutarBusquedaVentasAsync(BusquedaPago, aplicarDemora: false);
    }

    private void IniciarBusquedaVentas(string filtro)
    {
        var version = Interlocked.Increment(ref _versionBusquedaVentas);
        var cts = new CancellationTokenSource();
        var anterior = Interlocked.Exchange(ref _busquedaVentasCts, cts);
        anterior?.Cancel();
        anterior?.Dispose();
        _ = EjecutarBusquedaVentasAsync(filtro, version, cts, aplicarDemora: true);
    }

    private async Task EjecutarBusquedaVentasAsync(string filtro, bool aplicarDemora)
    {
        VentaCorreccionSeleccionada = null;
        PagoSeleccionado = null;
        PagosCorregibles.Clear();
        OnPropertyChanged(nameof(SinPagosCorregibles));
        var version = Interlocked.Increment(ref _versionBusquedaVentas);
        var cts = new CancellationTokenSource();
        var anterior = Interlocked.Exchange(ref _busquedaVentasCts, cts);
        anterior?.Cancel();
        anterior?.Dispose();
        await EjecutarBusquedaVentasAsync(filtro, version, cts, aplicarDemora);
    }

    private async Task EjecutarBusquedaVentasAsync(string filtro, int version, CancellationTokenSource cts, bool aplicarDemora)
    {
        var cancellationToken = cts.Token;
        if (!PuedeCorregir || !PuedeConsultar)
        {
            Interlocked.CompareExchange(ref _busquedaVentasCts, null, cts);
            cts.Dispose();
            return;
        }
        Error = string.Empty;
        BuscandoVentas = true;
        SugerenciasVentasAbiertas = true;
        VaciarSugerenciasVentas();
        EstadoBusquedaVentas = string.IsNullOrWhiteSpace(filtro) ? "Buscando ventas recientes…" : "Buscando…";
        try
        {
            if (aplicarDemora) await Task.Delay(250, cancellationToken);
            var resultados = await _servicios.ConsultasCaja.BuscarVentasParaCorreccionAsync(filtro, _idSesion(), cancellationToken);
            if (version != Volatile.Read(ref _versionBusquedaVentas) || cancellationToken.IsCancellationRequested) return;
            foreach (var venta in resultados) VentasSugeridas.Add(new VentaCorreccionSugeridaItemViewModel(venta));
            _busquedaVentasInicializada = true;
            OnPropertyChanged(nameof(HaySugerenciasVentas));
            EstadoBusquedaVentas = resultados.Count == 0
                ? (string.IsNullOrWhiteSpace(filtro) ? "No hay ventas confirmadas recientes con pagos." : "No se encontraron ventas para esa búsqueda.")
                : string.IsNullOrWhiteSpace(filtro) ? "Ventas recientes" : "Elegí una venta para revisar sus cobros.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (version == Volatile.Read(ref _versionBusquedaVentas))
            {
                Error = MensajeError(ex);
                EstadoBusquedaVentas = "No se pudo buscar ventas.";
            }
        }
        finally
        {
            if (version == Volatile.Read(ref _versionBusquedaVentas)) BuscandoVentas = false;
            Interlocked.CompareExchange(ref _busquedaVentasCts, null, cts);
            cts.Dispose();
        }
    }

    private async Task SeleccionarVentaSugeridaAsync(object? parameter)
    {
        if (parameter is not VentaCorreccionSugeridaItemViewModel item) return;
        Interlocked.Increment(ref _versionBusquedaVentas);
        var busquedaPendiente = Interlocked.Exchange(ref _busquedaVentasCts, null);
        busquedaPendiente?.Cancel();
        busquedaPendiente?.Dispose();
        var venta = item.Datos;
        VentaCorreccionSeleccionada = item;
        PagoSeleccionado = null;
        PagosCorregibles.Clear();
        OnPropertyChanged(nameof(SinPagosCorregibles));
        SugerenciasVentasAbiertas = false;
        if (!venta.TienePagosCorregibles)
        {
            EstadoBusquedaVentas = venta.SoloPoint
                ? "Esta venta se cobró únicamente con Mercado Pago Point y no admite corrección por esta vía."
                : "Esta venta no tiene pagos manuales disponibles para corregir.";
            return;
        }

        Error = string.Empty;
        Ocupado = true;
        try
        {
            var pagos = await _servicios.ConsultasCaja.ListarPagosCorregiblesPorVentaAsync(venta.VentaId, _idSesion());
            if (!ReferenceEquals(VentaCorreccionSeleccionada, item)) return;
            foreach (var pago in pagos) PagosCorregibles.Add(pago);
            OnPropertyChanged(nameof(SinPagosCorregibles));
            EstadoBusquedaVentas = pagos.Count == 0
                ? "Esta venta ya no tiene pagos disponibles para corregir."
                : "Seleccioná el pago registrado que necesitás corregir.";
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    public void CerrarSugerenciasVentas() => SugerenciasVentasAbiertas = false;

    public void AbrirSugerenciasVentas()
    {
        if (VentaCorreccionSeleccionada is not null) return;
        SugerenciasVentasAbiertas = true;
        if (!_busquedaVentasInicializada && !BuscandoVentas) IniciarBusquedaVentas(BusquedaPago);
    }

    public void LimpiarBusquedaVentas()
    {
        if (!string.IsNullOrEmpty(BusquedaPago))
        {
            BusquedaPago = string.Empty;
            return;
        }
        InvalidarVentaCorreccionSeleccionada();
        IniciarBusquedaVentas(string.Empty);
    }

    private void InvalidarVentaCorreccionSeleccionada()
    {
        _busquedaVentasInicializada = false;
        VentaCorreccionSeleccionada = null;
        PagoSeleccionado = null;
        PagosCorregibles.Clear();
        VaciarSugerenciasVentas();
        SugerenciasVentasAbiertas = true;
        EstadoBusquedaVentas = "Buscando…";
        OnPropertyChanged(nameof(SinPagosCorregibles));
    }

    private void VaciarSugerenciasVentas()
    {
        VentasSugeridas.Clear();
        OnPropertyChanged(nameof(HaySugerenciasVentas));
    }

    private async Task CorregirMedioAsync()
    {
        Error = string.Empty; Mensaje = string.Empty;
        if (!PuedeCorregir) { Error = "No tenés permiso para corregir medios de pago."; return; }
        if (PagoSeleccionado is null) { Error = "Buscá y seleccioná un pago para corregir."; return; }
        if (!CajaAbierta) { Error = "Abrí una sesión de caja para recibir el ajuste."; return; }
        if (PagoSeleccionado.MedioOriginal == MediosPago.MercadoPago || PagoSeleccionado.MedioActual == MediosPago.MercadoPago)
        { Error = "Los cobros de Mercado Pago Point no se pueden reclasificar desde Caja. Requieren un flujo remoto específico."; return; }
        if (!MediosPago.Manuales.Contains(MedioNuevo, StringComparer.Ordinal)) { Error = "Elegí una forma de cobro manual válida."; return; }
        if (MedioNuevo == PagoSeleccionado.MedioActual) { Error = "Elegí una forma de cobro distinta del medio original."; return; }
        if (string.IsNullOrWhiteSpace(MotivoCorreccion)) { Error = "El motivo de la corrección es obligatorio."; return; }
        Ocupado = true;
        try
        {
            var numeroVenta = PagoSeleccionado.NumeroVenta;
            await _servicios.CasoCorregirMedioPago.EjecutarAsync(PagoSeleccionado.PagoId, MedioNuevo, MotivoCorreccion, _servicios.GeneradorIds.NuevoId(), _idSesion());
            NotificarCajaActualizada();
            MotivoCorreccion = string.Empty;
            Mensaje = $"Se corrigió el medio del pago de la venta {numeroVenta}.";
            InvalidarVentaCorreccionSeleccionada();
            await CargarAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CargarMasHistorialAsync()
    {
        if (!PuedeConsultar || !HayMasHistorial || Ocupado) return;
        Ocupado = true;
        try { await CargarPaginaHistorialAsync(Historial.Count, reemplazar: false); }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CargarPaginaHistorialAsync(int desplazamiento, bool reemplazar)
    {
        var lista = await _servicios.ConsultasCaja.BuscarHistorialAsync(TamanoPagina, desplazamiento, _idSesion());
        if (reemplazar) Historial.Clear();
        HayMasHistorial = lista.Count > TamanoPagina;
        foreach (var sesion in lista.Take(TamanoPagina)) Historial.Add(sesion);
        OnPropertyChanged(nameof(SinHistorial));
    }

    private async Task CargarDetalleHistoricoAsync(string id)
    {
        Error = string.Empty;
        try
        {
            DetalleHistorico = await _servicios.ConsultasCaja.ObtenerDetalleAsync(id, _idSesion());
            MovimientosHistoricos.Clear();
            if (DetalleHistorico is not null)
                foreach (var item in DetalleHistorico.Movimientos)
                    MovimientosHistoricos.Add(new MovimientoCajaViewModel(item));
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    private long Suma(string tipo, string? medio = null)
        => DetalleActual?.ResumenMovimientos.Where(r => r.Tipo == tipo && (medio is null || r.Medio == medio)).Sum(r => r.ImporteMinor) ?? 0;

    private long SumaMedio(string medio) => DetalleActual?.ResumenMedios.FirstOrDefault(r => r.Medio == medio)?.ImporteMinor ?? 0;

    private string FormatearSuma(string tipo, string medio) => FormatearImporte(Suma(tipo, medio));
    private static string FormatearImporte(long minor) => PosViewModel.FormatearArs(minor);

    private void ActualizarCategorias()
    {
        var anterior = CategoriaSeleccionada?.Valor;
        CategoriasDisponibles.Clear();
        var opciones = TipoMovimientoSeleccionado == "Ingreso"
            ? new[] { new OpcionCategoriaCaja(CategoriasMovimientoCaja.CambioFondo, "Agregar cambio"), new OpcionCategoriaCaja(CategoriasMovimientoCaja.OtroIngreso, "Otro ingreso") }
            : new[] { new OpcionCategoriaCaja(CategoriasMovimientoCaja.Gasto, "Gasto"), new OpcionCategoriaCaja(CategoriasMovimientoCaja.RetiroPropietario, "Retiro del propietario"), new OpcionCategoriaCaja(CategoriasMovimientoCaja.OtroEgreso, "Otro egreso") };
        foreach (var opcion in opciones.Where(x => x.Valor != CategoriasMovimientoCaja.RetiroPropietario || PuedeRetirar)) CategoriasDisponibles.Add(opcion);
        CategoriaSeleccionada = CategoriasDisponibles.FirstOrDefault(x => x.Valor == anterior) ?? CategoriasDisponibles.FirstOrDefault();
        OnPropertyChanged(nameof(CategoriaSeleccionada));
        OnPropertyChanged(nameof(EnlaceCategoriaPropietarioVisible));
    }

    private void NotificarEstado()
    {
        OnPropertyChanged(nameof(CajaAbierta)); OnPropertyChanged(nameof(CajaCerrada));
        OnPropertyChanged(nameof(EstadoTexto)); OnPropertyChanged(nameof(SesionTexto));
        OnPropertyChanged(nameof(FondoInicialActualTexto)); OnPropertyChanged(nameof(EfectivoEsperadoTexto));
        OnPropertyChanged(nameof(SinMovimientos));
        OnPropertyChanged(nameof(CobrosEfectivoTexto)); OnPropertyChanged(nameof(IngresosTexto));
        OnPropertyChanged(nameof(EgresosTexto)); OnPropertyChanged(nameof(ReintegrosEfectivoTexto));
        OnPropertyChanged(nameof(CompensacionesEfectivoTexto)); OnPropertyChanged(nameof(TransferenciaTexto));
        OnPropertyChanged(nameof(DebitoTexto)); OnPropertyChanged(nameof(CreditoTexto)); OnPropertyChanged(nameof(BilleteraTexto));
        OnPropertyChanged(nameof(PuedeVer));
        NotificarCorreccion();
    }

    private void NotificarImportes()
    {
        OnPropertyChanged(nameof(CobrosEfectivoTexto)); OnPropertyChanged(nameof(IngresosTexto));
        OnPropertyChanged(nameof(EgresosTexto)); OnPropertyChanged(nameof(ReintegrosEfectivoTexto));
        OnPropertyChanged(nameof(CompensacionesEfectivoTexto)); OnPropertyChanged(nameof(TransferenciaTexto));
        OnPropertyChanged(nameof(DebitoTexto)); OnPropertyChanged(nameof(CreditoTexto)); OnPropertyChanged(nameof(BilleteraTexto));
    }

    private void NotificarCorreccion()
    {
        OnPropertyChanged(nameof(TienePagoSeleccionado));
        OnPropertyChanged(nameof(SinPagoSeleccionado));
        OnPropertyChanged(nameof(PuedeConfirmarCorreccion));
        OnPropertyChanged(nameof(ErrorValidacionCorreccion));
        OnPropertyChanged(nameof(SesionOrigenTexto));
        OnPropertyChanged(nameof(SesionDestinoTexto));
        OnPropertyChanged(nameof(ImpactoEfectivoTexto));
        OnPropertyChanged(nameof(VentaCorreccionSeleccionadaTexto));
    }

    private static string FormatoFecha(string valor)
        => DateTimeOffset.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fecha)
            ? fecha.ToLocalTime().ToString("HH:mm", CultureInfo.GetCultureInfo("es-AR")) : valor;

    private static string FormatearFechaSesion(string? valor)
        => DateTimeOffset.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fecha)
            ? fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-AR")) : "fecha no disponible";
    private static string NombreUsuario(string? nombre) => string.IsNullOrWhiteSpace(nombre) ? "usuario no disponible" : nombre;

    private static string FormatearFechaVenta(string valor)
        => DateTimeOffset.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fecha)
            ? fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-AR")) : valor;

    private static string MensajeError(Exception ex) => ex switch
    {
        LocalInfantil.Application.Abstractions.AccesoDenegadoException => "No tenés permiso para realizar esta operación.",
        LocalInfantil.Application.Abstractions.SesionInvalidaException => "La sesión venció. Iniciá sesión nuevamente.",
        _ => ex.Message,
    };

    internal void NotificarCajaActualizada()
        => _servicios.NotificadorCambios.Publicar(this, AreaDatos.Caja, AreaDatos.Caja);
}

public sealed record OpcionMedioCaja(string Valor, string Texto);

public sealed class VentaCorreccionSugeridaItemViewModel
{
    private static readonly IReadOnlyDictionary<string, string> NombresMedios = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [MediosPago.Efectivo] = "Efectivo",
        [MediosPago.Transferencia] = "Transferencia",
        [MediosPago.Debito] = "Débito",
        [MediosPago.Credito] = "Crédito",
        [MediosPago.Billetera] = "Billetera virtual",
        [MediosPago.MercadoPago] = "Mercado Pago Point",
    };

    public VentaCorreccionSugeridaItemViewModel(VentaCorreccionSugerida datos)
    {
        Datos = datos;
        Fecha = DateTimeOffset.TryParse(datos.CreadaEn, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fecha)
            ? fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-AR")) : datos.CreadaEn;
        Total = PosViewModel.FormatearArs(datos.TotalMinor);
        Medios = string.Join(" · ", datos.MediosPago.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).Select(m => NombresMedios.GetValueOrDefault(m, "Forma de pago")));
        Estado = datos.TienePagosCorregibles ? "Disponible para revisar"
            : datos.SoloPoint ? "Solo Point · no corregible por esta vía" : "Sin pagos corregibles";
        ResumenTexto = $"Venta {datos.Numero} · {Fecha} · {Total}";
    }

    public VentaCorreccionSugerida Datos { get; }
    public string VentaId => Datos.VentaId;
    public string Numero => Datos.Numero;
    public string Fecha { get; }
    public string Total { get; }
    public string Medios { get; }
    public string Estado { get; }
    public string ResumenTexto { get; }
}

public sealed class MovimientoCajaViewModel
{
    public MovimientoCajaViewModel(MovimientoCajaRegistrado movimiento)
    {
        Fecha = DateTimeOffset.TryParse(movimiento.CreadoEn, out var fecha)
            ? fecha.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.GetCultureInfo("es-AR")) : movimiento.CreadoEn;
        Descripcion = Describir(movimiento);
        Medio = TextoMedio(movimiento.Medio);
        Importe = $"{(movimiento.ImporteMinor > 0 ? "+" : "−")}{PosViewModel.FormatearArs(Math.Abs(movimiento.ImporteMinor))}";
        Usuario = movimiento.UsuarioNombre ?? "—";
        Motivo = movimiento.Motivo ?? string.Empty;
    }

    public string Fecha { get; }
    public string Descripcion { get; }
    public string Medio { get; }
    public string Importe { get; }
    public string Usuario { get; }
    public string Motivo { get; }

    internal static string TextoMedio(string? medio) => medio switch
    {
        MediosCaja.Efectivo => "Efectivo",
        MediosCaja.Transferencia => "Transferencia",
        MediosCaja.Debito => "Débito",
        MediosCaja.Credito => "Crédito",
        MediosCaja.Billetera => "Billetera virtual",
        _ => "—",
    };

    private static string Describir(MovimientoCajaRegistrado m)
    {
        if (m.Tipo == TiposMovimientoCaja.CambioCobro) return "Diferencia de cambio · Cobro";
        if (m.Tipo == TiposMovimientoCaja.CambioReintegro) return "Diferencia de cambio · Reintegro";
        if (m.Tipo == TiposMovimientoCaja.Venta) return $"Venta {m.NumeroVenta ?? string.Empty}";
        if (m.Tipo == TiposMovimientoCaja.Reintegro) return $"Reintegro · Venta {m.NumeroVenta ?? string.Empty}";
        if (m.Tipo == TiposMovimientoCaja.Correccion) return $"Corrección de medio · Venta {m.NumeroVenta ?? string.Empty}";
        var categoria = m.Categoria switch
        {
            CategoriasMovimientoCaja.CambioFondo => "Cambio de fondo",
            CategoriasMovimientoCaja.OtroIngreso => "Otro ingreso",
            CategoriasMovimientoCaja.Gasto => "Gasto",
            CategoriasMovimientoCaja.RetiroPropietario => "Retiro del propietario",
            CategoriasMovimientoCaja.OtroEgreso => "Otro egreso",
            _ => m.Tipo == TiposMovimientoCaja.Ingreso ? "Ingreso" : "Egreso",
        };
        return string.IsNullOrWhiteSpace(m.Motivo) ? categoria : $"{categoria} · {m.Motivo}";
    }

}

internal static class ImporteArs
{
    public static bool TryParse(string? texto, out long minor)
    {
        minor = 0;
        if (!decimal.TryParse(texto, NumberStyles.Number, CultureInfo.GetCultureInfo("es-AR"), out var importe)
            || importe < 0 || decimal.Round(importe, 2) != importe)
            return false;
        try { minor = checked((long)(importe * 100m)); return true; }
        catch (OverflowException) { return false; }
    }
}
