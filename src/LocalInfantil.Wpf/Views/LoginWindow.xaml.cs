using System.Windows;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

/// <summary>
/// Ventana de inicio de sesión separada y compacta (referencia
/// docs/ui-reference/LocalInfantil-login-compacto.html). No muestra la
/// navegación principal. Sin "recordar usuario" persistente en esta fase.
/// Al autenticar, App cierra esta ventana y abre la principal.
/// </summary>
public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel vm, string? rutaDev)
    {
        InitializeComponent();
        DataContext = vm;
        if (!string.IsNullOrWhiteSpace(rutaDev))
        {
            BannerDev.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
    }
}
