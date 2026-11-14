using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class FilaMatrizVarianteViewModel : ViewModelBase
{
    private bool _seleccionada = true;
    private string? _codigo;
    private bool _yaExiste;

    public FilaMatrizVarianteViewModel(string? talle, string? color)
        => (Talle, Color) = (talle, color);

    public string? Talle { get; }
    public string? Color { get; }
    public string TalleTexto => Talle ?? "Sin talle";
    public string ColorTexto => Color ?? "Sin color";
    public string Detalle => Talle is null && Color is null ? "Sin atributos" : string.Join(" · ", new[] { Talle, Color }.Where(x => x is not null));
    public bool Seleccionada { get => _seleccionada; set => SetProperty(ref _seleccionada, value); }
    public string? Codigo { get => _codigo; set => SetProperty(ref _codigo, value); }
    public bool YaExiste { get => _yaExiste; set { if (SetProperty(ref _yaExiste, value)) OnPropertyChanged(nameof(Estado)); } }
    public string Estado => YaExiste ? "Ya existe" : "Nueva";

    public CombinacionVariante AContrato() => new(Talle, Color);
}
