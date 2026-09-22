using System.Windows.Controls;

namespace LocalInfantil.Wpf.Views;

public partial class ShellDuenaView : UserControl
{
    public ShellDuenaView() => InitializeComponent();

    private async void Cerrar_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ShellDuenaViewModel vm)
            await vm.CerrarSesion();
    }
}
