using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class MatrizVariantesViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private readonly string _productoId;
    private string _talles = string.Empty;
    private string _colores = string.Empty;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private int _cantidadCreadas;

    public MatrizVariantesViewModel(ServiciosComerciales servicios, Func<string?> idSesion, string productoId)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        _productoId = productoId;
        Filas = new ObservableCollection<FilaMatrizVarianteViewModel>();
        GenerarMatrizCommand = new AsyncRelayCommand(GenerarMatrizAsync);
        PrevisualizarCommand = new AsyncRelayCommand(PrevisualizarAsync);
        CrearCommand = new AsyncRelayCommand(CrearAsync);
        CerrarCommand = new RelayCommand(_ => SolicitarCierre?.Invoke());
    }

    public ObservableCollection<FilaMatrizVarianteViewModel> Filas { get; }
    public string Talles { get => _talles; set => SetProperty(ref _talles, value); }
    public string Colores { get => _colores; set => SetProperty(ref _colores, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; private set => SetProperty(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; private set => SetProperty(ref _ocupado, value); }
    public int CantidadSeleccionada => Filas.Count(f => f.Seleccionada);
    public int CantidadCreadas { get => _cantidadCreadas; private set => SetProperty(ref _cantidadCreadas, value); }
    public event Action? SolicitarCierre;
    public AsyncRelayCommand GenerarMatrizCommand { get; }
    public AsyncRelayCommand PrevisualizarCommand { get; }
    public AsyncRelayCommand CrearCommand { get; }
    public RelayCommand CerrarCommand { get; }

    private Task GenerarMatrizAsync()
    {
        Error = Mensaje = null;
        Filas.Clear();
        var talles = Separar(Talles).Cast<string?>().ToList();
        var colores = Separar(Colores).Cast<string?>().ToList();
        if (talles.Count == 0 && colores.Count == 0)
            Filas.Add(new FilaMatrizVarianteViewModel(null, null));
        else
            foreach (var talle in talles.Count == 0 ? [null] : talles)
                foreach (var color in colores.Count == 0 ? [null] : colores)
                    Filas.Add(new FilaMatrizVarianteViewModel(talle, color));
        OnPropertyChanged(nameof(CantidadSeleccionada));
        Mensaje = $"{Filas.Count} combinación(es) en la matriz.";
        return Task.CompletedTask;
    }

    private async Task PrevisualizarAsync()
    {
        Error = Mensaje = null;
        var seleccionadas = Filas.Where(f => f.Seleccionada).ToArray();
        try
        {
            var propuestas = await _servicios.ConsultasMatrizVariantes.PrevisualizarAsync(_productoId, seleccionadas.Select(f => f.AContrato()).ToArray(), _idSesion());
            for (var i = 0; i < seleccionadas.Length; i++)
            {
                seleccionadas[i].Codigo = propuestas[i].CodigoInterno;
                seleccionadas[i].YaExiste = propuestas[i].YaExiste;
            }
            Mensaje = $"{propuestas.Count(p => !p.YaExiste)} variantes nuevas; {propuestas.Count(p => p.YaExiste)} ya existen.";
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    private async Task CrearAsync()
    {
        Error = Mensaje = null;
        Ocupado = true;
        try
        {
            var resultado = await _servicios.CasoCrearVariantesBatch.EjecutarAsync(_productoId, Filas.Where(f => f.Seleccionada).Select(f => f.AContrato()).ToArray(), _idSesion());
            CantidadCreadas = resultado.CantidadCreadas;
            Mensaje = CantidadCreadas == 0 ? "No había combinaciones nuevas para crear." : $"Se crearon {CantidadCreadas} variantes.";
            if (CantidadCreadas > 0) SolicitarCierre?.Invoke();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private static List<string> Separar(string texto)
        => texto.Split([',', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
}
