using System.Windows;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Infra;

/// <summary>
/// Diálogos secundarios de Catálogo. La ventana de ficha vive acá (capa UI)
/// para que los ViewModels sigan testeables con un Fake. Clave "__nuevo__"
/// para modo creación: una sola ventana de alta a la vez.
/// </summary>
public interface IDialogosCatalogo
{
    /// <summary>
    /// Abre la ficha. Al cerrar, si el wizard creó un producto, se informa
    /// su id por <paramref name="alProductoCreado"/> (para seleccionarlo).
    /// </summary>
    void MostrarFichaProducto(string? productoId, Func<Task>? alCerrar = null, Func<string, Task>? alProductoCreado = null);
}

/// <summary>
/// Registro testeable de ventanas abiertas: evita dos editores del mismo
/// producto simultáneamente. La ventana real se guarda aparte en el diálogo.
/// </summary>
public sealed class RegistroVentanasAbiertas
{
    private readonly HashSet<string> _abiertas = new(StringComparer.Ordinal);

    public bool IntentarAbrir(string clave)
    {
        lock (_abiertas)
            return _abiertas.Add(clave);
    }

    public void Liberar(string clave)
    {
        lock (_abiertas)
            _abiertas.Remove(clave);
    }

    public bool EstaAbierta(string clave)
    {
        lock (_abiertas)
            return _abiertas.Contains(clave);
    }
}

public sealed class DialogosCatalogoWpf : IDialogosCatalogo
{
    public const string ClaveCreacion = "__nuevo__";
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private readonly RegistroVentanasAbiertas _registro = new();
    private readonly Dictionary<string, Views.FichaProductoWindow> _ventanas = new(StringComparer.Ordinal);

    public DialogosCatalogoWpf(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
    }

    public void MostrarFichaProducto(string? productoId, Func<Task>? alCerrar = null, Func<string, Task>? alProductoCreado = null)
    {
        var clave = productoId ?? ClaveCreacion;
        if (_ventanas.TryGetValue(clave, out var existente))
        {
            if (existente.WindowState == WindowState.Minimized)
                existente.WindowState = WindowState.Normal;
            existente.Activate();
            existente.Focus();
            return;
        }

        if (!_registro.IntentarAbrir(clave))
            return;

        var vm = new FichaProductoViewModel(_servicios, _idSesion, productoId);
        var win = new Views.FichaProductoWindow(vm);
        _ventanas[clave] = win;
        win.Closed += async (_, _) =>
        {
            var creado = vm.ProductoIdCreado;
            _ventanas.Remove(clave);
            _registro.Liberar(clave);
            if (alCerrar is not null)
            {
                try { await alCerrar.Invoke(); } catch { }
            }

            if (creado is not null && alProductoCreado is not null)
            {
                try { await alProductoCreado.Invoke(creado); } catch { }
            }
        };
        win.Owner = VentanaPrincipal();
        win.ShowDialog();
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
