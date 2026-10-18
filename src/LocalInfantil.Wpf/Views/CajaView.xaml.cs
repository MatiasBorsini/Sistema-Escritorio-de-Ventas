using System.Windows.Controls;
using System.Windows;
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
}
