using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Reportes;
using LocalInfantil.Wpf.Infra;

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
            }
        }
    }

    public bool TabVentasVisible => TabActiva == "Ventas" && PuedeVerVentas;
    public bool TabCajaVisible => TabActiva == "Caja" && PuedeVerCaja;
    public bool TabInventarioVisible => TabActiva == "Inventario" && PuedeVerInventario;

    public string Preset { get => _preset; set => SetProperty(ref _preset, value); }
    public DateTime Desde { get => _desde; set => SetProperty(ref _desde, value); }
    public DateTime Hasta { get => _hasta; set => SetProperty(ref _hasta, value); }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; set => SetProperty(ref _ocupado, value); }

    public ResumenVentasPeriodo? Resumen { get => _resumen; private set => SetProperty(ref _resumen, value); }

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

        OnPropertyChanged(nameof(Desde));
        OnPropertyChanged(nameof(Hasta));
    }

    private PeriodoReporte PeriodoActual()
        => RangoFechas.PorDias(DateOnly.FromDateTime(Desde.Date), DateOnly.FromDateTime(Hasta.Date));

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
            await CargarDetallePaginaAsync(PeriodoActual());
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
            await CargarDetallePaginaAsync(PeriodoActual());
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
            var periodo = PeriodoActual();
            var lista = await _servicios.ConsultasReportes.HistorialCajaAsync(periodo.DesdeUtc, periodo.HastaUtc, TamanoPagina, _desplazamientoCaja, _idSesion());
            HayMasCaja = lista.Count > TamanoPagina;
            SesionesCaja.Clear();
            foreach (var s in lista.Take(TamanoPagina)) SesionesCaja.Add(s);
            OnPropertyChanged(nameof(HayAnteriorCaja));
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
                if (lista.Count == 0) Mensaje = "Sin resultados para el filtro.";
            }
            else if (SubAreaInventario == "Ranking")
            {
                _desplazamientoRanking = 0;
                await CargarRankingPaginaAsync();
            }
            else
            {
                _desplazamientoSinVentas = 0;
                await CargarSinVentasPaginaAsync();
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
        var periodo = PeriodoActual();
        var lista = AgruparPorProducto
            ? await _servicios.ConsultasReportes.RankingPorProductoAsync(periodo.DesdeUtc, periodo.HastaUtc, TamanoPagina, _desplazamientoRanking, _idSesion())
            : await _servicios.ConsultasReportes.RankingVentasAsync(periodo.DesdeUtc, periodo.HastaUtc, TamanoPagina, _desplazamientoRanking, _idSesion());
        HayMasRanking = lista.Count > TamanoPagina;
        Ranking.Clear();
        foreach (var r in lista.Take(TamanoPagina)) Ranking.Add(r);
        OnPropertyChanged(nameof(HayAnteriorRanking));
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
        var dias = DiasSinVentas > 0 ? DiasSinVentas : 30;
        var corte = DateTimeOffset.UtcNow.AddDays(-dias).ToString("o");
        var lista = await _servicios.ConsultasReportes.SinVentasAsync(corte, TamanoPagina, _desplazamientoSinVentas, _idSesion());
        HayMasSinVentas = lista.Count > TamanoPagina;
        SinVentasFilas.Clear();
        foreach (var s in lista.Take(TamanoPagina)) SinVentasFilas.Add(s);
        OnPropertyChanged(nameof(HayAnteriorSinVentas));
    }

    private static string MensajeError(Exception ex) => ex switch
    {
        LocalInfantil.Application.Abstractions.AccesoDenegadoException
            => "No tenés permiso para ver reportes. La Dueña puede habilitarlo desde Administración → Permisos.",
        LocalInfantil.Application.Abstractions.SesionInvalidaException
            => "Sesión inválida. Iniciá sesión nuevamente.",
        _ => ex.Message,
    };
}
