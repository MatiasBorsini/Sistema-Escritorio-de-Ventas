using System.Windows;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Infra;

/// <summary>
/// Comprobante de venta (solo lectura). Reutiliza RegistroVentanasAbiertas:
/// una sola ventana por venta. Reimprimir nunca muta registros.
/// </summary>
public interface IDialogosVentas
{
    void MostrarTicket(string ventaId);

    /// <summary>
    /// Pregunta humana ante un posible cobro manual duplicado de Point.
    /// Devuelve true solo si el operador confirma que es una segunda venta.
    /// </summary>
    bool ConfirmarCobroManualDuplicado(string detalle);
}

public sealed class DialogosVentasWpf : IDialogosVentas
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private readonly RegistroVentanasAbiertas _registro = new();
    private readonly Dictionary<string, Views.TicketVentaWindow> _ventanas = new(StringComparer.Ordinal);

    public DialogosVentasWpf(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
    }

    public async void MostrarTicket(string ventaId)
    {
        if (_ventanas.TryGetValue(ventaId, out var existente))
        {
            if (existente.WindowState == WindowState.Minimized)
                existente.WindowState = WindowState.Normal;
            existente.Activate();
            existente.Focus();
            return;
        }

        if (!_registro.IntentarAbrir(ventaId))
            return;

        try
        {
            var documento = await _servicios.CasoGenerarTicket.EjecutarAsync(ventaId, _idSesion());
            if (documento is null)
                return;

            var win = new Views.TicketVentaWindow(documento);
            _ventanas[ventaId] = win;
            win.Closed += (_, _) =>
            {
                _ventanas.Remove(ventaId);
                _registro.Liberar(ventaId);
            };
            win.Owner = VentanaPrincipal();
            win.Show();
        }
        catch
        {
            _registro.Liberar(ventaId);
        }
    }

    public bool ConfirmarCobroManualDuplicado(string detalle)
    {
        var respuesta = MessageBox.Show(
            VentanaPrincipal(),
            $"{detalle}\n\n¿Es una segunda venta del mismo importe y no un duplicado?",
            "Posible cobro duplicado de Point",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        return respuesta == MessageBoxResult.Yes;
    }

    private static Window? VentanaPrincipal()
    {
        var app = System.Windows.Application.Current;
        if (app is null) return null;
        foreach (var w in app.Windows)
            if (w is DevMainWindow dev) return dev;
        return app.MainWindow;
    }
}
