using System.Windows;
using System.Windows.Input;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

/// <summary>
/// Detalle de recepción: consulta modeless de solo lectura. No bloquea
/// Recepciones; puede permanecer abierta mientras se consultan otras.
/// </summary>
public partial class DetalleRecepcionWindow : Window
{
    public DetalleRecepcionWindow(DetalleRecepcionViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.CargarAsync();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
