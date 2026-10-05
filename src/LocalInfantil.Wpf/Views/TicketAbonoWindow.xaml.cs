using System.Windows;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.Views;

public partial class TicketAbonoWindow : Window
{
    public TicketAbonoWindow(DocumentoTicketAbono documento)
    {
        InitializeComponent();
        DataContext = documento;
    }
}
