using System.Windows.Controls;
using System.Windows.Input;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class RecepcionesConsultaView : UserControl
{
    public RecepcionesConsultaView() => InitializeComponent();

    /// <summary>Estrictamente UI: doble clic sobre una fila abre el detalle.</summary>
    private void Grilla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is RecepcionesConsultaViewModel vm && vm.FilaSeleccionada is not null)
            vm.AbrirDetalleCommand.Execute(null);
    }
}
