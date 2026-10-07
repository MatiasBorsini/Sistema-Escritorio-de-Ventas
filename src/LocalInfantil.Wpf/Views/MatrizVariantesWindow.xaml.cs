using System.Windows;
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
}
