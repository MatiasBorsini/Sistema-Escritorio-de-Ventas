using System.Windows.Controls;
using System.Windows;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class CambiosView : UserControl
{
    public CambiosView() => InitializeComponent();

    private async void Confirmar_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not CambiosViewModel vm) return;
        var cambioPrevio = vm.CambioIdReciente;
        var password = vm.RequiereAutorizacionPresencial ? ContrasenaDuena.Password : null;
        ContrasenaDuena.Clear();
        await vm.ConfirmarAsync(password);
        if (!string.IsNullOrWhiteSpace(vm.CambioIdReciente) && vm.CambioIdReciente != cambioPrevio)
        {
            var documento = await vm.ObtenerTicketAsync(vm.CambioIdReciente);
            if (documento is not null) new TicketCambioWindow(documento) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
    }

    private async void HistorialItem_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not System.Windows.FrameworkElement { DataContext: LocalInfantil.Application.Contracts.ResumenCambio resumen } || DataContext is not CambiosViewModel vm) return;
        var documento = await vm.ObtenerTicketAsync(resumen.Id);
        if (documento is not null) new TicketCambioWindow(documento) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
