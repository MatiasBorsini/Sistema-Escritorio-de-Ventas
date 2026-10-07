using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.Views;

/// <summary>
/// Búsqueda asistida reutilizable (TextBox + Popup + lista). Toda la lógica
/// vive en <see cref="MotorSugerencias{T}"/>; acá solo cableado visual:
/// tipeo→motor, teclado, click, foco y cierre. Sin consultas ni reglas.
/// </summary>
public partial class BuscadorAsistido : UserControl
{
    public static readonly DependencyProperty MotorProperty =
        DependencyProperty.Register(nameof(Motor), typeof(MotorSugerencias<SugerenciaVariante>), typeof(BuscadorAsistido),
            new PropertyMetadata(null, (d, e) => ((BuscadorAsistido)d).AlCambiarMotor(e.OldValue as MotorSugerencias<SugerenciaVariante>, e.NewValue as MotorSugerencias<SugerenciaVariante>)));

    public static readonly DependencyProperty ModoProperty =
        DependencyProperty.Register(nameof(Modo), typeof(string), typeof(BuscadorAsistido),
            new PropertyMetadata("Variante", (d, _) => ((BuscadorAsistido)d).AplicarPlantilla()));

    public static readonly DependencyProperty SeleccionCommandProperty =
        DependencyProperty.Register(nameof(SeleccionCommand), typeof(ICommand), typeof(BuscadorAsistido), new PropertyMetadata(null));

    public static readonly DependencyProperty BusquedaDirectaCommandProperty =
        DependencyProperty.Register(nameof(BusquedaDirectaCommand), typeof(ICommand), typeof(BuscadorAsistido), new PropertyMetadata(null));

    public static readonly DependencyProperty PriorizarBusquedaDirectaEnEnterProperty =
        DependencyProperty.Register(nameof(PriorizarBusquedaDirectaEnEnter), typeof(bool), typeof(BuscadorAsistido), new PropertyMetadata(false));

    public BuscadorAsistido() => InitializeComponent();

    public MotorSugerencias<SugerenciaVariante>? Motor
    {
        get => (MotorSugerencias<SugerenciaVariante>?)GetValue(MotorProperty);
        set => SetValue(MotorProperty, value);
    }

    /// <summary>"Variante" (código primero) o "Producto" (nombre primero).</summary>
    public string Modo
    {
        get => (string)GetValue(ModoProperty);
        set => SetValue(ModoProperty, value);
    }

    public ICommand? SeleccionCommand
    {
        get => (ICommand?)GetValue(SeleccionCommandProperty);
        set => SetValue(SeleccionCommandProperty, value);
    }

    /// <summary>Enter sin sugerencia activa (búsqueda explícita del host).</summary>
    public ICommand? BusquedaDirectaCommand
    {
        get => (ICommand?)GetValue(BusquedaDirectaCommandProperty);
        set => SetValue(BusquedaDirectaCommandProperty, value);
    }

    public bool PriorizarBusquedaDirectaEnEnter
    {
        get => (bool)GetValue(PriorizarBusquedaDirectaEnEnterProperty);
        set => SetValue(PriorizarBusquedaDirectaEnEnterProperty, value);
    }

    private void AlCambiarMotor(MotorSugerencias<SugerenciaVariante>? anterior, MotorSugerencias<SugerenciaVariante>? nuevo)
    {
        if (anterior is not null)
            anterior.PropertyChanged -= Motor_PropertyChanged;
        Raiz.DataContext = nuevo;
        PanelContenido.DataContext = nuevo;
        AplicarPlantilla();
        if (nuevo is not null)
        {
            nuevo.PropertyChanged += Motor_PropertyChanged;
            if (Caja.Text != nuevo.Texto)
                Caja.Text = nuevo.Texto;
        }
    }

    private void Motor_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MotorSugerencias<SugerenciaVariante>.Texto) && Motor is not null && Caja.Text != Motor.Texto)
            Caja.Text = Motor.Texto;
    }

    private void AplicarPlantilla()
    {
        if (Lista is null) return;
        Lista.ItemTemplate = string.Equals(Modo, "Producto", StringComparison.OrdinalIgnoreCase)
            ? (DataTemplate)Resources["PlantillaProducto"]
            : (DataTemplate)Resources["PlantillaVariante"];
    }

    private void Caja_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Motor is null) return;
        _ = Motor.TextoCambiadoAsync(Caja.Text);
    }

    private void Caja_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Motor is null) return;
        switch (e.Key)
        {
            case Key.Down:
                if (Motor.Abierto) Motor.MoverSeleccion(1);
                else _ = Motor.TextoCambiadoAsync(Caja.Text);
                e.Handled = true;
                break;
            case Key.Up:
                Motor.MoverSeleccion(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                if (PriorizarBusquedaDirectaEnEnter && BusquedaDirectaCommand?.CanExecute(Caja.Text) == true)
                {
                    Motor.Cerrar();
                    BusquedaDirectaCommand.Execute(Caja.Text);
                }
                else if (Motor.Abierto && Motor.SugerenciaActiva is not null)
                {
                    Confirmar(Motor.ConfirmarSeleccion());
                }
                else if (BusquedaDirectaCommand?.CanExecute(Caja.Text) == true)
                {
                    Motor.Cerrar();
                    BusquedaDirectaCommand.Execute(Caja.Text);
                }

                e.Handled = true;
                break;
            case Key.Escape:
                if (Motor.Abierto)
                {
                    Motor.Cerrar();
                    e.Handled = true;
                }

                break;
        }
    }

    private void Lista_Click(object sender, MouseButtonEventArgs e)
    {
        if (Motor is null) return;
        var origen = e.OriginalSource as DependencyObject;
        while (origen is not null && origen is not ListBoxItem)
            origen = VisualTreeHelper.GetParent(origen);
        if (origen is ListBoxItem item && Lista.ItemContainerGenerator.IndexFromContainer(item) is int i && i >= 0)
        {
            while (Motor.IndiceActivo < i) Motor.MoverSeleccion(1);
            while (Motor.IndiceActivo > i) Motor.MoverSeleccion(-1);
            Confirmar(Motor.ConfirmarSeleccion());
        }
    }

    /// <summary>Devuelve el foco al cuadro de búsqueda (p. ej. tras agregar).</summary>
    public void EnfocarBuscador() => Caja.Focus();

    private void Confirmar(SugerenciaVariante? item)
    {
        if (item is null || SeleccionCommand is null) return;
        if (SeleccionCommand.CanExecute(item))
            SeleccionCommand.Execute(item);
    }

    private void Caja_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Motor is null || !Motor.Abierto) return;
        // No cerrar si el foco va al popup (click en sugerencia).
        Dispatcher.BeginInvoke(() =>
        {
            if (Motor.Abierto && !PopupSugerencias.IsKeyboardFocusWithin && !Caja.IsKeyboardFocused)
                Motor.Cerrar();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Popup_Closed(object? sender, EventArgs e)
    {
        // StaysOpen=False lo cierra por fuera: sincronizar el motor.
        if (Motor?.Abierto == true)
            Motor.Cerrar();
    }
}
