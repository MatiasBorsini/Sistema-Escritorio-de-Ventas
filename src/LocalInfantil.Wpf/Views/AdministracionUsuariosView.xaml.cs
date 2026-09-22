using System.Windows;
using System.Windows.Controls;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class AdministracionUsuariosView : UserControl
{
    public AdministracionUsuariosView()
    {
        InitializeComponent();
    }

    private void NuevaPwd_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdministracionUsuariosViewModel vm && sender is PasswordBox pb)
            vm.NuevaContrasena = pb.Password;
    }

    private async void Conceder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is PermisoFilaViewModel fila && DataContext is AdministracionUsuariosViewModel vm)
            await vm.AlternarPermisoAsync(fila, true);
    }

    private async void Denegar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is PermisoFilaViewModel fila && DataContext is AdministracionUsuariosViewModel vm)
            await vm.AlternarPermisoAsync(fila, false);
    }
}
