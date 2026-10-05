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
        var passwordInicial = vm.RequiereAutorizacionPresencial ? ContrasenaDuena.Password : null;
        var resultado = await vm.ConfirmarAsync(passwordInicial);
        if (resultado is LocalInfantil.Application.Contracts.ResultadoCambio.DecisionCuentaRequerida deuda)
        {
            var decision = MessageBox.Show(Window.GetWindow(this),
                $"La venta original todavía tiene una deuda pendiente de {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(deuda.DeudaVentaMinor)}.\nEl crédito de la devolución es {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(deuda.CreditoDevolucionMinor)}.\n\n¿Aplicar el crédito a la deuda? Elegí No para reintegrarlo.",
                "Decisión de cuenta corriente", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (decision == MessageBoxResult.Cancel) return;
            var aplicar = decision == MessageBoxResult.Yes;
            var decisionCuenta = aplicar ? LocalInfantil.Application.Contracts.DecisionCuentaCorrienteCambio.AplicarADeuda : LocalInfantil.Application.Contracts.DecisionCuentaCorrienteCambio.Reintegrar;
            resultado = await vm.ConfirmarAsync(decisionCuenta: decisionCuenta, deudaEsperadaMinor: deuda.DeudaVentaMinor);
        }
        if (resultado is LocalInfantil.Application.Contracts.ResultadoCambio.ConfirmacionCuenta preview)
        {
            var texto = $"Deuda de la venta antes: {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(preview.DeudaAnteriorMinor)}\nCrédito de la devolución: {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(preview.CreditoMinor)}\nAplicar a deuda: {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(preview.AplicarMinor)}\nDeuda posterior: {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(preview.DeudaPosteriorMinor)}\nReintegro en caja: {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(preview.ReintegrarMinor)}";
            if (MessageBox.Show(Window.GetWindow(this), texto + "\n\n¿Confirmar esta decisión?", "Confirmar devolución", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var passwordFinal = preview.ReintegrarMinor > 0 && vm.RequiereAutorizacionPresencial ? ContrasenaDuena.Password : null;
            ContrasenaDuena.Clear();
            resultado = await vm.ConfirmarAsync(passwordFinal, preview.Decision, true, preview.DeudaAnteriorMinor);
        }
        else
        {
            ContrasenaDuena.Clear();
        }
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
