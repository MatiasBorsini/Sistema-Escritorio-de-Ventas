using System.Windows;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Infra;

/// <summary>
/// Ventana de detalle de recepción (consulta modeless, solo lectura).
/// Reutiliza RegistroVentanasAbiertas: una sola ventana por recepción.
/// </summary>
public interface IDialogosRecepciones
{
    void MostrarDetalleRecepcion(ResumenRecepcion resumen);
}

public sealed class DialogosRecepcionesWpf : IDialogosRecepciones
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private readonly RegistroVentanasAbiertas _registro = new();
    private readonly Dictionary<string, Views.DetalleRecepcionWindow> _ventanas = new(StringComparer.Ordinal);

    public DialogosRecepcionesWpf(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
    }

    public void MostrarDetalleRecepcion(ResumenRecepcion resumen)
    {
        if (_ventanas.TryGetValue(resumen.Id, out var existente))
        {
            if (existente.WindowState == WindowState.Minimized)
                existente.WindowState = WindowState.Normal;
            existente.Activate();
            existente.Focus();
            return;
        }

        if (!_registro.IntentarAbrir(resumen.Id))
            return;

        var vm = new DetalleRecepcionViewModel(_servicios, _idSesion, resumen);
        var win = new Views.DetalleRecepcionWindow(vm);
        _ventanas[resumen.Id] = win;
        win.Closed += (_, _) =>
        {
            _ventanas.Remove(resumen.Id);
            _registro.Liberar(resumen.Id);
        };
        win.Owner = VentanaPrincipal();
        win.Show();
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
