using System.Windows;

namespace LocalInfantil.Wpf;

public partial class DevMainWindow : Window
{
    public DevMainWindow() => InitializeComponent();

    private async void Cerrar_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.AppRootViewModel vm)
            await vm.CerrarSesionAsync();
    }
}
