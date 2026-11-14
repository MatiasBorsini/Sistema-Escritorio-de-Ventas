using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LocalInfantil.Wpf.Views;

public partial class ProveedoresView : UserControl
{
    public ProveedoresView() => InitializeComponent();

    private void AbrirAlta_Click(object sender, RoutedEventArgs e)
    {
        AltaPanel.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(NuevoNombreInput.Focus, DispatcherPriority.Input);
    }

    private void CerrarAlta_Click(object sender, RoutedEventArgs e)
        => AltaPanel.Visibility = Visibility.Collapsed;
}
