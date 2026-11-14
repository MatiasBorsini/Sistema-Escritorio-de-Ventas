using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class CambiosView : UserControl
{
    private bool _restaurandoSeleccion;
    private CambiosViewModel? _vm;
    public CambiosView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => EnlazarViewModel();
        Loaded += async (_, _) =>
        {
            _ = Dispatcher.BeginInvoke(new Action(() => BuscarVentaTextBox.Focus()), DispatcherPriority.Input);
            if (DataContext is CambiosViewModel vm) await vm.BuscarAsyncPublic();
        };
    }

    private void EnlazarViewModel()
    {
        if (_vm is not null)
        {
            _vm.Ventas.CollectionChanged -= Ventas_CollectionChanged;
            _vm.PropertyChanged -= Vm_PropertyChanged;
        }
        _vm = DataContext as CambiosViewModel;
        if (_vm is not null)
        {
            _vm.Ventas.CollectionChanged += Ventas_CollectionChanged;
            _vm.PropertyChanged += Vm_PropertyChanged;
        }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CambiosViewModel.Venta)) SincronizarSeleccionVenta();
    }

    private void Ventas_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SincronizarSeleccionVenta();

    private void SincronizarSeleccionVenta()
    {
        // La búsqueda reactiva reconstruye la lista preservando la identidad de
        // las filas; devolvemos la selección visual a la venta en curso sin
        // cambiar la venta del borrador. También refleja la autoselección de la
        // primera venta que decide el ViewModel.
        if (_vm?.Venta is null || ReferenceEquals(VentasListBox.SelectedItem, _vm.Venta)) return;
        _restaurandoSeleccion = true;
        VentasListBox.SelectedItem = _vm.Venta;
        _restaurandoSeleccion = false;
    }

    private void VentasListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ListBox { SelectedItem: FilaVentaCambio venta }) return;
        if (DataContext is CambiosViewModel vm) vm.Venta = venta;
        EnfocarOperacion();
        e.Handled = true;
    }

    private async void BuscarVentas_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CambiosViewModel vm) return;
        await vm.BuscarAsyncPublic();
    }

    private async void BuscarVentaTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not CambiosViewModel vm) return;
        await vm.BuscarAsyncPublic();
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
    }

    private void EnfocarOperacion()
    {
        if (DataContext is CambiosViewModel { EsModoCambio: true }) BuscadorArticulo.EnfocarBuscador();
        else EntradasDataGrid.Focus();
    }

    private bool ConfirmarPerdidaBorrador(CambiosViewModel vm)
        => !vm.TieneBorradorOperacion || MessageBox.Show(Window.GetWindow(this),
            "Hay datos cargados en la operación actual. Si continuás, se perderán. ¿Querés seguir?",
            "Descartar borrador", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private async void Continuar_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CambiosViewModel vm) return;
        var owner = Window.GetWindow(this);
        var confirmacion = new ConfirmarCambioWindow(vm) { Owner = owner };
        confirmacion.ConfirmarOperacionAsync = contrasena => ConfirmarOperacionAsync(vm, confirmacion, contrasena);
        // El comprobante se abre contra la ventana que contiene la vista: la
        // modal ya está cerrada y WPF rechaza asignarla como Owner.
        if (confirmacion.ShowDialog() == true)
            await MostrarComprobanteSiNuevoAsync(vm, owner);
    }

    private async Task<bool> ConfirmarOperacionAsync(CambiosViewModel vm, Window? owner, string? contrasenaAutorizante)
    {
        var cambioPrevio = vm.CambioIdReciente;
        var resultado = await vm.ConfirmarAsync(contrasenaAutorizante);
        if (resultado is LocalInfantil.Application.Contracts.ResultadoCambio.DecisionCuentaRequerida deuda)
        {
            var decision = MessageBox.Show(owner,
                $"La venta original todavía tiene una deuda pendiente de {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(deuda.DeudaVentaMinor)}.\nEl crédito de la devolución es {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(deuda.CreditoDevolucionMinor)}.\n\n¿Aplicar el crédito a la deuda? Elegí No para reintegrarlo.",
                "Decisión de cuenta corriente", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (decision == MessageBoxResult.Cancel) return false;
            var aplicar = decision == MessageBoxResult.Yes;
            var decisionCuenta = aplicar ? LocalInfantil.Application.Contracts.DecisionCuentaCorrienteCambio.AplicarADeuda : LocalInfantil.Application.Contracts.DecisionCuentaCorrienteCambio.Reintegrar;
            resultado = await vm.ConfirmarAsync(decisionCuenta: decisionCuenta, deudaEsperadaMinor: deuda.DeudaVentaMinor);
        }
        if (resultado is LocalInfantil.Application.Contracts.ResultadoCambio.ConfirmacionCuenta preview)
        {
            var texto = $"Deuda de la venta antes: {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(preview.DeudaAnteriorMinor)}\nCrédito de la devolución: {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(preview.CreditoMinor)}\nAplicar a deuda: {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(preview.AplicarMinor)}\nDeuda posterior: {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(preview.DeudaPosteriorMinor)}\nReintegro en caja: {LocalInfantil.Wpf.ViewModels.PosViewModel.FormatearArs(preview.ReintegrarMinor)}";
            if (MessageBox.Show(owner, texto + "\n\n¿Confirmar esta decisión?", "Confirmar devolución", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
            var passwordFinal = preview.ReintegrarMinor > 0 && vm.RequiereAutorizacionPresencial ? contrasenaAutorizante : null;
            resultado = await vm.ConfirmarAsync(passwordFinal, preview.Decision, true, preview.DeudaAnteriorMinor);
        }
        return !string.IsNullOrWhiteSpace(vm.CambioIdReciente) && vm.CambioIdReciente != cambioPrevio;
    }

    private static async Task MostrarComprobanteSiNuevoAsync(CambiosViewModel vm, Window? owner)
    {
        if (string.IsNullOrWhiteSpace(vm.CambioIdReciente)) return;
        try
        {
            var documento = await vm.ObtenerTicketAsync(vm.CambioIdReciente);
            if (documento is not null) new TicketCambioWindow(documento) { Owner = owner }.ShowDialog();
        }
        catch (Exception ex)
        {
            // La operación ya quedó registrada: se informa y no se pierde el shell.
            MessageBox.Show(owner,
                $"El cambio quedó registrado, pero no se pudo abrir el comprobante: {ex.Message}\n\nFigura en el historial de cambios y devoluciones.",
                "Comprobante no disponible", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void HistorialListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: LocalInfantil.Application.Contracts.ResumenCambio resumen } lista
            || ItemsControl.ContainerFromElement(lista, e.OriginalSource as DependencyObject) is not DataGridRow
            || DataContext is not CambiosViewModel vm) return;
        var owner = Window.GetWindow(this);
        try
        {
            var documento = await vm.ObtenerTicketAsync(resumen.Id);
            if (documento is not null) new TicketCambioWindow(documento) { Owner = owner }.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"No se pudo abrir el comprobante: {ex.Message}", "Comprobante no disponible",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
