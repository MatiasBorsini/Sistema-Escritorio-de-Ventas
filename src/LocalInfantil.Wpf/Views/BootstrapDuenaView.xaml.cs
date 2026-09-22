using System.Windows.Controls;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class BootstrapDuenaView : UserControl
{
    public BootstrapDuenaView()
    {
        InitializeComponent();
    }

    private void Pwd1_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is BootstrapDuenaViewModel vm && sender is PasswordBox pb) vm.Contrasena = pb.Password;
    }

    private void Pwd2_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is BootstrapDuenaViewModel vm && sender is PasswordBox pb) vm.Confirmacion = pb.Password;
    }
}
