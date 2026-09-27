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

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
