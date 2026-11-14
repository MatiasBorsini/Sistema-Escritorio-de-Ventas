using System.Windows;
using System.Windows.Documents;
using LocalInfantil.Wpf.Printing;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class EtiquetasWindow : Window
{
    private readonly EtiquetasViewModel _vm;
    private readonly DocumentoEtiquetasRenderer _renderer;
    private readonly ServicioImpresionWpf _impresion = new();

    public EtiquetasWindow(EtiquetasViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _renderer = new DocumentoEtiquetasRenderer(vm.Generador);
        DataContext = vm;
        Loaded += async (_, _) => await vm.InicializarAsync();
    }

    private async void VistaPrevia_Click(object sender, RoutedEventArgs e) => await MostrarVistaPreviaAsync();
    private async void Imprimir_Click(object sender, RoutedEventArgs e) => await MostrarVistaPreviaAsync();

    private async Task MostrarVistaPreviaAsync()
    {
        await _vm.RefrescarAntesDePrevisualizarAsync();
        try
        {
            if (_vm.Error is not null) throw new InvalidOperationException(_vm.Error);
            var documento = _renderer.Renderizar(_vm.Seleccionadas, _vm.Opciones);
            async Task<FixedDocument> RefrescarDocumentoAsync()
            {
                await _vm.RefrescarAntesDePrevisualizarAsync();
                if (_vm.Error is not null) throw new InvalidOperationException(_vm.Error);
                return _renderer.Renderizar(_vm.Seleccionadas, _vm.Opciones);
            }
            var preview = new VistaPreviaEtiquetasWindow(documento, _impresion, RefrescarDocumentoAsync) { Owner = this };
            preview.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Vista previa de etiquetas", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
