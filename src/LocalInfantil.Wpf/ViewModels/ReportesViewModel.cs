using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Reportes;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.Printing;

namespace LocalInfantil.Wpf.ViewModels;

/// <summary>
/// Módulo Reportes: tres pestañas funcionales (Ventas, Caja, Inventario)
/// sin pestaña de costos hasta D-07. Filtros arriba, resumen compacto y
/// tabla principal paginada. Sin gráficos ni exportación.
/// </summary>
public sealed class ReportesViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private string _tabActiva = "Ventas";
    private string _preset = "Hoy";
    private DateTime _desde = DateTime.Today;
    private DateTime _hasta = DateTime.Today;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;

    private ResumenVentasPeriodo? _resumen;
    private string? _diaSeleccionado;
    private int _desplazamientoDetalle;
    private bool _hayMasDetalle;
    private bool _agruparPorProducto;
    private string _subAreaInventario = "Stock";
    private string _textoStock = string.Empty;
    private string? _categoriaId;
    private bool _soloCero;
    private string _hastaUnidadesTexto = string.Empty;
    private int _diasSinVentas = 30;
    private int _desplazamientoRanking;
    private bool _hayMasRanking;
    private int _desplazamientoSinVentas;
    private bool _hayMasSinVentas;
    private int _desplazamientoCaja;
    private bool _hayMasCaja;
    private DetalleSesionCaja? _detalleCaja;

    private bool _ventasConsultadas;
    private bool _cajaConsultada;
    private bool _stockConsultado;
    private bool _rankingConsultado;
    private bool _sinVentasConsultado;
    private bool _aplicandoPreset;

    private DateTime _desdeAplicado = DateTime.Today;
    private DateTime _hastaAplicado = DateTime.Today;
    private string _presetAplicado = "Hoy";
    private string _textoStockAplicado = string.Empty;
    private bool _soloCeroAplicado;
    private long? _hastaUnidadesAplicado;
    private int _diasSinVentasAplicado = 30;
    private bool _agruparAplicado;

    private const int TamanoPagina = 20;

    public ReportesViewModel(ServiciosComerciales servicios, Func<string?> idSesion, bool puedeVerVentas, bool puedeVerCaja, bool puedeVerInventario)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        PuedeVerVentas = puedeVerVentas;
        PuedeVerCaja = puedeVerCaja;
        PuedeVerInventario = puedeVerInventario;
        FilasDiarias = new ObservableCollection<FilaDiaria>();
        Detalle = new ObservableCollection<DetalleVentaReporte>();
        Medios = new ObservableCollection<DesgloseMedio>();
        SesionesCaja = new ObservableCollection<SesionCajaRegistrada>();
        FilasStock = new ObservableCollection<FilaStockActual>();
        Ranking = new ObservableCollection<RankingVenta>();
        SinVentasFilas = new ObservableCollection<SinVentas>();
        Categorias = new ObservableCollection<CategoriaRegistrada>();
        TabActiva = puedeVerVentas ? "Ventas" : puedeVerCaja ? "Caja" : "Inventario";
        SeleccionarTabCommand = new RelayCommand(t => { if (t is string s) TabActiva = s; });
        SeleccionarSubAreaCommand = new RelayCommand(s => { if (s is string a) SubAreaInventario = a; });
        AlternarSoloCeroCommand = new RelayCommand(_ => SoloCero = !SoloCero);
        AlternarAgrupacionCommand = new RelayCommand(_ => AgruparPorProducto = !AgruparPorProducto);
        AplicarPresetCommand = new RelayCommand(p => { if (p is string s) { Preset = s; AplicarPreset(); } });
        CargarVentasCommand = new AsyncRelayCommand(CargarVentasAsync);
        CargarDetalleCommand = new AsyncRelayCommand(CargarDetalleAsync);
        SiguienteDetalleCommand = new AsyncRelayCommand(() => MoverDetalleAsync(1));
        AnteriorDetalleCommand = new AsyncRelayCommand(() => MoverDetalleAsync(-1));
        CargarCajaCommand = new AsyncRelayCommand(CargarCajaAsync);
        SiguienteCajaCommand = new AsyncRelayCommand(() => MoverCajaAsync(1));
        AnteriorCajaCommand = new AsyncRelayCommand(() => MoverCajaAsync(-1));
        CargarInventarioCommand = new AsyncRelayCommand(CargarInventarioAsync);
        SiguienteRankingCommand = new AsyncRelayCommand(() => MoverRankingAsync(1));
        AnteriorRankingCommand = new AsyncRelayCommand(() => MoverRankingAsync(-1));
        SiguienteSinVentasCommand = new AsyncRelayCommand(() => MoverSinVentasAsync(1));
        AnteriorSinVentasCommand = new AsyncRelayCommand(() => MoverSinVentasAsync(-1));
        CargarCategoriasCommand = new AsyncRelayCommand(CargarCategoriasAsync);
    }

    public bool PuedeVerVentas { get; }
    public bool PuedeVerCaja { get; }
    public bool PuedeVerInventario { get; }

    public string TabActiva
    {
        get => _tabActiva;
        set
        {
            if (SetProperty(ref _tabActiva, value))
            {
                OnPropertyChanged(nameof(TabVentasVisible));
                OnPropertyChanged(nameof(TabCajaVisible));
                OnPropertyChanged(nameof(TabInventarioVisible));
                OnPropertyChanged(nameof(PuedeImprimir));
            }
        }
    }

    public bool TabVentasVisible => TabActiva == "Ventas" && PuedeVerVentas;
    public bool TabCajaVisible => TabActiva == "Caja" && PuedeVerCaja;
    public bool TabInventarioVisible => TabActiva == "Inventario" && PuedeVerInventario;

    public string Preset
    {
        get => _preset;
        set
        {
            if (SetProperty(ref _preset, value))
            {
                OnPropertyChanged(nameof(PresetHoy));
                OnPropertyChanged(nameof(PresetEsteMes));
                OnPropertyChanged(nameof(PresetPersonalizado));
            }
        }
    }

    public bool PresetHoy
    {
        get => Preset == "Hoy";
        set { if (value) AplicarPresetDesdeBoton("Hoy"); }
    }

    public bool PresetEsteMes
    {
        get => Preset == "Este mes";
        set { if (value) AplicarPresetDesdeBoton("Este mes"); }
    }

    public bool PresetPersonalizado
    {
        get => Preset == "Personalizado";
        set { if (value) Preset = "Personalizado"; }
    }

    public DateTime Desde
    {
        get => _desde;
        set
        {
            if (SetProperty(ref _desde, value) && !_aplicandoPreset) MarcarPeriodoPersonalizado();
        }
    }

    public DateTime Hasta
    {
        get => _hasta;
        set
        {
            if (SetProperty(ref _hasta, value) && !_aplicandoPreset) MarcarPeriodoPersonalizado();
        }
    }

    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }

    public bool Ocupado
    {
        get => _ocupado;
        set
        {
            if (SetProperty(ref _ocupado, value)) OnPropertyChanged(nameof(PuedeImprimir));
        }
    }

    public ResumenVentasPeriodo? Resumen
    {
        get => _resumen;
        private set
        {
            if (SetProperty(ref _resumen, value)) OnPropertyChanged(nameof(PuedeImprimir));
        }
    }

    /// <summary>
    /// Ajustes del resumen compacto: Netas − Brutas. Incluye anulaciones,
    /// devoluciones y también salidas positivas de cambio, por lo que puede
    /// ser negativo o positivo (ver converter con signo).
    /// </summary>
    public long AjustesVentasMinor => Resumen is null ? 0 : Resumen.NetasMinor - Resumen.BrutasMinor;
    public ObservableCollection<FilaDiaria> FilasDiarias { get; }
    public ObservableCollection<DetalleVentaReporte> Detalle { get; }
    public ObservableCollection<DesgloseMedio> Medios { get; }

    public string? DiaSeleccionado
    {
        get => _diaSeleccionado;
        set
        {
            if (SetProperty(ref _diaSeleccionado, value) && value is not null)
                _ = CargarDetalleDiaAsync(value);
        }
    }

    public bool HayMasDetalle { get => _hayMasDetalle; private set => SetProperty(ref _hayMasDetalle, value); }
    public bool HayAnteriorDetalle => _desplazamientoDetalle > 0;

    public ObservableCollection<SesionCajaRegistrada> SesionesCaja { get; }
    public bool HayMasCaja { get => _hayMasCaja; private set => SetProperty(ref _hayMasCaja, value); }
    public bool HayAnteriorCaja => _desplazamientoCaja > 0;
    public DetalleSesionCaja? DetalleCaja { get => _detalleCaja; private set => SetProperty(ref _detalleCaja, value); }

    public bool VentasConsultadas
    {
        get => _ventasConsultadas;
        private set
        {
            if (SetProperty(ref _ventasConsultadas, value)) NotificarEstado();
        }
    }

    public bool CajaConsultada
    {
        get => _cajaConsultada;
        private set
        {
            if (SetProperty(ref _cajaConsultada, value)) NotificarEstado();
        }
    }

    public bool StockConsultado
    {
        get => _stockConsultado;
        private set
        {
            if (SetProperty(ref _stockConsultado, value)) NotificarEstado();
        }
    }

    public bool RankingConsultado
    {
        get => _rankingConsultado;
        private set
        {
            if (SetProperty(ref _rankingConsultado, value)) NotificarEstado();
        }
    }

    public bool SinVentasConsultado
    {
        get => _sinVentasConsultado;
        private set
        {
            if (SetProperty(ref _sinVentasConsultado, value)) NotificarEstado();
        }
    }

    public bool EsReporteStock
    {
        get => SubAreaInventario == "Stock";
        set { if (value) SubAreaInventario = "Stock"; }
    }

    public bool EsReporteRanking
    {
        get => SubAreaInventario == "Ranking";
        set { if (value) SubAreaInventario = "Ranking"; }
    }

    public bool EsReporteSinVentas
    {
        get => SubAreaInventario == "SinVentas";
        set { if (value) SubAreaInventario = "SinVentas"; }
    }

    public bool PuedeImprimir => !Ocupado && TabActiva switch
    {
        "Ventas" => PuedeVerVentas && VentasConsultadas && Resumen is not null,
        "Caja" => PuedeVerCaja && CajaConsultada,
        "Inventario" => PuedeVerInventario && SubAreaInventario switch
        {
            "Stock" => StockConsultado,
            "Ranking" => RankingConsultado,
            _ => SinVentasConsultado,
        },
        _ => false,
    };

    public string TextoVacioVentas => !VentasConsultadas
        ? "Elegí un período y presioná Consultar para ver el reporte."
        : FilasDiarias.Count == 0 && Medios.Count == 0 && Detalle.Count == 0
            ? "No se encontraron ventas para el período seleccionado."
            : string.Empty;

    public string TextoVacioDetalle => VentasConsultadas && Detalle.Count == 0
        ? "No se encontraron movimientos de venta para el período seleccionado."
        : string.Empty;

    public string TextoVacioCaja => !CajaConsultada
        ? "Elegí un período y presioná Consultar para ver los cierres de caja."
        : SesionesCaja.Count == 0
            ? "No se encontraron cierres de caja para el período seleccionado."
            : string.Empty;

    public string TextoVacioStock => !StockConsultado
        ? "Consultá para ver el stock actual con los filtros aplicados."
        : FilasStock.Count == 0
            ? "No se encontraron artículos para los filtros seleccionados."
            : string.Empty;

    public string TextoVacioRanking => !RankingConsultado
        ? "Elegí un período y presioná Consultar para ver los artículos con más movimiento."
        : Ranking.Count == 0
            ? "No se encontraron movimientos de artículos para el período seleccionado."
            : string.Empty;

    public string TextoVacioSinVentas => !SinVentasConsultado
        ? "Consultá para ver los artículos sin ventas en el período indicado."
        : SinVentasFilas.Count == 0
            ? "No se encontraron artículos sin ventas en el período seleccionado."
            : string.Empty;

    public string TextoPaginacionDetalle => $"Página {_desplazamientoDetalle / TamanoPagina + 1} · {Detalle.Count} registros";
    public string TextoPaginacionCaja => $"Página {_desplazamientoCaja / TamanoPagina + 1} · {SesionesCaja.Count} registros";
    public string TextoPaginacionRanking => $"Página {_desplazamientoRanking / TamanoPagina + 1} · {Ranking.Count} registros";
    public string TextoPaginacionSinVentas => $"Página {_desplazamientoSinVentas / TamanoPagina + 1} · {SinVentasFilas.Count} registros";
    public string TextoConteoStock => $"{FilasStock.Count} artículos";

    private SesionCajaRegistrada? _sesionCajaSeleccionada;

    public SesionCajaRegistrada? SesionCajaSeleccionada
    {
        get => _sesionCajaSeleccionada;
        set
        {
            if (SetProperty(ref _sesionCajaSeleccionada, value) && value is not null)
                _ = CargarDetalleCajaAsync(value.Id);
        }
    }

    public bool AgruparPorProducto { get => _agruparPorProducto; set => SetProperty(ref _agruparPorProducto, value); }
    public string SubAreaInventario
    {
        get => _subAreaInventario;
        set
        {
            if (SetProperty(ref _subAreaInventario, value))
            {
                OnPropertyChanged(nameof(SubAreaStockVisible));
                OnPropertyChanged(nameof(SubAreaRankingVisible));
                OnPropertyChanged(nameof(SubAreaSinVentasVisible));
                OnPropertyChanged(nameof(EsReporteStock));
                OnPropertyChanged(nameof(EsReporteRanking));
                OnPropertyChanged(nameof(EsReporteSinVentas));
                OnPropertyChanged(nameof(PuedeImprimir));
            }
        }
    }

    public bool SubAreaStockVisible => SubAreaInventario == "Stock";
    public bool SubAreaRankingVisible => SubAreaInventario == "Ranking";
    public bool SubAreaSinVentasVisible => SubAreaInventario == "SinVentas";

    public string DiasSinVentasTexto
    {
        get => _diasSinVentas.ToString();
        set
        {
            if (int.TryParse(value.Trim(), out var n) && n > 0)
                DiasSinVentas = n;
            else
                OnPropertyChanged(nameof(DiasSinVentasTexto));
        }
    }
    public string TextoStock { get => _textoStock; set => SetProperty(ref _textoStock, value); }
    public ObservableCollection<CategoriaRegistrada> Categorias { get; }
    public string? CategoriaId { get => _categoriaId; set => SetProperty(ref _categoriaId, value); }
    public bool SoloCero { get => _soloCero; set => SetProperty(ref _soloCero, value); }
    public string HastaUnidadesTexto { get => _hastaUnidadesTexto; set => SetProperty(ref _hastaUnidadesTexto, value); }
    public int DiasSinVentas { get => _diasSinVentas; set => SetProperty(ref _diasSinVentas, value > 0 ? value : 30); }
    public ObservableCollection<FilaStockActual> FilasStock { get; }
    public ObservableCollection<RankingVenta> Ranking { get; }
    public ObservableCollection<SinVentas> SinVentasFilas { get; }
    public bool HayMasRanking { get => _hayMasRanking; private set => SetProperty(ref _hayMasRanking, value); }
    public bool HayAnteriorRanking => _desplazamientoRanking > 0;
    public bool HayMasSinVentas { get => _hayMasSinVentas; private set => SetProperty(ref _hayMasSinVentas, value); }
    public bool HayAnteriorSinVentas => _desplazamientoSinVentas > 0;

    public RelayCommand SeleccionarTabCommand { get; }
    public RelayCommand SeleccionarSubAreaCommand { get; }
    public RelayCommand AlternarSoloCeroCommand { get; }
    public RelayCommand AlternarAgrupacionCommand { get; }
    public RelayCommand AplicarPresetCommand { get; }
    public AsyncRelayCommand CargarVentasCommand { get; }
    public AsyncRelayCommand CargarDetalleCommand { get; }
    public AsyncRelayCommand SiguienteDetalleCommand { get; }
    public AsyncRelayCommand AnteriorDetalleCommand { get; }
    public AsyncRelayCommand CargarCajaCommand { get; }
    public AsyncRelayCommand SiguienteCajaCommand { get; }
    public AsyncRelayCommand AnteriorCajaCommand { get; }
    public AsyncRelayCommand CargarInventarioCommand { get; }
    public AsyncRelayCommand SiguienteRankingCommand { get; }
    public AsyncRelayCommand AnteriorRankingCommand { get; }
    public AsyncRelayCommand SiguienteSinVentasCommand { get; }
    public AsyncRelayCommand AnteriorSinVentasCommand { get; }
    public AsyncRelayCommand CargarCategoriasCommand { get; }

    public Task CargarVentasAsyncPublic() => CargarVentasAsync();
    public Task CargarCajaAsyncPublic() => CargarCajaAsync();
    public Task CargarInventarioAsyncPublic() => CargarInventarioAsync();
    public Task CargarCategoriasAsyncPublic() => CargarCategoriasAsync();

    /// <summary>Actualiza solo el reporte visible, sin alterar filtros ni pestaña activa.</summary>
    public async Task RefrescarPestanaActivaAsync()
    {
        switch (TabActiva)
        {
            case "Ventas" when PuedeVerVentas:
                await CargarVentasAsync();
                break;
            case "Caja" when PuedeVerCaja:
                await CargarCajaAsync();
                break;
            case "Inventario" when PuedeVerInventario:
                await CargarInventarioAsync();
                break;
        }
    }

    public static string FormatearMinor(long minor)
    {
        var dec = (decimal)minor / 100m;
        return $"ARS {dec.ToString("N2", new System.Globalization.CultureInfo("es-AR"))}";
    }

    private void AplicarPreset()
    {
        var hoy = DateTime.Today;
        _aplicandoPreset = true;
        try
        {
            if (Preset == "Hoy")
            {
                Desde = hoy;
                Hasta = hoy;
            }
            else if (Preset == "Este mes")
            {
                Desde = new DateTime(hoy.Year, hoy.Month, 1);
                Hasta = hoy;
            }
        }
        finally { _aplicandoPreset = false; }

        OnPropertyChanged(nameof(Desde));
        OnPropertyChanged(nameof(Hasta));
    }

    private void AplicarPresetDesdeBoton(string preset)
    {
        Preset = preset;
        AplicarPreset();
    }

    private void MarcarPeriodoPersonalizado()
    {
        if (Preset != "Personalizado") Preset = "Personalizado";
    }

    private void NotificarEstado()
    {
        OnPropertyChanged(nameof(PuedeImprimir));
        OnPropertyChanged(nameof(TextoVacioVentas));
        OnPropertyChanged(nameof(TextoVacioDetalle));
        OnPropertyChanged(nameof(TextoVacioCaja));
        OnPropertyChanged(nameof(TextoVacioStock));
        OnPropertyChanged(nameof(TextoVacioRanking));
        OnPropertyChanged(nameof(TextoVacioSinVentas));
        OnPropertyChanged(nameof(TextoConteoStock));
        OnPropertyChanged(nameof(TextoPaginacionDetalle));
        OnPropertyChanged(nameof(TextoPaginacionCaja));
        OnPropertyChanged(nameof(TextoPaginacionRanking));
        OnPropertyChanged(nameof(TextoPaginacionSinVentas));
    }

    private PeriodoReporte PeriodoActual()
        => RangoFechas.PorDias(DateOnly.FromDateTime(Desde.Date), DateOnly.FromDateTime(Hasta.Date));

    private PeriodoReporte PeriodoAplicado()
        => RangoFechas.PorDias(DateOnly.FromDateTime(_desdeAplicado), DateOnly.FromDateTime(_hastaAplicado));

    private string DesplazamientoActual()
    {
        var zona = TimeZoneInfo.Local;
        var medio = DateOnly.FromDateTime(Desde.Date.AddDays((Hasta.Date - Desde.Date).TotalDays / 2));
        var local = new DateTime(medio.Year, medio.Month, medio.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var horas = (int)zona.GetUtcOffset(local).TotalHours;
        return horas == 0 ? "+0 hours" : $"{horas} hours";
    }

    private async Task CargarVentasAsync()
    {
        if (Ocupado || !PuedeVerVentas) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            var periodo = PeriodoActual();
            Resumen = await _servicios.ConsultasReportes.ResumenVentasAsync(periodo.DesdeUtc, periodo.HastaUtc, _idSesion());
            OnPropertyChanged(nameof(AjustesVentasMinor));
            var diarias = await _servicios.ConsultasReportes.VentasPorDiaAsync(periodo.DesdeUtc, periodo.HastaUtc, DesplazamientoActual(), _idSesion());
            FilasDiarias.Clear();
            foreach (var f in diarias) FilasDiarias.Add(f);
            var medios = await _servicios.ConsultasReportes.DesgloseMediosAsync(periodo.DesdeUtc, periodo.HastaUtc, _idSesion());
            Medios.Clear();
            foreach (var m in medios) Medios.Add(m);
            _desplazamientoDetalle = 0;
            await CargarDetallePaginaAsync(periodo);
            _desdeAplicado = Desde.Date;
            _hastaAplicado = Hasta.Date;
            _presetAplicado = Preset;
            VentasConsultadas = true;
            NotificarEstado();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CargarDetalleAsync()
    {
        if (Ocupado || !PuedeVerVentas) return;
        _desplazamientoDetalle = 0;
        Error = null; Ocupado = true;
        try
        {
            await CargarDetallePaginaAsync(PeriodoAplicado());
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task MoverDetalleAsync(int delta)
    {
        if (Ocupado) return;
        if (delta > 0 && !HayMasDetalle) return;
        var nuevo = _desplazamientoDetalle + delta * TamanoPagina;
        if (nuevo < 0) return;
        _desplazamientoDetalle = nuevo;
        Error = null; Ocupado = true;
        try
        {
            await CargarDetallePaginaAsync(PeriodoAplicado());
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CargarDetallePaginaAsync(PeriodoReporte periodo)
    {
        var lista = await _servicios.ConsultasReportes.DetalleVentasAsync(periodo.DesdeUtc, periodo.HastaUtc, TamanoPagina, _desplazamientoDetalle, _idSesion());
        HayMasDetalle = lista.Count > TamanoPagina;
        Detalle.Clear();
        foreach (var d in lista.Take(TamanoPagina)) Detalle.Add(d);
        OnPropertyChanged(nameof(HayAnteriorDetalle));
        NotificarEstado();
    }

    private async Task CargarDetalleDiaAsync(string dia)
    {
        if (Ocupado || !PuedeVerVentas) return;
        if (!DateOnly.TryParseExact(dia, "yyyy-MM-dd", out var fecha)) return;
        Error = null; Ocupado = true;
        try
        {
            var periodo = RangoFechas.PorDias(fecha, fecha);
            _desplazamientoDetalle = 0;
            await CargarDetallePaginaAsync(periodo);
            Mensaje = $"Detalle del día {dia}.";
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CargarCajaAsync()
    {
        if (Ocupado || !PuedeVerCaja) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            var periodo = PeriodoActual();
            _desplazamientoCaja = 0;
            var lista = await _servicios.ConsultasReportes.HistorialCajaAsync(periodo.DesdeUtc, periodo.HastaUtc, TamanoPagina, _desplazamientoCaja, _idSesion());
            HayMasCaja = lista.Count > TamanoPagina;
            SesionesCaja.Clear();
            SetProperty(ref _sesionCajaSeleccionada, null, nameof(SesionCajaSeleccionada));
            DetalleCaja = null;
            foreach (var s in lista.Take(TamanoPagina)) SesionesCaja.Add(s);
            OnPropertyChanged(nameof(HayAnteriorCaja));
            _desdeAplicado = Desde.Date;
            _hastaAplicado = Hasta.Date;
            _presetAplicado = Preset;
            CajaConsultada = true;
            NotificarEstado();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task MoverCajaAsync(int delta)
    {
        if (Ocupado) return;
        if (delta > 0 && !HayMasCaja) return;
        var nuevo = _desplazamientoCaja + delta * TamanoPagina;
        if (nuevo < 0) return;
        _desplazamientoCaja = nuevo;
        Error = null; Ocupado = true;
        try
        {
            var periodo = PeriodoAplicado();
            var lista = await _servicios.ConsultasReportes.HistorialCajaAsync(periodo.DesdeUtc, periodo.HastaUtc, TamanoPagina, _desplazamientoCaja, _idSesion());
            HayMasCaja = lista.Count > TamanoPagina;
            SesionesCaja.Clear();
            foreach (var s in lista.Take(TamanoPagina)) SesionesCaja.Add(s);
            OnPropertyChanged(nameof(HayAnteriorCaja));
            NotificarEstado();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CargarDetalleCajaAsync(string sesionId)
    {
        Error = null;
        try
        {
            DetalleCaja = await _servicios.ConsultasCaja.ObtenerDetalleAsync(sesionId, _idSesion());
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    private async Task CargarCategoriasAsync()
    {
        try
        {
            var lista = await _servicios.ConsultasCatalogo.ListarCategoriasAsync(_idSesion(), true);
            Categorias.Clear();
            foreach (var c in lista) Categorias.Add(c);
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    private async Task CargarInventarioAsync()
    {
        if (Ocupado || !PuedeVerInventario) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            if (SubAreaInventario == "Stock")
            {
                long? hasta = long.TryParse(HastaUnidadesTexto.Trim(), out var n) && n >= 0 ? n : null;
                var lista = await _servicios.ConsultasReportes.StockActualAsync(TextoStock ?? string.Empty, CategoriaId, SoloCero, hasta, 100, 0, _idSesion());
                FilasStock.Clear();
                foreach (var f in lista) FilasStock.Add(f);
                _textoStockAplicado = TextoStock ?? string.Empty;
                _soloCeroAplicado = SoloCero;
                _hastaUnidadesAplicado = hasta;
                StockConsultado = true;
                NotificarEstado();
            }
            else if (SubAreaInventario == "Ranking")
            {
                _desplazamientoRanking = 0;
                _desdeAplicado = Desde.Date;
                _hastaAplicado = Hasta.Date;
                _presetAplicado = Preset;
                _agruparAplicado = AgruparPorProducto;
                await CargarRankingPaginaAsync();
                RankingConsultado = true;
                NotificarEstado();
            }
            else
            {
                _desplazamientoSinVentas = 0;
                _diasSinVentasAplicado = DiasSinVentas > 0 ? DiasSinVentas : 30;
                await CargarSinVentasPaginaAsync();
                SinVentasConsultado = true;
                NotificarEstado();
            }
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task MoverRankingAsync(int delta)
    {
        if (Ocupado) return;
        if (delta > 0 && !HayMasRanking) return;
        var nuevo = _desplazamientoRanking + delta * TamanoPagina;
        if (nuevo < 0) return;
        _desplazamientoRanking = nuevo;
        Error = null; Ocupado = true;
        try
        {
            await CargarRankingPaginaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CargarRankingPaginaAsync()
    {
        var periodo = PeriodoAplicado();
        var lista = AgruparPorProducto
            ? await _servicios.ConsultasReportes.RankingPorProductoAsync(periodo.DesdeUtc, periodo.HastaUtc, TamanoPagina, _desplazamientoRanking, _idSesion())
            : await _servicios.ConsultasReportes.RankingVentasAsync(periodo.DesdeUtc, periodo.HastaUtc, TamanoPagina, _desplazamientoRanking, _idSesion());
        HayMasRanking = lista.Count > TamanoPagina;
        Ranking.Clear();
        foreach (var r in lista.Take(TamanoPagina)) Ranking.Add(r);
        OnPropertyChanged(nameof(HayAnteriorRanking));
        NotificarEstado();
    }

    private async Task MoverSinVentasAsync(int delta)
    {
        if (Ocupado) return;
        if (delta > 0 && !HayMasSinVentas) return;
        var nuevo = _desplazamientoSinVentas + delta * TamanoPagina;
        if (nuevo < 0) return;
        _desplazamientoSinVentas = nuevo;
        Error = null; Ocupado = true;
        try
        {
            await CargarSinVentasPaginaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CargarSinVentasPaginaAsync()
    {
        var dias = _diasSinVentasAplicado > 0 ? _diasSinVentasAplicado : 30;
        var corte = DateTimeOffset.UtcNow.AddDays(-dias).ToString("o");
        var lista = await _servicios.ConsultasReportes.SinVentasAsync(corte, TamanoPagina, _desplazamientoSinVentas, _idSesion());
        HayMasSinVentas = lista.Count > TamanoPagina;
        SinVentasFilas.Clear();
        foreach (var s in lista.Take(TamanoPagina)) SinVentasFilas.Add(s);
        OnPropertyChanged(nameof(HayAnteriorSinVentas));
        NotificarEstado();
    }

    /// <summary>
    /// Prepara los datos del reporte visible para impresión. Solo consulta
    /// (nunca escribe) y usa el período y filtros aplicados por última vez, no
    /// los que el usuario esté editando. Devuelve null si todavía no hay un
    /// resultado válido para imprimir.
    /// </summary>
    public async Task<ReporteImpresion?> PrepararReporteActualAsync()
    {
        if (!PuedeImprimir) return null;
        Ocupado = true;
        Error = null;
        try
        {
            return TabActiva switch
            {
                "Ventas" => await ReporteVentasAsync(),
                "Caja" => await ReporteCajaAsync(),
                "Inventario" => await ReporteInventarioAsync(),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            Error = MensajeError(ex);
            return null;
        }
        finally { Ocupado = false; }
    }

    private async Task<ReporteImpresion?> ReporteVentasAsync()
    {
        if (Resumen is not { } resumen) return null;
        var periodo = PeriodoAplicado();
        var detalle = await TodasLasPaginasAsync((limite, desplazamiento) =>
            _servicios.ConsultasReportes.DetalleVentasAsync(periodo.DesdeUtc, periodo.HastaUtc, limite, desplazamiento, _idSesion()));

        return new ReporteImpresion
        {
            Titulo = "Reporte de ventas",
            Orientacion = OrientacionReporte.Horizontal,
            Filtros = FiltrosPeriodo(),
            Nota = "Ventas netas = brutas − anulaciones − crédito por devoluciones + salidas por cambio. Cobrado y reintegros corresponden a pagos registrados; flujo neto = cobrado − reintegros.",
            Resumen =
            [
                ("Ventas brutas", Ars(resumen.BrutasMinor)),
                ("Ajustes", ArsConSigno(resumen.NetasMinor - resumen.BrutasMinor)),
                ("Ventas netas", Ars(resumen.NetasMinor)),
                ("Crédito por devoluciones", Ars(resumen.CreditoDevolucionesMinor)),
                ("Salidas por cambio", Ars(resumen.SalidasCambioMinor)),
                ("Cobrado", Ars(resumen.CobradoMinor)),
                ("Reintegros", Ars(resumen.ReintegrosMinor)),
                ("Flujo neto", Ars(resumen.FlujoNetoMinor)),
            ],
            Tablas =
            [
                new TablaReporte("Ventas por día",
                    [
                        new("Fecha", 1.1),
                        new("Brutas", 1.2, true),
                        new("Ajustes", 1.1, true),
                        new("Netas", 1.2, true),
                        new("Cobros", 1.2, true),
                        new("Reintegros", 1.2, true),
                    ],
                    FilasDiarias.Select(f => (IReadOnlyList<string>)
                    [
                        FechaDia(f.Fecha), Ars(f.BrutasMinor), Ars(f.AjustesMinor),
                        Ars(f.NetasMinor), Ars(f.CobrosMinor), Ars(f.ReintegrosMinor),
                    ]).ToList()),
                new TablaReporte("Ventas por medio",
                    [
                        new("Medio", 1.3),
                        new("Ventas", 1.2, true),
                        new("Abonos", 1.1, true),
                        new("Cambios", 1.1, true),
                        new("Reintegros", 1.15, true),
                        new("Neto", 1.2, true),
                    ],
                    Medios.Select(m => (IReadOnlyList<string>)
                    [
                        EtiquetasCaja.Humano(m.Medio), Ars(m.VentasMinor), Ars(m.AbonosMinor),
                        Ars(m.CambiosCobrosMinor), Ars(m.ReintegrosMinor), Ars(m.NetoMinor),
                    ]).ToList()),
                new TablaReporte("Detalle de ventas",
                    [
                        new("Fecha", 1.25),
                        new("Referencia", 1.0),
                        new("Tipo", 0.8),
                        new("Cliente", 1.6),
                        new("Bruta", 1.0, true),
                        new("Ajustes", 1.0, true),
                        new("Neto", 1.0, true),
                        new("Estado", 0.9),
                    ],
                    detalle.Select(d => (IReadOnlyList<string>)
                    [
                        FechaHora(d.Fecha), d.Referencia, EtiquetasCaja.Humano(d.Tipo),
                        d.ClienteNombre ?? "—",
                        Ars(d.BrutaMinor), Ars(d.AjustesMinor), Ars(d.NetoMinor),
                        EtiquetasCaja.Humano(d.Estado),
                    ]).ToList()),
            ],
        };
    }

    private async Task<ReporteImpresion?> ReporteCajaAsync()
    {
        if (!CajaConsultada) return null;
        var periodo = PeriodoAplicado();
        var sesiones = await TodasLasPaginasAsync((limite, desplazamiento) =>
            _servicios.ConsultasReportes.HistorialCajaAsync(periodo.DesdeUtc, periodo.HastaUtc, limite, desplazamiento, _idSesion()));

        return new ReporteImpresion
        {
            Titulo = "Reporte de caja",
            Orientacion = OrientacionReporte.Horizontal,
            Filtros = FiltrosPeriodo(),
            Nota = "Importes del arqueo registrado. El detalle de movimientos de cada sesión se consulta en Caja.",
            Tablas =
            [
                new TablaReporte("Cajas del período",
                    [
                        new("Apertura", 1.25),
                        new("Cierre", 1.25),
                        new("Apertura por", 1.5),
                        new("Fondo", 1.0, true),
                        new("Teórico", 1.0, true),
                        new("Contado", 1.0, true),
                        new("Diferencia", 1.0, true),
                        new("Estado", 0.8),
                    ],
                    sesiones.Select(s => (IReadOnlyList<string>)
                    [
                        FechaHora(s.AbiertaEn),
                        s.CerradaEn is null ? "Abierta" : FechaHora(s.CerradaEn),
                        s.UsuarioAperturaNombre ?? "—",
                        Ars(s.FondoInicialMinor),
                        s.EfectivoEsperadoMinor is { } teorico ? Ars(teorico) : "—",
                        s.EfectivoContadoMinor is { } contado ? Ars(contado) : "—",
                        s.DiferenciaMinor is { } diferencia ? ArsConSigno(diferencia) : "—",
                        EtiquetasCaja.Humano(s.Estado),
                    ]).ToList()),
            ],
        };
    }

    private async Task<ReporteImpresion?> ReporteInventarioAsync()
    {
        if (SubAreaInventario == "Stock")
        {
            if (!StockConsultado) return null;
            var filas = await TodasLasPaginasAsync((limite, desplazamiento) =>
                _servicios.ConsultasReportes.StockActualAsync(_textoStockAplicado, CategoriaId, _soloCeroAplicado, _hastaUnidadesAplicado, limite, desplazamiento, _idSesion()));
            return new ReporteImpresion
            {
                Titulo = "Stock actual",
                Filtros = FiltrosStock(),
                Nota = "Disponible + Apartado = Físico. Existencias vivas al momento de emitir el reporte.",
                Tablas =
                [
                    new TablaReporte("Artículos",
                        [
                            new("Producto", 2.2),
                            new("Código", 1.1),
                            new("Talle/Color", 1.2),
                            new("Disponible", 0.9, true),
                            new("Apartado", 0.9, true),
                            new("Físico", 0.8, true),
                        ],
                        filas.Select(f => (IReadOnlyList<string>)
                        [
                            f.ProductoNombre, f.CodigoInterno, f.TalleColor,
                            f.Disponible.ToString(), f.Apartado.ToString(), f.Fisico.ToString(),
                        ]).ToList()),
                ],
            };
        }

        if (SubAreaInventario == "Ranking")
        {
            if (!RankingConsultado) return null;
            var periodo = PeriodoAplicado();
            var filas = await TodasLasPaginasAsync((limite, desplazamiento) => _agruparAplicado
                ? _servicios.ConsultasReportes.RankingPorProductoAsync(periodo.DesdeUtc, periodo.HastaUtc, limite, desplazamiento, _idSesion())
                : _servicios.ConsultasReportes.RankingVentasAsync(periodo.DesdeUtc, periodo.HastaUtc, limite, desplazamiento, _idSesion()));
            return new ReporteImpresion
            {
                Titulo = _agruparAplicado ? "Artículos con más movimiento (por producto)" : "Artículos con más movimiento",
                Filtros = FiltrosPeriodo(),
                Nota = "Unidades netas = brutas − anuladas − devueltas + salidas por cambio. Las anulaciones se cuentan en la fecha en que se registran.",
                Tablas =
                [
                    new TablaReporte("Ranking",
                        [
                            new("Producto", 1.8),
                            new("Código", 1.0),
                            new("Talle/Color", 0.9),
                            new("Brutas", 0.8, true),
                            new("Anuladas", 0.9, true),
                            new("Devueltas", 0.9, true),
                            new("Salidas", 0.8, true),
                            new("Netas", 0.8, true),
                        ],
                        filas.Select(f => (IReadOnlyList<string>)
                        [
                            f.NombreProducto, f.CodigoInterno, f.TalleColor,
                            f.BrutasUnidades.ToString(), f.AnuladasUnidades.ToString(),
                            f.DevueltasUnidades.ToString(), f.SalidasCambioUnidades.ToString(),
                            f.NetasUnidades.ToString(),
                        ]).ToList()),
                ],
            };
        }

        if (!SinVentasConsultado) return null;
        var dias = _diasSinVentasAplicado > 0 ? _diasSinVentasAplicado : 30;
        var corte = DateTimeOffset.UtcNow.AddDays(-dias).ToString("o");
        var sinVentas = await TodasLasPaginasAsync((limite, desplazamiento) =>
            _servicios.ConsultasReportes.SinVentasAsync(corte, limite, desplazamiento, _idSesion()));
        return new ReporteImpresion
        {
            Titulo = "Artículos sin ventas",
            Filtros = $"Últimos {dias} días · Corte al {DateTime.Today:dd/MM/yyyy}",
            Nota = "Se listan artículos sin ventas registradas dentro del período indicado.",
            Tablas =
            [
                new TablaReporte("Artículos",
                    [
                        new("Producto", 2.0),
                        new("Código", 1.1),
                        new("Talle/Color", 1.1),
                        new("Última venta", 1.2),
                        new("Última recepción", 1.2),
                    ],
                    sinVentas.Select(f => (IReadOnlyList<string>)
                    [
                        f.ProductoNombre, f.CodigoInterno, f.TalleColor,
                        f.NuncaVendido || f.UltimaVentaEn is null ? "Nunca" : FechaHora(f.UltimaVentaEn),
                        f.UltimaRecepcionEn is null ? "—" : FechaHora(f.UltimaRecepcionEn),
                    ]).ToList()),
            ],
        };
    }

    private static Task<List<T>> TodasLasPaginasAsync<T>(Func<int, int, Task<IReadOnlyList<T>>> pagina)
        => PaginacionReporte.TodasLasPaginasAsync(pagina);

    private string FiltrosPeriodo()
        => $"Período: {_desdeAplicado:dd/MM/yyyy} al {_hastaAplicado:dd/MM/yyyy} · {_presetAplicado}";

    private string FiltrosStock()
    {
        var partes = new List<string>
        {
            string.IsNullOrWhiteSpace(_textoStockAplicado) ? "Todos los artículos" : $"Búsqueda: «{_textoStockAplicado}»",
        };
        if (_soloCeroAplicado) partes.Add("Solo stock 0");
        if (_hastaUnidadesAplicado is { } hasta) partes.Add($"Hasta {hasta} unidades");
        return string.Join(" · ", partes);
    }

    private static string Ars(long minor) => FormatearMinor(minor);

    private static string ArsConSigno(long minor)
        => minor == 0 ? FormatearMinor(0) : (minor > 0 ? "+" : "-") + FormatearMinor(Math.Abs(minor));

    private static string FechaDia(string valor)
        => DateOnly.TryParseExact(valor, "yyyy-MM-dd", out var dia) ? dia.ToString("dd/MM/yyyy") : valor;

    private static string FechaHora(string? valor)
        => DateTimeOffset.TryParse(valor, out var fecha) ? fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "—";

    private static string MensajeError(Exception ex) => ex switch
    {
        LocalInfantil.Application.Abstractions.AccesoDenegadoException
            => "No tenés permiso para ver reportes. La Dueña puede habilitarlo desde Administración → Permisos.",
        LocalInfantil.Application.Abstractions.SesionInvalidaException
            => "Sesión inválida. Iniciá sesión nuevamente.",
        _ => ex.Message,
    };
}
