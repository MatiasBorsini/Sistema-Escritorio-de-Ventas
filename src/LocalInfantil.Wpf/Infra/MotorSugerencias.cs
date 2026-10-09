using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LocalInfantil.Wpf.Infra;

/// <summary>
/// Lógica de typeahead sin UI: debounce, cancelación, anti-stale por
/// secuencia, navegación por teclado y cierre. La vista (TextBox + Popup +
/// lista) solo refleja estas propiedades. Nunca propaga excepciones: los
/// fallos van a <see cref="Error"/>.
/// </summary>
public sealed class MotorSugerencias<T> : INotifyPropertyChanged
{
    public static readonly TimeSpan DebouncePredeterminado = TimeSpan.FromMilliseconds(275);

    private readonly Func<string, CancellationToken, Task<IReadOnlyList<T>>> _proveedor;
    private readonly TimeSpan _debounce;
    private readonly Func<TimeSpan, CancellationToken, Task> _espera;
    private readonly int _minimoCaracteres;
    private readonly int _maximo;
    private readonly bool _seleccionarPrimeraAlBuscar;
    private CancellationTokenSource? _cts;
    private long _secuencia;
    private string _texto = string.Empty;
    private List<T> _sugerencias = new();
    private int _indiceActivo = -1;
    private bool _ocupado;
    private bool _abierto;
    private bool _sinCoincidencias;
    private string? _error;

    public MotorSugerencias(
        Func<string, CancellationToken, Task<IReadOnlyList<T>>> proveedor,
        TimeSpan? debounce = null,
        Func<TimeSpan, CancellationToken, Task>? espera = null,
        int minimoCaracteres = 2,
        int maximo = 10,
        bool seleccionarPrimeraAlBuscar = true)
    {
        _proveedor = proveedor ?? throw new ArgumentNullException(nameof(proveedor));
        _debounce = debounce ?? DebouncePredeterminado;
        _espera = espera ?? ((d, ct) => Task.Delay(d, ct));
        _minimoCaracteres = minimoCaracteres;
        _maximo = maximo;
        _seleccionarPrimeraAlBuscar = seleccionarPrimeraAlBuscar;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Texto
    {
        get => _texto;
        private set => SetProperty(ref _texto, value);
    }

    public IReadOnlyList<T> Sugerencias => _sugerencias;
    public int IndiceActivo { get => _indiceActivo; private set => SetProperty(ref _indiceActivo, value); }
    public T? SugerenciaActiva => IndiceActivo >= 0 && IndiceActivo < _sugerencias.Count ? _sugerencias[IndiceActivo] : default;
    public bool Ocupado { get => _ocupado; private set => SetProperty(ref _ocupado, value); }
    public bool Abierto { get => _abierto; private set => SetProperty(ref _abierto, value); }
    public bool SinCoincidencias { get => _sinCoincidencias; private set => SetProperty(ref _sinCoincidencias, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public int MinimoCaracteres => _minimoCaracteres;

    /// <summary>El texto cambió: debounced, cancela la petición anterior.</summary>
    public async Task TextoCambiadoAsync(string texto)
    {
        texto ??= string.Empty;
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        _cts?.Dispose();
        _cts = null;
        Texto = texto;
        if (texto.Length < _minimoCaracteres)
        {
            LimpiarSinCancelar();
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        var token = cts.Token;
        var secuencia = ++_secuencia;
        Error = null;
        SinCoincidencias = false;
        Ocupado = true;
        Abierto = true;
        try
        {
            await _espera(_debounce, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || secuencia != _secuencia)
        {
            return;
        }

        IReadOnlyList<T> resultado;
        try
        {
            resultado = await _proveedor(texto, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            if (secuencia != _secuencia || token.IsCancellationRequested)
            {
                return;
            }

            Error = "No se pudieron cargar las sugerencias.";
            Ocupado = false;
            Abierto = true;
            return;
        }

        if (secuencia != _secuencia || token.IsCancellationRequested)
        {
            return;
        }

        _sugerencias = resultado.Take(_maximo).ToList();
        IndiceActivo = _seleccionarPrimeraAlBuscar && _sugerencias.Count > 0 ? 0 : -1;
        SinCoincidencias = _sugerencias.Count == 0;
        Ocupado = false;
        Abierto = true;
        OnPropertyChanged(nameof(Sugerencias));
        OnPropertyChanged(nameof(SugerenciaActiva));
    }

    /// <summary>Flechas: mueve el activo con clamp.</summary>
    public void MoverSeleccion(int delta)
    {
        if (!Abierto || _sugerencias.Count == 0) return;
        IndiceActivo = Math.Clamp(IndiceActivo + delta, 0, _sugerencias.Count - 1);
        OnPropertyChanged(nameof(SugerenciaActiva));
    }

    /// <summary>Enter o click: devuelve la activa y cierra.</summary>
    public T? ConfirmarSeleccion()
    {
        var activa = Abierto ? SugerenciaActiva : default;
        Cerrar();
        return activa;
    }

    /// <summary>Esc: cierra sin limpiar el texto.</summary>
    public void Cerrar()
    {
        _cts?.Cancel();
        IndiceActivo = -1;
        Abierto = false;
        Ocupado = false;
        OnPropertyChanged(nameof(SugerenciaActiva));
    }

    /// <summary>
    /// Fija el texto sin buscar (sincronización programática, p. ej. valor
    /// inicial de la fila). No abre ni consulta.
    /// </summary>
    public void EstablecerTexto(string texto)
    {
        Texto = texto ?? string.Empty;
    }

    /// <summary>Texto vacío: cierra y limpia todo.</summary>
    public void Limpiar()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        LimpiarSinCancelar();
    }

    private void LimpiarSinCancelar()
    {
        _sugerencias = new List<T>();
        IndiceActivo = -1;
        Ocupado = false;
        Abierto = false;
        SinCoincidencias = false;
        Error = null;
        OnPropertyChanged(nameof(Sugerencias));
        OnPropertyChanged(nameof(SugerenciaActiva));
    }

    private bool SetProperty<U>(ref U field, U value, [CallerMemberName] string? nombre = null)
    {
        if (EqualityComparer<U>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(nombre);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? nombre = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));
}
