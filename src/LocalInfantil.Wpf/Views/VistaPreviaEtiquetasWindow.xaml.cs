using System.Windows;
using System.Windows.Documents;
using LocalInfantil.Wpf.Printing;

namespace LocalInfantil.Wpf.Views;

public partial class VistaPreviaEtiquetasWindow : Window
{
    private FixedDocument _documento;
    private readonly ServicioImpresionWpf _impresion;
    private readonly Func<Task<FixedDocument>> _refrescar;
    public VistaPreviaEtiquetasWindow(FixedDocument documento, ServicioImpresionWpf impresion, Func<Task<FixedDocument>> refrescar)
    {
        InitializeComponent();
        _documento = documento;
        _impresion = impresion;
        _refrescar = refrescar;
        Visor.Document = documento;
    }
    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
    private async void Imprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _documento = await _refrescar();
            Visor.Document = _documento;
            var resultado = _impresion.Imprimir(_documento, "LocalInfantil - etiquetas de productos");
            Estado.Text = resultado.Cancelado ? "Impresión cancelada; no se envió el trabajo." : resultado.MensajeError ?? "Trabajo enviado a la cola de Windows.";
        }
        catch (Exception ex) { Estado.Text = $"No se pudo preparar la impresión: {ex.Message}"; }
    }
}
