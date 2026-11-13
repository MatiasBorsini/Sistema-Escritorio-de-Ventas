using System.Windows.Controls;
using System.Windows.Threading;

namespace LocalInfantil.Wpf.Views;

public partial class PosView : UserControl
{
    private void ContinuarCobro_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.PosViewModel vm) return;
        vm.AbrirCobroCommand.Execute(null);
        if (!vm.CobroVisible) return;

        var ventana = new CobrarVentaWindow(vm)
        {
            Owner = System.Windows.Window.GetWindow(this)
        };
        ventana.ShowDialog();
    }

    public PosView()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(Enfocar), DispatcherPriority.Input);
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ViewModels.PosViewModel anterior)
                anterior.SolicitarFocoBuscador -= Enfocar;
            if (DataContext is ViewModels.PosViewModel vm)
            {
                vm.SolicitarFocoBuscador -= Enfocar;
                vm.SolicitarFocoBuscador += Enfocar;
            }
        };
    }

    private void Enfocar() => Buscador.EnfocarBuscador();

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

    }
}
