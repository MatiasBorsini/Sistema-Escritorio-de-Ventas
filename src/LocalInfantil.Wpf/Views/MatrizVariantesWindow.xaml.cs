using System.Windows;
using System.Windows.Input;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class MatrizVariantesWindow : Window
{
    public MatrizVariantesWindow(MatrizVariantesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.SolicitarCierre += Close;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }
}
