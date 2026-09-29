using System.Windows;
using System.Windows.Media;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Printing;

namespace LocalInfantil.Wpf.Views;

public partial class VistaPreviaImpresionWindow : Window
{
    private readonly ServicioImpresionWpf _servicioImpresion = new();
    private readonly System.Windows.Documents.FixedDocument _documentoVisual;

    public VistaPreviaImpresionWindow(DocumentoImpresionDePrueba documento)
    {
        ArgumentNullException.ThrowIfNull(documento);
        InitializeComponent();

        _documentoVisual = new DocumentoImpresionDePruebaRenderer().Renderizar(documento);
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
        var resultado = _servicioImpresion.Imprimir(_documentoVisual, "LocalInfantil - impresión de prueba");

        if (resultado.Enviado)
        {
            MensajeEstado.Foreground = Brushes.DarkGreen;
            MensajeEstado.Text = "El trabajo de impresión de prueba fue enviado a la impresora elegida.";
            return;
        }

        if (resultado.Cancelado)
        {
            MensajeEstado.Foreground = Brushes.DimGray;
            MensajeEstado.Text = "La impresión fue cancelada. No se envió ningún trabajo.";
            return;
        }

        MensajeEstado.Foreground = Brushes.Firebrick;
        MensajeEstado.Text = resultado.MensajeError ?? "No se pudo completar la impresión de prueba.";
    }
}
