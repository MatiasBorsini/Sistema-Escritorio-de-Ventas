using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.RegularExpressions;
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

    public static readonly DependencyProperty LimpiarAlSeleccionarProperty =
        DependencyProperty.Register(nameof(LimpiarAlSeleccionar), typeof(bool), typeof(BuscadorAsistido), new PropertyMetadata(false));

    public static readonly DependencyProperty TextoSeleccionadoProperty =
        DependencyProperty.Register(nameof(TextoSeleccionado), typeof(string), typeof(BuscadorAsistido),
            new PropertyMetadata(string.Empty, (d, e) => ((BuscadorAsistido)d).MostrarTextoSeleccionado(e.NewValue as string)));

    public static readonly DependencyProperty EnfocarAlLimpiarMotorProperty =
        DependencyProperty.Register(nameof(EnfocarAlLimpiarMotor), typeof(bool), typeof(BuscadorAsistido), new PropertyMetadata(false));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(BuscadorAsistido), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PatronCodigoExactoEnEnterProperty =
        DependencyProperty.Register(nameof(PatronCodigoExactoEnEnter), typeof(string), typeof(BuscadorAsistido), new PropertyMetadata(null));

    public BuscadorAsistido() => InitializeComponent();

    private bool _reflejandoTextoSeleccionado;
    private string _ultimoTextoMotor = string.Empty;

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

    public bool LimpiarAlSeleccionar
    {
        get => (bool)GetValue(LimpiarAlSeleccionarProperty);
        set => SetValue(LimpiarAlSeleccionarProperty, value);
    }

    /// <summary>Texto de presentación tras una selección; no modifica la consulta ni inicia otra búsqueda.</summary>
    public string TextoSeleccionado
    {
        get => (string)GetValue(TextoSeleccionadoProperty);
        set => SetValue(TextoSeleccionadoProperty, value);
    }

    /// <summary>Devuelve el foco al buscador cuando el host vacía la consulta tras una acción exitosa.</summary>
    public bool EnfocarAlLimpiarMotor
    {
        get => (bool)GetValue(EnfocarAlLimpiarMotorProperty);
        set => SetValue(EnfocarAlLimpiarMotorProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Opt-in para hosts donde un código exacto debe preceder a la selección del typeahead.</summary>
    public string? PatronCodigoExactoEnEnter
    {
        get => (string?)GetValue(PatronCodigoExactoEnEnterProperty);
        set => SetValue(PatronCodigoExactoEnEnterProperty, value);
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
            _ultimoTextoMotor = nuevo.Texto;
            if (Caja.Text != nuevo.Texto)
                Caja.Text = nuevo.Texto;
        }
    }

    private void Motor_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MotorSugerencias<SugerenciaVariante>.Texto) || Motor is null) return;
        var textoAnterior = _ultimoTextoMotor;
        _ultimoTextoMotor = Motor.Texto;
        if (Caja.Text != Motor.Texto)
            Caja.Text = Motor.Texto;
        if (EnfocarAlLimpiarMotor && textoAnterior.Length > 0 && Motor.Texto.Length == 0)
            Dispatcher.BeginInvoke(new Action(() => Caja.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void AplicarPlantilla()
    {
        if (Lista is null) return;
        Lista.ItemTemplate = string.Equals(Modo, "Producto", StringComparison.OrdinalIgnoreCase)
            ? (DataTemplate)Resources["PlantillaProducto"]
            : string.Equals(Modo, "VarianteStock", StringComparison.OrdinalIgnoreCase)
                ? (DataTemplate)Resources["PlantillaVarianteStock"]
                : string.Equals(Modo, "VarianteAgregar", StringComparison.OrdinalIgnoreCase)
                    ? (DataTemplate)Resources["PlantillaVarianteAgregar"]
                    : (DataTemplate)Resources["PlantillaVariante"];
    }

    private void Caja_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_reflejandoTextoSeleccionado) return;
        // La sincronización VM→Motor también puede reflejar el texto de vuelta
        // en Caja. En ese caso no hay una nueva búsqueda que iniciar.
        if (Motor is null || string.Equals(Caja.Text, Motor.Texto, StringComparison.Ordinal)) return;
        _ = Motor.TextoCambiadoAsync(Caja.Text);
    }

    private void MostrarTextoSeleccionado(string? texto)
    {
        if (string.IsNullOrEmpty(texto) || Caja is null || Caja.Text == texto) return;
        _reflejandoTextoSeleccionado = true;
        try { Caja.Text = texto; }
        finally { _reflejandoTextoSeleccionado = false; }
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
                else if (EsCodigoExacto(Caja.Text) && BusquedaDirectaCommand?.CanExecute(Caja.Text) == true)
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

    private void Lista_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Motor is null) return;
        var origen = e.OriginalSource as DependencyObject;
        var botonAgregar = origen;
        while (botonAgregar is not null && botonAgregar is not Button)
            botonAgregar = VisualTreeHelper.GetParent(botonAgregar);
        if (botonAgregar is Button { Content: "Agregar" }) return;

        while (origen is not null && origen is not ListBoxItem)
            origen = VisualTreeHelper.GetParent(origen);
        if (origen is ListBoxItem { DataContext: SugerenciaVariante sugerencia })
        {
            // Se toma la sugerencia apuntada por el clic directamente, sin
            // depender de Motor.Abierto/IndiceActivo: un cierre por foco no
            // debe invalidar el clic ya iniciado sobre la fila.
            Motor.Cerrar();
            Confirmar(sugerencia);
            e.Handled = true;
        }
    }

    /// <summary>Devuelve el foco al cuadro de búsqueda (p. ej. tras agregar).</summary>
    public void EnfocarBuscador() => Caja.Focus();

    private void Confirmar(SugerenciaVariante? item)
    {
        if (item is null || SeleccionCommand is null) return;
        if (!SeleccionCommand.CanExecute(item)) return;
        SeleccionCommand.Execute(item);
        if (!LimpiarAlSeleccionar || Motor is null) return;
        Motor.EstablecerTexto(string.Empty);
        Motor.Limpiar();
        Dispatcher.BeginInvoke(new Action(() => Caja.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    private bool EsCodigoExacto(string texto)
    {
        var patron = PatronCodigoExactoEnEnter;
        return !string.IsNullOrWhiteSpace(patron)
            && Regex.IsMatch(texto.Trim(), patron, RegexOptions.CultureInvariant);
    }

    private void AgregarButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SugerenciaVariante sugerencia })
        {
            Confirmar(sugerencia);
            e.Handled = true;
        }
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
