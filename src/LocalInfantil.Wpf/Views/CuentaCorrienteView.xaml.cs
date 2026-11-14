using System.Windows;
using System.Windows.Controls;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class CuentaCorrienteView : UserControl
{
    public CuentaCorrienteView() => InitializeComponent();

    private async void AbrirUltimoAbono_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CuentaCorrienteViewModel vm || vm.UltimoAbonoId is not { } id) return;
        try
        {
            var recibo = await vm.ObtenerTicketAbonoAsync(id);
            if (recibo is not null) new TicketAbonoWindow(recibo) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Recibo de abono", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
