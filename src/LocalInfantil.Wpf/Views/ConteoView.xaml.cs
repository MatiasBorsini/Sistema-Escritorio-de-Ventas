using System.Windows.Controls;
using System.Windows.Threading;
namespace LocalInfantil.Wpf.Views;
public partial class ConteoView : UserControl
{
    public ConteoView() => InitializeComponent();

    private void ConteoView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.ConteoViewModel vm || !vm.Paso1Visible) return;
        Dispatcher.BeginInvoke((Action)BuscadorPaso1.EnfocarBuscador, DispatcherPriority.Input);
    }
}
