using System.Windows;
using System.Windows.Media;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Printing;

namespace LocalInfantil.Wpf.Views;

/// <summary>
/// Comprobante interno de venta (solo lectura). Reimprimir nunca muta
/// registros: el documento se reconstruye desde snapshots persistidos.
/// </summary>
public partial class TicketVentaWindow : Window
{
    private readonly ServicioImpresionWpf _servicioImpresion = new();
    private readonly System.Windows.Documents.FixedDocument _documentoVisual;

    public string Titulo { get; }

    public TicketVentaWindow(DocumentoTicketVenta documento)
    {
        ArgumentNullException.ThrowIfNull(documento);
        Titulo = $"Comprobante interno {documento.Numero}";
        DataContext = this;
        InitializeComponent();

        _documentoVisual = new DocumentoTicketVentaRenderer().Renderizar(documento);
        VistaDocumento.Document = _documentoVisual;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private void Imprimir_Click(object sender, RoutedEventArgs e)
    {
        var resultado = _servicioImpresion.Imprimir(_documentoVisual, "LocalInfantil - comprobante interno");

        if (resultado.Enviado)
        {
            MensajeEstado.Foreground = Brushes.DarkGreen;
            MensajeEstado.Text = "El comprobante fue enviado a la impresora elegida.";
            return;
        }

        if (resultado.Cancelado)
        {
            MensajeEstado.Foreground = Brushes.DimGray;
            MensajeEstado.Text = "La impresión fue cancelada. No se envió ningún trabajo.";
            return;
        }

        MensajeEstado.Foreground = Brushes.Firebrick;
        MensajeEstado.Text = resultado.MensajeError ?? "No se pudo completar la impresión.";
    }
}
