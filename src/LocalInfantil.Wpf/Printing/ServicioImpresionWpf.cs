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
}

public sealed record ResultadoEnvioImpresion(bool Enviado, bool Cancelado, string? MensajeError)
{
    public static ResultadoEnvioImpresion CrearEnviado() => new(true, false, null);

    public static ResultadoEnvioImpresion CrearCancelado() => new(false, true, null);

    public static ResultadoEnvioImpresion CrearError(string mensajeError) => new(false, false, mensajeError);
}
