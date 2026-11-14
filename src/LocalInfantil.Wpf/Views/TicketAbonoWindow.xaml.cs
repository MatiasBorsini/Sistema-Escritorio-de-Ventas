using System.Windows;
using System.Windows.Input;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.Views;

public partial class TicketAbonoWindow : Window
{
    public TicketAbonoWindow(DocumentoTicketAbono documento)
    {
        InitializeComponent();
        DataContext = documento;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }
}
