using System.Windows;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class ConfirmarCambioWindow : Window
{
    private bool _confirmando;

    public ConfirmarCambioWindow(CambiosViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Closed += (_, _) =>
        {
            ContrasenaDuena.Clear();
            if (DataContext is CambiosViewModel vm) vm.ContrasenaAutorizanteIngresada = false;
        };
    }

    public Func<string?, Task<bool>>? ConfirmarOperacionAsync { get; set; }

    private void ContrasenaDuena_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is CambiosViewModel vm)
            vm.ContrasenaAutorizanteIngresada = ContrasenaDuena.Password.Length > 0;
    }

    private async void Confirmar_Click(object sender, RoutedEventArgs e)
    {
        if (_confirmando || ConfirmarOperacionAsync is null) return;

        _confirmando = true;
        ConfirmarButton.IsEnabled = false;
        try
        {
            if (await ConfirmarOperacionAsync(ContrasenaDuena.Password)) DialogResult = true;
        }
        finally
        {
            _confirmando = false;
            if (IsVisible) ConfirmarButton.IsEnabled = true;
        }
    }
}
