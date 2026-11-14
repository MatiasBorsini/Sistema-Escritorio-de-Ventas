using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class CajaView : UserControl
{
    public CajaView() => InitializeComponent();

    private async void Corregir_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not CajaViewModel vm || vm.PagoSeleccionado is null) return;
        var pago = vm.PagoSeleccionado;
        var importe = LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(pago.ImporteMinor);
        var detalle = $"Venta {pago.NumeroVenta}\nImporte que no cambia: {importe}\nForma anterior: {MovimientoCajaViewModel.TextoMedio(pago.MedioActual)}\nForma nueva: {MovimientoCajaViewModel.TextoMedio(vm.MedioNuevo)}\n\n{vm.SesionDestinoTexto}\n{vm.ImpactoEfectivoTexto}\n\n¿Confirmás la corrección?";
        if (MessageBox.Show(Window.GetWindow(this), detalle, "Confirmar forma de cobro", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await vm.CorregirMedioAsyncPublic();
    }

    private void BusquedaVentaCaja_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is CajaViewModel vm) vm.AbrirSugerenciasVentas();
    }

    private void BusquedaVentaCaja_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CajaViewModel vm) return;
        if (e.Key == Key.Down && vm.VentasSugeridas.Count > 0)
        {
            SugerenciasVentasCajaListBox.SelectedIndex = Math.Min(SugerenciasVentasCajaListBox.SelectedIndex + 1, vm.VentasSugeridas.Count - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && vm.VentasSugeridas.Count > 0)
        {
            SugerenciasVentasCajaListBox.SelectedIndex = Math.Max(0, SugerenciasVentasCajaListBox.SelectedIndex - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && SugerenciasVentasCajaListBox.SelectedItem is VentaCorreccionSugeridaItemViewModel item)
        {
            vm.SeleccionarVentaSugeridaCommand.Execute(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CerrarSugerenciasVentas();
            e.Handled = true;
        }
    }

    private void SugerenciasVentasCaja_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox lista || DataContext is not CajaViewModel vm) return;
        var source = e.OriginalSource as DependencyObject;
        if (source is null || ItemsControl.ContainerFromElement(lista, source) is not ListBoxItem fila
            || fila.DataContext is not VentaCorreccionSugeridaItemViewModel item) return;

        // La selección visual de WPF sucede en MouseDown. Confirmamos en MouseUp
        // sobre el elemento realmente clickeado para no depender del foco del template.
        lista.SelectedItem = item;
        vm.SeleccionarVentaSugeridaCommand.Execute(item);
        BusquedaVentaCajaTextBox.Focus();
        e.Handled = true;
    }

    private void SugerenciasVentasCaja_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CajaViewModel vm) return;
        if (e.Key == Key.Enter && SugerenciasVentasCajaListBox.SelectedItem is VentaCorreccionSugeridaItemViewModel item)
        {
            vm.SeleccionarVentaSugeridaCommand.Execute(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CerrarSugerenciasVentas();
            BusquedaVentaCajaTextBox.Focus();
            e.Handled = true;
        }
    }
}
