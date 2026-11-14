using System.Windows;
using System.Windows.Controls;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.Printing;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class ReportesView : UserControl
{
    public ReportesView() => InitializeComponent();

    private async void Imprimir_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportesViewModel vm) return;
        var boton = sender as Button;
        if (boton is not null) boton.IsEnabled = false;
        try
        {
            var modelo = await vm.PrepararReporteActualAsync();
            if (modelo is null)
            {
                if (!string.IsNullOrWhiteSpace(vm.Error))
                {
                    MessageBox.Show(Window.GetWindow(this), vm.Error, "Impresión de reportes",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            var documento = new DocumentoReporteRenderer().Renderizar(modelo, LogoNegocio.ObtenerBannerPng());
            var resultado = new ServicioImpresionWpf().ImprimirA4(documento, $"TiNA kids · {modelo.Titulo}", modelo.Orientacion);
            if (resultado.MensajeError is { } error)
            {
                MessageBox.Show(Window.GetWindow(this), error, "Impresión de reportes",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),
                $"No se pudo preparar el reporte para imprimir: {ex.Message}",
                "Impresión de reportes", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (boton is not null)
                boton.GetBindingExpression(IsEnabledProperty)?.UpdateTarget();
        }
    }
}
