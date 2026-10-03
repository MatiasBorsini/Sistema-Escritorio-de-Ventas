using System.Windows;
using System.Windows.Controls;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.Views;

public partial class TicketCambioWindow : Window
{
    public TicketCambioWindow(DocumentoTicketCambio documento)
    {
        InitializeComponent();
        DataContext = documento;
    }

    private void Imprimir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        dialog.PrintVisual(Documento, $"LocalInfantil · Cambio {(DataContext as DocumentoTicketCambio)?.Numero}");
    }
}
