using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Printing;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.Printing;

namespace LocalInfantil.Wpf.Views;

public partial class TicketCambioWindow : Window
{
    private readonly ComprobanteCambioPresentacion _modelo;
    private FixedDocument? _documento;
    private FormatoComprobanteCambio Formato => FormatoSelector.SelectedIndex == 1 ? FormatoComprobanteCambio.Ticket80mm : FormatoComprobanteCambio.A4;

    public TicketCambioWindow(DocumentoTicketCambio documento)
    {
        _modelo = new ComprobanteCambioPresentacion(documento);
        InitializeComponent();
        DataContext = documento;
        ActualizarDocumento();
        Loaded += (_, _) => AjustarZoom();
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 32);
        CommandManager.AddPreviewExecutedHandler(this, (_, e) =>
        {
            if (e.Command != ApplicationCommands.Print) return;
            e.Handled = true;
            Imprimir_Click(this, new RoutedEventArgs());
        });
    }

    private void Formato_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VistaPrevia is not null) ActualizarDocumento();
    }

    private void ActualizarDocumento()
    {
        _documento = new DocumentoCambioRenderer().Renderizar(_modelo, Formato, LogoNegocio.ObtenerBannerPng());
        VistaPrevia.Document = _documento;
        AjustarZoom();
    }

    private void VistaPrevia_SizeChanged(object sender, SizeChangedEventArgs e) => AjustarZoom();
    private void AjustarZoom()
    {
        if (VistaPrevia.ActualWidth <= 0) return;
        VistaPrevia.Zoom = Formato == FormatoComprobanteCambio.Ticket80mm ? 100
            : Math.Clamp((VistaPrevia.ActualWidth - 40) / DocumentoCambioRenderer.AnchoA4 * 100, 30, 95);
    }

    private void Imprimir_Click(object sender, RoutedEventArgs e)
    {
        MensajeImpresion.Visibility = Visibility.Collapsed;
        if (_documento is null) return;
        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;
            var solicitud = dialog.PrintTicket.Clone();
            solicitud.PageOrientation = PageOrientation.Portrait;
            solicitud.PageMediaSize = Formato == FormatoComprobanteCambio.A4
                ? new PageMediaSize(PageMediaSizeName.ISOA4)
                : new PageMediaSize(DocumentoCambioRenderer.AnchoTicket, _documento.Pages[0].Child.Height);
            var validado = dialog.PrintQueue.MergeAndValidatePrintTicket(dialog.PrintTicket, solicitud).ValidatedPrintTicket;
            var ancho = Formato == FormatoComprobanteCambio.A4 ? DocumentoCambioRenderer.AnchoA4 : DocumentoCambioRenderer.AnchoTicket;
            if (validado.PageMediaSize?.Width is { } anchoAdmitido && Math.Abs(anchoAdmitido - ancho) > 3
                || validado.PageMediaSize?.Height is { } altoAdmitido && altoAdmitido + 3 < _documento.Pages[0].Child.Height)
                throw new InvalidOperationException("La impresora no admite el tamaño de papel solicitado. Configurá un papel compatible con el formato elegido");
            dialog.PrintTicket = validado;
            dialog.PrintDocument(_documento.DocumentPaginator, $"TiNA kids · {_modelo.Tipo} {_modelo.Datos.Numero}");
        }
        catch (Exception ex)
        {
            MensajeImpresion.Text = $"No se pudo imprimir el comprobante: {ex.Message}. Revisá la impresora y el papel seleccionado.";
            MensajeImpresion.Visibility = Visibility.Visible;
        }
    }
}
