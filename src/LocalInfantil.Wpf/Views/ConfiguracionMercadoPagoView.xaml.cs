using System.Windows.Controls;

namespace LocalInfantil.Wpf.Views;

public partial class ConfiguracionMercadoPagoView : UserControl
{
    public ConfiguracionMercadoPagoView()
    {
        InitializeComponent();
        CajaCredencial.PasswordChanged += (_, _) =>
        {
            if (DataContext is ViewModels.ConfiguracionMercadoPagoViewModel vm)
                vm.CredencialTexto = CajaCredencial.Password;
        };
    }
}
