using System.Collections.ObjectModel;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.Printing;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class EtiquetasViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private readonly string? _productoId;
    private readonly IReadOnlyList<string>? _varianteIds;
    private readonly IReadOnlyDictionary<string, int> _cantidadesIniciales;
    private string? _error;
    private string? _nombreProducto;
    private bool _mostrarPrecio;
    private double _anchoMm = 50, _altoMm = 30, _margenHorizontalMm = 4, _margenVerticalMm = 4, _separacionHorizontalMm = 2, _separacionVerticalMm = 2;

    public EtiquetasViewModel(ServiciosComerciales servicios, Func<string?> idSesion, string productoId, IReadOnlyDictionary<string, int>? cantidadesIniciales = null)
    {
        _servicios = servicios; _idSesion = idSesion; _productoId = productoId;
        _cantidadesIniciales = cantidadesIniciales ?? new Dictionary<string, int>();
        Filas = new ObservableCollection<FilaEtiquetaViewModel>();
        PonerStockDisponibleCommand = new AsyncRelayCommand(PonerStockDisponibleAsync);
        LimpiarCantidadesCommand = new AsyncRelayCommand(LimpiarAsync);
    }

    public EtiquetasViewModel(ServiciosComerciales servicios, Func<string?> idSesion, IReadOnlyDictionary<string, int> cantidadesPorVarianteId)
        : this(servicios, idSesion, string.Empty)
    {
        _productoId = null;
        _varianteIds = cantidadesPorVarianteId.Keys.ToArray();
        _cantidadesIniciales = cantidadesPorVarianteId;
    }

    public ObservableCollection<FilaEtiquetaViewModel> Filas { get; }
    public string? NombreProducto { get => _nombreProducto; private set => SetProperty(ref _nombreProducto, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public bool MostrarPrecio { get => _mostrarPrecio; set => SetProperty(ref _mostrarPrecio, value); }
    public double AnchoMm { get => _anchoMm; set => SetProperty(ref _anchoMm, value); }
    public double AltoMm { get => _altoMm; set => SetProperty(ref _altoMm, value); }
    public double MargenHorizontalMm { get => _margenHorizontalMm; set => SetProperty(ref _margenHorizontalMm, value); }
    public double MargenVerticalMm { get => _margenVerticalMm; set => SetProperty(ref _margenVerticalMm, value); }
    public double SeparacionHorizontalMm { get => _separacionHorizontalMm; set => SetProperty(ref _separacionHorizontalMm, value); }
    public double SeparacionVerticalMm { get => _separacionVerticalMm; set => SetProperty(ref _separacionVerticalMm, value); }
    public AsyncRelayCommand PonerStockDisponibleCommand { get; }
    public AsyncRelayCommand LimpiarCantidadesCommand { get; }
    public IReadOnlyList<SeleccionEtiqueta> Seleccionadas => Filas.Where(f => f.Cantidad > 0).Select(f => f.AContrato()).ToArray();
    public EtiquetasOpciones Opciones => new(AnchoMm, AltoMm, MargenHorizontalMm, MargenVerticalMm, SeparacionHorizontalMm, SeparacionVerticalMm, MostrarPrecio);
    public IGeneradorCodigoBarras Generador => _servicios.GeneradorCodigoBarras;

    public async Task InicializarAsync()
    {
        try { await RecargarDatosAsync(); } catch (Exception ex) { Error = ex.Message; }
    }

    public async Task RefrescarAntesDePrevisualizarAsync()
    {
        Error = null;
        try { await RecargarDatosAsync(true); } catch (Exception ex) { Error = ex.Message; }
    }

    private async Task RecargarDatosAsync(bool preservarCantidades = false)
    {
        var previas = preservarCantidades ? Filas.ToDictionary(f => f.Variante.VarianteId, f => f.Cantidad) : null;
        var datos = _varianteIds is null
            ? await _servicios.ConsultasEtiquetas.ListarPorProductoAsync(_productoId!, _idSesion())
            : await _servicios.ConsultasEtiquetas.ListarPorIdsAsync(_varianteIds, _idSesion());
        Filas.Clear();
        foreach (var variante in datos)
            Filas.Add(new FilaEtiquetaViewModel(variante, previas?.GetValueOrDefault(variante.VarianteId) ?? _cantidadesIniciales.GetValueOrDefault(variante.VarianteId, _cantidadesIniciales.GetValueOrDefault(variante.CodigoInterno))));
        NombreProducto = datos.Select(d => d.NombreProducto).Distinct().Count() <= 1
            ? datos.FirstOrDefault()?.NombreProducto ?? (_productoId is null ? "Variantes recibidas" : (await _servicios.ConsultasCatalogo.ObtenerProductoAsync(_productoId, _idSesion()))?.Nombre)
            : "Variantes de la recepción";
        OnPropertyChanged(nameof(Seleccionadas));
    }

    private Task PonerStockDisponibleAsync()
    {
        Error = null;
        foreach (var fila in Filas) fila.Cantidad = (int)Math.Clamp(fila.Variante.Disponible, 0, int.MaxValue);
        OnPropertyChanged(nameof(Seleccionadas));
        return Task.CompletedTask;
    }

    private Task LimpiarAsync()
    {
        foreach (var fila in Filas) fila.Cantidad = 0;
        OnPropertyChanged(nameof(Seleccionadas));
        return Task.CompletedTask;
    }
}
