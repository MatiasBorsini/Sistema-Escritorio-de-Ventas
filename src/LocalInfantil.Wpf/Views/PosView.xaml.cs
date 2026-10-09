using System.Windows.Controls;
using System.Windows.Threading;

namespace LocalInfantil.Wpf.Views;

public partial class PosView : UserControl
{
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
}
