using System.Windows.Controls;
using System.Windows;
using LocalInfantil.Wpf.ViewModels;
namespace LocalInfantil.Wpf.Views;
public partial class RecepcionView : UserControl
{
    public RecepcionView() => InitializeComponent();
    private void ImprimirEtiquetas_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecepcionViewModel vm) return;
        var ventana = new EtiquetasWindow(vm.CrearEtiquetasRecepcionViewModel()) { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }
}
