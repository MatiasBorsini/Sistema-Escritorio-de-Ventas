using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.Views;

public partial class BuscadorCliente : UserControl
{
    public static readonly DependencyProperty MotorProperty = DependencyProperty.Register(nameof(Motor), typeof(MotorSugerencias<SugerenciaCliente>), typeof(BuscadorCliente), new PropertyMetadata(null, (d, e) => ((BuscadorCliente)d).CambiarMotor(e.OldValue as MotorSugerencias<SugerenciaCliente>, e.NewValue as MotorSugerencias<SugerenciaCliente>)));
    public static readonly DependencyProperty SeleccionCommandProperty = DependencyProperty.Register(nameof(SeleccionCommand), typeof(ICommand), typeof(BuscadorCliente));
    public BuscadorCliente() => InitializeComponent();
    public MotorSugerencias<SugerenciaCliente>? Motor { get => (MotorSugerencias<SugerenciaCliente>?)GetValue(MotorProperty); set => SetValue(MotorProperty, value); }
    public ICommand? SeleccionCommand { get => (ICommand?)GetValue(SeleccionCommandProperty); set => SetValue(SeleccionCommandProperty, value); }

    private void CambiarMotor(MotorSugerencias<SugerenciaCliente>? oldMotor, MotorSugerencias<SugerenciaCliente>? newMotor)
    {
        if (oldMotor is not null) oldMotor.PropertyChanged -= Motor_PropertyChanged;
        DataContext = newMotor;
        if (newMotor is not null) newMotor.PropertyChanged += Motor_PropertyChanged;
    }
    private void Motor_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Motor is null) return;
        PopupSugerencias.IsOpen = Motor.Abierto;
        Hint.Visibility = string.IsNullOrEmpty(Motor.Texto) ? Visibility.Visible : Visibility.Collapsed;
        if (e.PropertyName == nameof(MotorSugerencias<SugerenciaCliente>.Texto) && Caja.Text != Motor.Texto) Caja.Text = Motor.Texto;
    }
    private void Caja_TextChanged(object sender, TextChangedEventArgs e)
    {
        Hint.Visibility = string.IsNullOrEmpty(Caja.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (Motor is not null) _ = Motor.TextoCambiadoAsync(Caja.Text);
    }
    private void Caja_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Motor is null) return;
        if (e.Key == Key.Down) { if (Motor.Abierto) Motor.MoverSeleccion(1); else _ = Motor.TextoCambiadoAsync(Caja.Text); e.Handled = true; }
        else if (e.Key == Key.Up) { Motor.MoverSeleccion(-1); e.Handled = true; }
        else if (e.Key == Key.Enter) { var selected = Motor.ConfirmarSeleccion(); if (selected is not null && SeleccionCommand?.CanExecute(selected) == true) SeleccionCommand.Execute(selected); e.Handled = true; }
        else if (e.Key == Key.Escape && Motor.Abierto) { Motor.Cerrar(); e.Handled = true; }
    }
    private void Lista_Click(object sender, MouseButtonEventArgs e)
    {
        if (Motor is null) return;
        var node = e.OriginalSource as DependencyObject;
        while (node is not null && node is not ListBoxItem) node = VisualTreeHelper.GetParent(node);
        if (node is ListBoxItem item)
        {
            var index = Lista.ItemContainerGenerator.IndexFromContainer(item);
            while (Motor.IndiceActivo < index) Motor.MoverSeleccion(1);
            while (Motor.IndiceActivo > index) Motor.MoverSeleccion(-1);
            var selected = Motor.ConfirmarSeleccion(); if (selected is not null && SeleccionCommand?.CanExecute(selected) == true) SeleccionCommand.Execute(selected);
        }
    }
    private void Caja_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => Dispatcher.BeginInvoke(() => { if (Motor?.Abierto == true && !PopupSugerencias.IsKeyboardFocusWithin && !Caja.IsKeyboardFocused) Motor.Cerrar(); }, System.Windows.Threading.DispatcherPriority.Input);
    private void Popup_Closed(object? sender, EventArgs e) { if (Motor?.Abierto == true) Motor.Cerrar(); }
}
