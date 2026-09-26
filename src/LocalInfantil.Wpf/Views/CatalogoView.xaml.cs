using System.Windows.Controls;
using System.Windows.Input;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class CatalogoView : UserControl
{
    public CatalogoView() => InitializeComponent();

    /// <summary>Estrictamente UI: doble clic sobre una fila abre la ficha.</summary>
    private void Grilla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CatalogoViewModel vm && vm.FilaSeleccionada is not null)
            vm.AbrirFichaCommand.Execute(null);
    }
}
