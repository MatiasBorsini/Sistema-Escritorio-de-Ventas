using System.Collections.ObjectModel;
using System.Globalization;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

/// <summary>
/// Fila del carrito: variante resuelta con precio vigente congelado al
/// agregar y cantidad editable. Solo vive en la VM; cancelar no persiste.
/// </summary>
public sealed class FilaVentaViewModel : ViewModelBase
{
    private long _cantidad = 1;
    private long? _disponible;

    public string VarianteId { get; set; } = string.Empty;
    public string CodigoInterno { get; set; } = string.Empty;
    public string NombreProducto { get; set; } = string.Empty;
    public string? Talle { get; set; }
    public string? Color { get; set; }
    public long PrecioUnitarioMinor { get; set; }

    public string DetalleTalleColor => (Talle, Color) switch
    {
        (null, null) => "—",
        (null, var c) => c!,
        (var t, null) => t!,
        (var t, var c) => $"{t} · {c}",
    };

    public long Cantidad
    {
        get => _cantidad;
        set
        {
            var v = Math.Max(1, value);
            if (SetProperty(ref _cantidad, v))
                OnPropertyChanged(nameof(SubtotalMinor));
        }
    }

    /// <summary>Aviso hacia el POS cuando la cantidad excede el Disponible.</summary>
    public Action<string>? AvisoStock { get; set; }

    public string CantidadTexto
    {
        get => _cantidad.ToString();
        set
        {
            if (!long.TryParse(value.Trim(), out var c) || c <= 0)
            {
                OnPropertyChanged(nameof(CantidadTexto));
                return;
            }

            if (Disponible.HasValue && c > Disponible.Value)
            {
                AvisoStock?.Invoke($"Solo hay {Disponible.Value} unidades disponibles de esta variante.");
                OnPropertyChanged(nameof(CantidadTexto));
                return;
            }

            Cantidad = c;
        }
    }

    public long SubtotalMinor => Cantidad * PrecioUnitarioMinor;

    /// <summary>Disponible conocido al agregar (orientativo; la confirmación recalcula).</summary>
    public long? Disponible { get => _disponible; set => SetProperty(ref _disponible, value); }
}

/// <summary>
/// Fila de pago: un medio con su importe imputado; recibido solo efectivo.
/// </summary>
public sealed class FilaPagoViewModel : ViewModelBase
{
    private string _medio = MediosPago.Efectivo;
    private string _importeTexto = string.Empty;
    private string _recibidoTexto = string.Empty;

    public string Medio
    {
        get => _medio;
        set
        {
            if (SetProperty(ref _medio, value))
            {
                OnPropertyChanged(nameof(EsEfectivo));
                OnPropertyChanged(nameof(ImporteMinor));
                OnPropertyChanged(nameof(RecibidoMinor));
                OnPropertyChanged(nameof(VueltoMinor));
            }
        }
    }

    public bool EsEfectivo => _medio == MediosPago.Efectivo;

    public string ImporteTexto
    {
        get => _importeTexto;
        set
        {
            if (SetProperty(ref _importeTexto, value))
            {
                OnPropertyChanged(nameof(ImporteMinor));
                OnPropertyChanged(nameof(VueltoMinor));
            }
        }
    }

    public string RecibidoTexto
    {
        get => _recibidoTexto;
        set
        {
            if (SetProperty(ref _recibidoTexto, value))
            {
                OnPropertyChanged(nameof(RecibidoMinor));
                OnPropertyChanged(nameof(VueltoMinor));
            }
        }
    }

    public long? ImporteMinor => TryParseArs(_importeTexto, out var m) ? m : null;
    public long? RecibidoMinor => EsEfectivo && TryParseArs(_recibidoTexto, out var m) ? m : null;
    public long? VueltoMinor => ImporteMinor.HasValue && RecibidoMinor.HasValue ? RecibidoMinor.Value - ImporteMinor.Value : null;

    /// <summary>
    /// Parsea ARS con el mismo criterio que Recepción: admite 1.234,50
    /// (punto miles, coma decimal), 1234,50 y 1234.50; rechaza el ambiguo
    /// 1,234.50 y más de dos decimales sin redondeo silencioso.
    /// </summary>
    public static bool TryParseArs(string texto, out long minor)
    {
        minor = 0;
        var raw = texto.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (raw.Contains('-')) return false;
        var t = raw.Replace("ARS", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(t)) return false;
        bool tieneComa = t.Contains(',');
        bool tienePunto = t.Contains('.');
        string normalizado;
        if (tieneComa && tienePunto)
        {
            if (t.LastIndexOf(',') < t.LastIndexOf('.')) return false;
            if (t.Count(c => c == ',') != 1) return false;
            normalizado = t.Replace(".", "").Replace(',', '.');
        }
        else if (tieneComa)
        {
            if (t.Count(c => c == ',') != 1) return false;
            normalizado = t.Replace(',', '.');
        }
        else
        {
            normalizado = t;
        }

        if (!decimal.TryParse(normalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec)) return false;
        if (dec < 0) return false;
        if (dec > (decimal)long.MaxValue / 100m) return false;
        var decRedondeado = Math.Round(dec, 2, MidpointRounding.AwayFromZero);
        if (dec != decRedondeado) return false;
        minor = (long)(dec * 100m);
        return true;
    }
}

/// <summary>
/// Punto de venta: carrito en memoria + pagos + confirmación atómica.
/// El carrito nunca se persiste; cancelar solo limpia la VM (con
/// confirmación humana si tiene líneas). Tras confirmar, rota el
/// operacion_id y deja el foco en el buscador para la venta siguiente.
/// </summary>
public sealed class PosViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private readonly IDialogosVentas? _dialogos;
    private string _operacionId;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private bool _confirmacionCancelarVisible;
    private string? _ultimoNumero;
    private long? _ultimoTotal;

    public PosViewModel(ServiciosComerciales servicios, Func<string?> idSesion, IDialogosVentas? dialogos = null)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        _dialogos = dialogos;
        Filas = new ObservableCollection<FilaVentaViewModel>();
        Filas.CollectionChanged += FilasCambiadas;
        Pagos = new ObservableCollection<FilaPagoViewModel> { new FilaPagoViewModel() };
        Pagos.CollectionChanged += PagosCambiados;
        Recalcular();
        _operacionId = servicios.GeneradorIds.NuevoId();
        MotorSugerencias = new MotorSugerencias<SugerenciaVariante>((t, ct) =>
            _servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(t, 10, _idSesion(), ct));
        SeleccionarSugerenciaCommand = new AsyncRelayCommand(p => AgregarSugerenciaAsync(p as SugerenciaVariante));
        BuscarDirectaCommand = new AsyncRelayCommand(() => AgregarDirectaAsync(MotorSugerencias.Texto));
        QuitarFilaCommand = new RelayCommand(p => { if (p is FilaVentaViewModel f) { Filas.Remove(f); } });
        IncrementarCommand = new RelayCommand(p => { if (p is FilaVentaViewModel f) AjustarCantidad(f, 1); });
        DecrementarCommand = new RelayCommand(p => { if (p is FilaVentaViewModel f) AjustarCantidad(f, -1); });
        AgregarPagoCommand = new AsyncRelayCommand(AgregarPagoAsync);
        QuitarPagoCommand = new AsyncRelayCommand(QuitarPagoAsync);
        ConfirmarCommand = new AsyncRelayCommand(ConfirmarAsync);
        SolicitarCancelarCommand = new RelayCommand(_ =>
        {
            if (Filas.Count == 0) return;
            Error = null; Mensaje = null;
            ConfirmacionCancelarVisible = true;
        });
        CancelarCancelarCommand = new RelayCommand(_ => ConfirmacionCancelarVisible = false);
        ConfirmarCancelarCommand = new RelayCommand(_ => { LimpiarVenta(); ConfirmacionCancelarVisible = false; });
        NuevaVentaCommand = new RelayCommand(_ => LimpiarVenta());
        VerTicketCommand = new AsyncRelayCommand(VerTicketAsync);
    }

    /// <summary>La vista enfoca el buscador cuando se invoca.</summary>
    public event Action? SolicitarFocoBuscador;

    /// <summary>Motor del buscador asistido (modo Variante). El VM es dueño.</summary>
    public MotorSugerencias<SugerenciaVariante> MotorSugerencias { get; }

    public ObservableCollection<FilaVentaViewModel> Filas { get; }
    public ObservableCollection<FilaPagoViewModel> Pagos { get; }

    /// <summary>Medios del esquema (registros manuales, sin integraciones).</summary>
    public IReadOnlyList<string> MediosDisponibles => MediosPago.Todos;
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; set => SetProperty(ref _ocupado, value); }
    public bool ConfirmacionCancelarVisible { get => _confirmacionCancelarVisible; set => SetProperty(ref _confirmacionCancelarVisible, value); }
    public string OperacionId => _operacionId;

    /// <summary>Banner de éxito con el número estable de la última venta.</summary>
    public string? UltimoNumero { get => _ultimoNumero; set => SetProperty(ref _ultimoNumero, value); }
    public long? UltimoTotal { get => _ultimoTotal; set => SetProperty(ref _ultimoTotal, value); }

    public long Total => Filas.Sum(f => f.SubtotalMinor);
    public long Pagado => Pagos.Sum(p => p.ImporteMinor ?? 0);
    public long Restante => Total - Pagado;
    public long? VueltoTotal => Pagos.Any(p => p.VueltoMinor.HasValue) ? Pagos.Sum(p => p.VueltoMinor ?? 0) : null;

    /// <summary>
    /// Habilita Confirmar solo con carrito no vacío, pagos válidos e
    /// imputado == total (la confirmación revalida todo de todos modos).
    /// </summary>
    public bool PuedeConfirmar
    {
        get
        {
            if (Filas.Count == 0 || Total <= 0) return false;
            foreach (var p in Pagos)
            {
                if (!p.ImporteMinor.HasValue || p.ImporteMinor <= 0) return false;
                if (p.EsEfectivo && (!p.RecibidoMinor.HasValue || p.RecibidoMinor < p.ImporteMinor)) return false;
            }

            return Pagado == Total;
        }
    }

    public AsyncRelayCommand SeleccionarSugerenciaCommand { get; }
    public AsyncRelayCommand BuscarDirectaCommand { get; }
    public RelayCommand QuitarFilaCommand { get; }
    public RelayCommand IncrementarCommand { get; }
    public RelayCommand DecrementarCommand { get; }
    public AsyncRelayCommand AgregarPagoCommand { get; }
    public AsyncRelayCommand QuitarPagoCommand { get; }
    public AsyncRelayCommand ConfirmarCommand { get; }
    public RelayCommand SolicitarCancelarCommand { get; }
    public RelayCommand CancelarCancelarCommand { get; }
    public RelayCommand ConfirmarCancelarCommand { get; }
    public RelayCommand NuevaVentaCommand { get; }
    public AsyncRelayCommand VerTicketCommand { get; }

    public Task AgregarSugerenciaAsyncPublic(SugerenciaVariante? s) => AgregarSugerenciaAsync(s);
    public Task AgregarDirectaAsyncPublic(string? texto) => AgregarDirectaAsync(texto);
    public Task ConfirmarAsyncPublic() => ConfirmarAsync();
    public Task VerTicketAsyncPublic() => VerTicketAsync();

    public Task AgregarPagoAsync()
    {
        Pagos.Add(new FilaPagoViewModel());
        return Task.CompletedTask;
    }

    public Task QuitarPagoAsync()
    {
        if (Pagos.Count > 1) Pagos.RemoveAt(Pagos.Count - 1);
        return Task.CompletedTask;
    }

    private readonly HashSet<FilaVentaViewModel> _filasSuscriptas = new();
    private readonly HashSet<FilaPagoViewModel> _pagosSuscriptos = new();

    private void FilasCambiadas(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        var filas = e.NewItems is not null
            ? e.NewItems.Cast<FilaVentaViewModel>()
            : Filas;
        foreach (var f in filas)
        {
            if (_filasSuscriptas.Add(f))
            {
                f.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(FilaVentaViewModel.SubtotalMinor))
                        NotificarTotales();
                };
                f.AvisoStock = mensaje => Error = mensaje;
            }
        }

        NotificarTotales();
    }

    private void PagosCambiados(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        var pagos = e.NewItems is not null
            ? e.NewItems.Cast<FilaPagoViewModel>()
            : Pagos;
        foreach (var p in pagos)
        {
            if (_pagosSuscriptos.Add(p))
            {
                p.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(FilaPagoViewModel.ImporteMinor)
                        || args.PropertyName == nameof(FilaPagoViewModel.RecibidoMinor)
                        || args.PropertyName == nameof(FilaPagoViewModel.Medio))
                        NotificarTotales();
                };
            }
        }

        NotificarTotales();
    }

    private void Recalcular()
    {
        FilasCambiadas(this, new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        PagosCambiados(this, new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }

    private void NotificarTotales()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Pagado));
        OnPropertyChanged(nameof(Restante));
        OnPropertyChanged(nameof(VueltoTotal));
        OnPropertyChanged(nameof(PuedeConfirmar));
    }

    /// <summary>
    /// Salto desde sugerencia o código exacto: resuelve variante, precio
    /// vigente y Disponible; agrega o incrementa la fila (nunca duplica).
    /// </summary>
    private async Task AgregarSugerenciaAsync(SugerenciaVariante? sugerencia)
    {
        if (sugerencia is null || Ocupado) return;
        await AgregarVarianteAsync(sugerencia.VarianteId);
    }

    /// <summary>Enter sin sugerencia: código interno exacto agrega directo.</summary>
    private async Task AgregarDirectaAsync(string? texto)
    {
        if (Ocupado || string.IsNullOrWhiteSpace(texto)) return;
        Error = null;
        try
        {
            var variante = await _servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(texto.Trim(), _idSesion());
            if (variante is null)
            {
                Error = "Código interno no encontrado.";
                return;
            }

            await AgregarVarianteAsync(variante.Id);
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    private async Task AgregarVarianteAsync(string varianteId)
    {
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            var variante = await _servicios.ConsultasCatalogo.ObtenerVarianteAsync(varianteId, _idSesion());
            if (variante is null || !variante.Activa)
            {
                Error = "No hay unidades disponibles de esta variante.";
                return;
            }

            var fecha = DateTime.UtcNow.ToString("o");
            var ficha = await _servicios.ConsultasCatalogo.ObtenerFichaAsync(variante.ProductoId, fecha, _idSesion());
            var linea = ficha?.Variantes.FirstOrDefault(v => v.Id == varianteId);
            if (linea?.PrecioMinor is null)
            {
                Error = "Esta variante no tiene un precio vigente.";
                return;
            }

            var stock = await _servicios.ConsultasInventario.ObtenerStockVendibleAsync(varianteId, _idSesion());
            var disponible = stock?.Disponible ?? 0;
            var existente = Filas.FirstOrDefault(f => f.VarianteId == varianteId);
            var pedidas = (existente?.Cantidad ?? 0) + 1;
            if (disponible < pedidas)
            {
                Error = disponible <= 0
                    ? "No hay unidades disponibles de esta variante."
                    : $"Solo hay {disponible} unidades disponibles de esta variante.";
                return;
            }

            if (existente is not null)
            {
                existente.Cantidad = pedidas;
                existente.PrecioUnitarioMinor = linea.PrecioMinor.Value;
                OnPropertyChanged(nameof(Total));
                OnPropertyChanged(nameof(Pagado));
                OnPropertyChanged(nameof(Restante));
                OnPropertyChanged(nameof(PuedeConfirmar));
            }
            else
            {
                var producto = ficha?.Producto;
                var fila = new FilaVentaViewModel
                {
                    VarianteId = varianteId,
                    CodigoInterno = variante.CodigoInterno,
                    NombreProducto = producto?.Nombre ?? variante.CodigoInterno,
                    Talle = variante.Talle,
                    Color = variante.Color,
                    PrecioUnitarioMinor = linea.PrecioMinor.Value,
                    Disponible = disponible,
                };
                fila.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FilaVentaViewModel.SubtotalMinor))
                        NotificarTotales();
                };
                Filas.Add(fila);
            }

            MotorSugerencias.EstablecerTexto(string.Empty);
            MotorSugerencias.Limpiar();
            UltimoNumero = null;
            Mensaje = null;
            SolicitarFocoBuscador?.Invoke();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private void AjustarCantidad(FilaVentaViewModel fila, int delta)
    {
        Error = null;
        var nueva = fila.Cantidad + delta;
        if (nueva < 1)
        {
            Filas.Remove(fila);
            return;
        }

        if (fila.Disponible.HasValue && nueva > fila.Disponible.Value)
        {
            Error = $"Solo hay {fila.Disponible.Value} unidades disponibles de esta variante.";
            return;
        }

        fila.Cantidad = nueva;
    }

    private async Task ConfirmarAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null;
        if (Filas.Count == 0) { Error = "El carrito está vacío."; return; }
        var lineas = new List<NuevaLineaVenta>();
        foreach (var f in Filas)
            lineas.Add(new NuevaLineaVenta(f.VarianteId, f.Cantidad, f.PrecioUnitarioMinor));

        var pagos = new List<NuevoPago>();
        foreach (var p in Pagos)
        {
            if (!p.ImporteMinor.HasValue || p.ImporteMinor <= 0) { Error = "Revisá los importes de los pagos."; return; }
            if (p.EsEfectivo && (!p.RecibidoMinor.HasValue || p.RecibidoMinor < p.ImporteMinor))
            {
                Error = "El efectivo recibido es menor al importe correspondiente.";
                return;
            }

            pagos.Add(new NuevoPago(p.Medio, p.ImporteMinor.Value, p.EsEfectivo ? p.RecibidoMinor : null));
        }

        Ocupado = true;
        try
        {
            var resultado = await _servicios.CasoConfirmarVenta.EjecutarAsync(lineas, pagos, _operacionId, _idSesion());
            switch (resultado)
            {
                case ResultadoConfirmacionVenta.Confirmada ok:
                    UltimoNumero = ok.Numero;
                    UltimoTotal = ok.TotalMinor;
                    Mensaje = $"Venta {ok.Numero} confirmada.";
                    LimpiarVenta();
                    _operacionId = _servicios.GeneradorIds.NuevoId();
                    OnPropertyChanged(nameof(OperacionId));
                    SolicitarFocoBuscador?.Invoke();
                    break;
                case ResultadoConfirmacionVenta.PrecioCambiado cambio:
                    foreach (var c in cambio.Cambios)
                    {
                        var fila = Filas.FirstOrDefault(f => f.VarianteId == c.VarianteId);
                        if (fila is not null)
                        {
                            fila.PrecioUnitarioMinor = c.PrecioVigenteMinor;
                            OnPropertyChanged(nameof(Total));
                        }
                    }

                    var primero = cambio.Cambios[0];
                    Error = $"El precio de {primero.NombreProducto} cambió de {FormatearArs(primero.PrecioEsperadoMinor)} a {FormatearArs(primero.PrecioVigenteMinor)}. Revisá la venta antes de continuar.";
                    break;
            }
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private void LimpiarVenta()
    {
        Filas.Clear();
        Pagos.Clear();
        Pagos.Add(new FilaPagoViewModel());
        Error = null;
        ConfirmacionCancelarVisible = false;
        NotificarTotales();
    }

    private async Task VerTicketAsync()
    {
        if (string.IsNullOrWhiteSpace(UltimoNumero)) { Error = "Todavía no hay venta para mostrar."; return; }
        Error = null;
        try
        {
            var venta = await _servicios.ConsultasVentas.BuscarPorNumeroAsync(UltimoNumero, _idSesion());
            if (venta is null) { Error = "No se encontró la venta."; return; }
            if (_dialogos is null) { Error = "Vista de comprobante no disponible."; return; }
            _dialogos.MostrarTicket(venta.Id);
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    public static string FormatearArs(long minor)
    {
        var dec = (decimal)minor / 100m;
        return $"ARS {dec.ToString("N2", new CultureInfo("es-AR"))}";
    }

    private static string MensajeError(Exception ex) => ex switch
    {
        LocalInfantil.Application.Abstractions.AccesoDenegadoException
            => "No tenés permiso para vender. La Dueña puede habilitarlo desde Administración → Permisos.",
        LocalInfantil.Application.Abstractions.SesionInvalidaException
            => "Sesión inválida. Iniciá sesión nuevamente.",
        _ => ex.Message,
    };
}
