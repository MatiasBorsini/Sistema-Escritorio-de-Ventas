using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class FilaEtiquetaViewModel : ViewModelBase
{
    private int _cantidad;
    public FilaEtiquetaViewModel(VarianteParaEtiqueta variante, int cantidad = 0) => (Variante, _cantidad) = (variante, cantidad);
    public VarianteParaEtiqueta Variante { get; private set; }
    public int Cantidad { get => _cantidad; set => SetProperty(ref _cantidad, value < 0 ? 0 : value); }
    public string VarianteTexto => (Variante.Talle, Variante.Color) switch
    {
        (null, null) => "—", (null, var c) => c!, (var t, null) => t!, (var t, var c) => $"{t} · {c}",
    };
    public void Actualizar(VarianteParaEtiqueta variante) { Variante = variante; OnPropertyChanged(nameof(Variante)); OnPropertyChanged(nameof(VarianteTexto)); }
    public SeleccionEtiqueta AContrato() => new(Variante, Cantidad);
}
