using System.Windows.Controls;

namespace LocalInfantil.Wpf.Views;

public partial class ShellEmpleadaView : UserControl
{
    public ShellEmpleadaView() => InitializeComponent();

    private async void Cerrar_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ShellEmpleadaViewModel vm)
            await vm.CerrarSesion();
    }
}
