using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf.Views;

public partial class CobrarVentaWindow : Window
{
    private readonly PosViewModel _viewModel;

    public CobrarVentaWindow(PosViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Loaded += (_, _) => AjustarAlAreaDelPropietario();
    }

    private void AjustarAlAreaDelPropietario()
    {
        if (Owner is null) return;

        var anchoPropietario = Owner.ActualWidth > 0 ? Owner.ActualWidth : SystemParameters.WorkArea.Width;
        var altoPropietario = Owner.ActualHeight > 0 ? Owner.ActualHeight : SystemParameters.WorkArea.Height;
        Width = Math.Min(1000, Math.Max(760, anchoPropietario * 0.94));
        MaxHeight = Math.Min(700, Math.Max(400, Math.Min(altoPropietario, SystemParameters.WorkArea.Height) * 0.94));
        MinWidth = Math.Min(760, Width);
        MinHeight = Math.Min(400, MaxHeight);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PosViewModel.CobroVisible)
            && !_viewModel.CobroVisible
            && !string.IsNullOrWhiteSpace(_viewModel.UltimoNumero))
        {
            Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Background);
        }
    }

    private void VolverCarrito_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.PuedeCerrarCobro) return;
        _viewModel.CerrarCobroCommand.Execute(null);
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.CobroVisible && !string.IsNullOrWhiteSpace(_viewModel.UltimoNumero)) return;
        if (!_viewModel.PuedeCerrarCobro)
        {
            e.Cancel = true;
            return;
        }

        if (_viewModel.CobroVisible)
            _viewModel.CerrarCobroCommand.Execute(null);
    }
}
