using System.Windows;
using System.Windows.Input;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

/// <summary>
/// Ficha de producto en ventana secundaria (creación o edición). Modal/owned:
/// Owner = principal, sin taskbar, CenterOwner. Code-behind solo para ciclo de
/// vida de ventana (cargar, cerrar, Escape).
/// </summary>
public partial class FichaProductoWindow : Window
{
    public FichaProductoWindow(FichaProductoViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InicializarAsync();
        vm.SolicitarCierre += () => Close();
        Closing += (_, e) =>
        {
            if (DataContext is FichaProductoViewModel actual && !actual.AlIntentarCerrar())
                e.Cancel = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private async void CrearPorMatriz_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FichaProductoViewModel vm || vm.ProductoId is null) return;
        var matriz = vm.CrearMatrizVariantesViewModel();
        var ventana = new MatrizVariantesWindow(matriz) { Owner = this };
        ventana.ShowDialog();
        if (matriz.CantidadCreadas > 0) await vm.RecargarFichaAsyncPublic();
    }

    private void ImprimirEtiquetas_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FichaProductoViewModel vm || vm.ProductoId is null) return;
        var ventana = new EtiquetasWindow(vm.CrearEtiquetasViewModel()) { Owner = this };
        ventana.ShowDialog();
    }

    private void ImprimirEtiquetasCarga_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FichaProductoViewModel vm) return;
        var ventana = new EtiquetasWindow(vm.CrearEtiquetasCargaInicialViewModel()) { Owner = this };
        ventana.ShowDialog();
    }
}
