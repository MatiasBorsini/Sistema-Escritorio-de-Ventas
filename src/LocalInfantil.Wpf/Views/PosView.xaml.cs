using System.Windows.Controls;

namespace LocalInfantil.Wpf.Views;

public partial class PosView : UserControl
{
    public PosView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.PosViewModel vm)
            {
                vm.SolicitarFocoBuscador -= Enfocar;
                vm.SolicitarFocoBuscador += Enfocar;
            }
        };
    }

    private void Enfocar() => Buscador.EnfocarBuscador();
}
