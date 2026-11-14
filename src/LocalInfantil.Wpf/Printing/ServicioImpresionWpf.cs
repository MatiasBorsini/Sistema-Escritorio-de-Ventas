using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace LocalInfantil.Wpf.Printing;

/// <summary>
/// Muestra el diálogo nativo de Windows y solo envía el trabajo después de la confirmación explícita.
/// </summary>
public sealed class ServicioImpresionWpf
{
    public ResultadoEnvioImpresion Imprimir(FixedDocument documento, string descripcionTrabajo)
    {
        ArgumentNullException.ThrowIfNull(documento);
        ArgumentException.ThrowIfNullOrWhiteSpace(descripcionTrabajo);

        try
        {
            var dialogo = new PrintDialog();
            if (dialogo.ShowDialog() != true)
            {
                return ResultadoEnvioImpresion.CrearCancelado();
            }

            dialogo.PrintDocument(documento.DocumentPaginator, descripcionTrabajo);
            return ResultadoEnvioImpresion.CrearEnviado();
        }
        catch (Exception exception)
        {
            return ResultadoEnvioImpresion.CrearError($"No se pudo enviar el trabajo de impresión: {exception.Message}");
        }
    }

    /// <summary>
    /// Envía un reporte A4 con la orientación del documento. Usa el diálogo
    /// nativo, por lo que «Microsoft Print to PDF» permite guardar el PDF.
    /// </summary>
    public ResultadoEnvioImpresion ImprimirA4(FixedDocument documento, string descripcionTrabajo, OrientacionReporte orientacion)
    {
        ArgumentNullException.ThrowIfNull(documento);
        ArgumentException.ThrowIfNullOrWhiteSpace(descripcionTrabajo);

        try
        {
            var dialogo = new PrintDialog();
            if (dialogo.ShowDialog() != true)
            {
                return ResultadoEnvioImpresion.CrearCancelado();
            }

            var solicitud = dialogo.PrintTicket.Clone();
            solicitud.PageOrientation = orientacion == OrientacionReporte.Horizontal
                ? PageOrientation.Landscape
                : PageOrientation.Portrait;
            solicitud.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            var validado = dialogo.PrintQueue.MergeAndValidatePrintTicket(dialogo.PrintTicket, solicitud).ValidatedPrintTicket;

            if (validado.PageMediaSize is { Width: > 0, Height: > 0 } media)
            {
                var tamano = new DocumentoReporteRenderer().TamanoPagina(orientacion);
                var anchoMedia = media.Width!.Value;
                var altoMedia = media.Height!.Value;
                var anchoAceptado = Math.Max(anchoMedia, altoMedia);
                var altoAceptado = Math.Min(anchoMedia, altoMedia);
                var anchoEsperado = Math.Max(tamano.Width, tamano.Height);
                var altoEsperado = Math.Min(tamano.Width, tamano.Height);
                if (Math.Abs(anchoAceptado - anchoEsperado) > 4 || Math.Abs(altoAceptado - altoEsperado) > 4)
                {
                    return ResultadoEnvioImpresion.CrearError(
                        "La impresora no admite hojas A4. Configurá un papel A4 en el diálogo de impresión.");
                }
            }

            dialogo.PrintTicket = validado;
            dialogo.PrintDocument(documento.DocumentPaginator, descripcionTrabajo);
            return ResultadoEnvioImpresion.CrearEnviado();
        }
        catch (Exception exception)
        {
            return ResultadoEnvioImpresion.CrearError($"No se pudo imprimir el reporte: {exception.Message}");
        }
    }
}

public sealed record ResultadoEnvioImpresion(bool Enviado, bool Cancelado, string? MensajeError)
{
    public static ResultadoEnvioImpresion CrearEnviado() => new(true, false, null);

    public static ResultadoEnvioImpresion CrearCancelado() => new(false, true, null);

    public static ResultadoEnvioImpresion CrearError(string mensajeError) => new(false, false, mensajeError);
}
