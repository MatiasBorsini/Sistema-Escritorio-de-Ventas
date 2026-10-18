using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class CambiosView : UserControl
{
    private bool _restaurandoSeleccion;
    public CambiosView()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() => BuscarVentaTextBox.Focus()), DispatcherPriority.Input);
    }

    private void VentasListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ListBox { SelectedItem: FilaVentaCambio venta }) return;
        if (DataContext is CambiosViewModel vm) vm.Venta = venta;
        if (DataContext is CambiosViewModel { EsModoCambio: true }) CodigoSalidaTextBox.Focus();
        else EntradasDataGrid.Focus();
        e.Handled = true;
    }

    private async void BuscarVentas_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CambiosViewModel vm) return;
        if (!ConfirmarPerdidaBorrador(vm)) return;
        await vm.BuscarAsyncPublic();
    }

    private void BuscarVentaTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        BuscarVentas_Click(sender, e);
        e.Handled = true;
    }

    private void CodigoSalidaTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not CambiosViewModel vm) return;
        vm.AgregarSalidaCommand.Execute(null);
        e.Handled = true;
    }

    private void VentasListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restaurandoSeleccion || DataContext is not CambiosViewModel vm || sender is not ListBox lista
            || lista.SelectedItem is not FilaVentaCambio nueva) return;
        if (!ReferenceEquals(vm.Venta, nueva))
        {
            if (!ConfirmarPerdidaBorrador(vm))
            {
                _restaurandoSeleccion = true;
                lista.SelectedItem = vm.Venta;
                _restaurandoSeleccion = false;
                return;
            }
            vm.Venta = nueva;
        }
        ContrasenaDuena.Clear();
        vm.ContrasenaAutorizanteIngresada = false;
    }

    private bool ConfirmarPerdidaBorrador(CambiosViewModel vm)
        => !vm.TieneBorradorOperacion || MessageBox.Show(Window.GetWindow(this),
            "Hay datos cargados en la operación actual. Si continuás, se perderán. ¿Querés seguir?",
            "Descartar borrador", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ContrasenaDuena_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is CambiosViewModel vm) vm.ContrasenaAutorizanteIngresada = ContrasenaDuena.Password.Length > 0;
    }

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

    private async void HistorialListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: LocalInfantil.Application.Contracts.ResumenCambio resumen } || DataContext is not CambiosViewModel vm) return;
        var documento = await vm.ObtenerTicketAsync(resumen.Id);
        if (documento is not null) new TicketCambioWindow(documento) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
