using System.Collections.ObjectModel;
using System.Globalization;
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

    public CajaViewModel(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios ?? throw new ArgumentNullException(nameof(servicios));
        _idSesion = idSesion ?? throw new ArgumentNullException(nameof(idSesion));
        AbrirCommand = new AsyncRelayCommand(AbrirCajaAsync);
        RegistrarMovimientoCommand = new AsyncRelayCommand(RegistrarMovimientoAsync);
        IniciarArqueoCommand = new RelayCommand(_ => IniciarArqueo());
        BuscarPagosCommand = new AsyncRelayCommand(BuscarPagosAsync);
        CorregirMedioCommand = new AsyncRelayCommand(CorregirMedioAsync);
        CargarMasHistorialCommand = new AsyncRelayCommand(CargarMasHistorialAsync);
        ActualizarCategorias();
    }

    public ObservableCollection<MovimientoCajaViewModel> Movimientos { get; } = [];
    public ObservableCollection<MovimientoCajaViewModel> MovimientosHistoricos { get; } = [];
    public ObservableCollection<SesionCajaRegistrada> Historial { get; } = [];
    public ObservableCollection<PagoCajaActual> PagosCorregibles { get; } = [];
    public ObservableCollection<OpcionCategoriaCaja> CategoriasDisponibles { get; } = [];
    public IReadOnlyList<string> TiposMovimiento { get; } = ["Ingreso", "Egreso"];
    public IReadOnlyList<OpcionMedioCaja> MediosDisponibles { get; } =
    [
        new(MediosCaja.Efectivo, "Efectivo"),
        new(MediosCaja.Transferencia, "Transferencia"),
        new(MediosCaja.Debito, "Débito"),
        new(MediosCaja.Credito, "Crédito"),
        new(MediosCaja.Billetera, "Billetera virtual"),
        new(MediosPago.MercadoPago, "Mercado Pago Point"),
    ];

    public ICommand AbrirCommand { get; }
    public ICommand RegistrarMovimientoCommand { get; }
    public ICommand IniciarArqueoCommand { get; }
    public ICommand BuscarPagosCommand { get; }
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
    public PagoCajaActual? PagoSeleccionado { get => _pagoSeleccionado; set => SetProperty(ref _pagoSeleccionado, value); }
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
    public bool PuedeAbrir { get => _puedeAbrir; private set => SetProperty(ref _puedeAbrir, value); }
    public bool PuedeConsultar { get => _puedeConsultar; private set => SetProperty(ref _puedeConsultar, value); }
    public bool PuedeMover { get => _puedeMover; private set => SetProperty(ref _puedeMover, value); }
    public bool PuedeCerrar { get => _puedeCerrar; private set => SetProperty(ref _puedeCerrar, value); }
    public bool PuedeRetirar { get => _puedeRetirar; private set { if (SetProperty(ref _puedeRetirar, value)) ActualizarCategorias(); } }
    public bool PuedeCorregir { get => _puedeCorregir; private set => SetProperty(ref _puedeCorregir, value); }
    public bool PuedeVer => PuedeAbrir || PuedeConsultar || PuedeMover || PuedeCerrar;
    public bool Ocupado { get => _ocupado; private set => SetProperty(ref _ocupado, value); }
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
    public string BusquedaPago { get => _busquedaPago; set => SetProperty(ref _busquedaPago, value); }
    public string MedioNuevo { get => _medioNuevo; set => SetProperty(ref _medioNuevo, value); }
    public string MotivoCorreccion { get => _motivoCorreccion; set => SetProperty(ref _motivoCorreccion, value); }
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
    public async Task CorregirMedioAsyncPublic() => await CorregirMedioAsync();
    public async Task CargarDetalleHistoricoAsyncPublic(string id) => await CargarDetalleHistoricoAsync(id);

    private async Task CargarAsync()
    {
        Error = string.Empty; Mensaje = string.Empty;
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
    }

    private async Task BuscarPagosAsync()
    {
        Error = string.Empty;
        if (!PuedeCorregir) { Error = "No tenés permiso para corregir medios de pago."; return; }
        try
        {
            var lista = await _servicios.ConsultasCaja.BuscarPagosCorregiblesAsync(BusquedaPago, _idSesion());
            PagosCorregibles.Clear();
            foreach (var pago in lista) PagosCorregibles.Add(pago);
            PagoSeleccionado = PagosCorregibles.FirstOrDefault();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    private async Task CorregirMedioAsync()
    {
        Error = string.Empty; Mensaje = string.Empty;
        if (!PuedeCorregir) { Error = "No tenés permiso para corregir medios de pago."; return; }
        if (PagoSeleccionado is null) { Error = "Buscá y seleccioná un pago para corregir."; return; }
        if (string.IsNullOrWhiteSpace(MotivoCorreccion)) { Error = "El motivo de la corrección es obligatorio."; return; }
        Ocupado = true;
        try
        {
            await _servicios.CasoCorregirMedioPago.EjecutarAsync(PagoSeleccionado.PagoId, MedioNuevo, MotivoCorreccion, _servicios.GeneradorIds.NuevoId(), _idSesion());
            MotivoCorreccion = string.Empty;
            Mensaje = $"Se corrigió el medio del pago de la venta {PagoSeleccionado.NumeroVenta}.";
            await BuscarPagosAsync();
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
    }

    private void NotificarImportes()
    {
        OnPropertyChanged(nameof(CobrosEfectivoTexto)); OnPropertyChanged(nameof(IngresosTexto));
        OnPropertyChanged(nameof(EgresosTexto)); OnPropertyChanged(nameof(ReintegrosEfectivoTexto));
        OnPropertyChanged(nameof(CompensacionesEfectivoTexto)); OnPropertyChanged(nameof(TransferenciaTexto));
        OnPropertyChanged(nameof(DebitoTexto)); OnPropertyChanged(nameof(CreditoTexto)); OnPropertyChanged(nameof(BilleteraTexto));
    }

    private static string FormatoFecha(string valor)
        => DateTimeOffset.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fecha)
            ? fecha.ToLocalTime().ToString("HH:mm", CultureInfo.GetCultureInfo("es-AR")) : valor;

    private static string MensajeError(Exception ex) => ex switch
    {
        LocalInfantil.Application.Abstractions.AccesoDenegadoException => "No tenés permiso para realizar esta operación.",
        LocalInfantil.Application.Abstractions.SesionInvalidaException => "La sesión venció. Iniciá sesión nuevamente.",
        _ => ex.Message,
    };
}

public sealed record OpcionMedioCaja(string Valor, string Texto);

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
