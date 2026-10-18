using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed record RecepcionConfirmadaPresentacion(string Id, string? ComprobanteRef, int Lineas, long Unidades);

public sealed class FilaRecepcionViewModel : ViewModelBase
{
    private string _sku = string.Empty;
    private string _cantidadTexto = string.Empty;
    private string _costoTexto = string.Empty;
    private string _moneda = "ARS";
    private string? _varianteId;
    private string? _detalle;
    private MotorSugerencias<SugerenciaVariante>? _motorVariantes;
    private long? _stockActual;
    private bool _resuelta;
    private bool _valida;
    private bool _cargandoStock;
    private bool _tieneDuplicado;
    private string? _errorResolucion;
    private long _versionResolucion;
    private CancellationTokenSource? _cancelacionResolucion;
    private bool _aplicandoSugerencia;
    private readonly Action<FilaRecepcionViewModel, SugerenciaVariante>? _seleccionada;

    public FilaRecepcionViewModel(Action<FilaRecepcionViewModel, SugerenciaVariante>? seleccionada = null)
    {
        _seleccionada = seleccionada;
        SeleccionarSugerenciaCommand = new RelayCommand(p =>
        {
            if (p is not SugerenciaVariante sugerencia) return;
            AplicarSugerencia(sugerencia);
            _seleccionada?.Invoke(this, sugerencia);
        });
    }

    public string Sku
    {
        get => _sku;
        set
        {
            if (!SetProperty(ref _sku, value)) return;
            if (!_aplicandoSugerencia)
            {
                CancelarResolucion();
                VarianteId = null;
                Detalle = null;
                ErrorResolucion = null;
                StockActual = null;
                Resuelta = false;
                Valida = false;
            }
            if (MotorVariantes is not null && MotorVariantes.Texto != value)
                MotorVariantes.EstablecerTexto(value);
        }
    }

    public string CantidadTexto
    {
        get => _cantidadTexto;
        set { if (SetProperty(ref _cantidadTexto, value)) { NotificarCalculos(); Valida = false; } }
    }

    public string CostoTexto
    {
        get => _costoTexto;
        set { if (SetProperty(ref _costoTexto, value)) { NotificarCalculos(); Valida = false; } }
    }

    public string Moneda
    {
        get => _moneda;
        set { if (SetProperty(ref _moneda, value)) { NotificarCalculos(); Valida = false; } }
    }

    public string? VarianteId { get => _varianteId; set => SetProperty(ref _varianteId, value); }
    public string? Detalle { get => _detalle; set => SetProperty(ref _detalle, value); }
    public string? ErrorResolucion { get => _errorResolucion; set => SetProperty(ref _errorResolucion, value); }

    public bool CargandoStock
    {
        get => _cargandoStock;
        private set => SetProperty(ref _cargandoStock, value);
    }

    public bool TieneDuplicado
    {
        get => _tieneDuplicado;
        set
        {
            if (!SetProperty(ref _tieneDuplicado, value)) return;
            OnPropertyChanged(nameof(Quedara));
            OnPropertyChanged(nameof(AvisoEntrada));
            if (value) Valida = false;
        }
    }

    public bool CantidadInvalida => !string.IsNullOrWhiteSpace(CantidadTexto)
        && (!long.TryParse(CantidadTexto.Trim(), out var cantidad) || cantidad <= 0);

    public bool CostoInvalido => !string.IsNullOrWhiteSpace(CostoTexto)
        && (Moneda.Trim().ToUpperInvariant() != "ARS" || !RecepcionViewModel.TryParseArs(CostoTexto, out _));

    public string? AvisoEntrada
    {
        get
        {
            var avisos = new List<string>();
            if (CantidadInvalida) avisos.Add("La cantidad debe ser un entero mayor que cero.");
            if (CostoInvalido) avisos.Add("Revisá el costo: se admite ARS con hasta dos decimales.");
            if (TieneDuplicado) avisos.Add("Esta variante está repetida; la recepción no admite filas duplicadas.");
            if (!string.IsNullOrWhiteSpace(ErrorResolucion)) avisos.Add(ErrorResolucion);
            return avisos.Count == 0 ? null : string.Join(" ", avisos);
        }
    }

    /// <summary>Buscador de la fila (lo provee el VM padre). Null = solo texto.</summary>
    public MotorSugerencias<SugerenciaVariante>? MotorVariantes
    {
        get => _motorVariantes;
        set
        {
            if (ReferenceEquals(_motorVariantes, value)) return;
            if (_motorVariantes is not null)
                _motorVariantes.PropertyChanged -= Motor_TextoCambiado;
            SetProperty(ref _motorVariantes, value);
            if (_motorVariantes is not null)
            {
                _motorVariantes.PropertyChanged += Motor_TextoCambiado;
                if (_motorVariantes.Texto != _sku)
                    _motorVariantes.EstablecerTexto(_sku);
            }
        }
    }

    /// <summary>Puente motor→fila: lo tipeado en el buscador es el código.</summary>
    private void Motor_TextoCambiado(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MotorSugerencias<SugerenciaVariante>.Texto)
            && _motorVariantes is not null && _motorVariantes.Texto != _sku)
        {
            Sku = _motorVariantes.Texto;
        }
    }

    /// <summary>
    /// Aplica una sugerencia con el código/id real y el detalle humano, sin
    /// validaciones nuevas (Verificar/Confirmar siguen validando todo).
    /// </summary>
    public void AplicarSugerencia(SugerenciaVariante s)
    {
        CancelarResolucion();
        _aplicandoSugerencia = true;
        try { Sku = s.CodigoInterno; }
        finally { _aplicandoSugerencia = false; }
        VarianteId = s.VarianteId;
        Detalle = $"{s.CodigoInterno} — {s.DetalleTalleColor}";
        ErrorResolucion = null;
        StockActual = null;
        Resuelta = false;
        Valida = false;
    }

    /// <summary>
    /// Lectura derivada: las dos propiedades calculadas son previews locales;
    /// ninguna escribe movimientos ni sustituye la revalidación al confirmar.
    /// </summary>
    public long? StockActual
    {
        get => _stockActual;
        set { if (SetProperty(ref _stockActual, value)) OnPropertyChanged(nameof(Quedara)); }
    }

    public long? Quedara
    {
        get
        {
            if (TieneDuplicado || !StockActual.HasValue
                || !long.TryParse(CantidadTexto.Trim(), out var cantidad) || cantidad <= 0) return null;
            try { return checked(StockActual.Value + cantidad); }
            catch (OverflowException) { return null; }
        }
    }

    public long? SubtotalMinor
    {
        get
        {
            if (!long.TryParse(CantidadTexto.Trim(), out var cantidad) || cantidad <= 0
                || string.IsNullOrWhiteSpace(CostoTexto) || Moneda.Trim().ToUpperInvariant() != "ARS"
                || !RecepcionViewModel.TryParseArs(CostoTexto, out var costo)) return null;
            try { return checked(cantidad * costo); }
            catch (OverflowException) { return null; }
        }
    }

    public bool Resuelta { get => _resuelta; set => SetProperty(ref _resuelta, value); }
    public bool Valida { get => _valida; set => SetProperty(ref _valida, value); }

    public RelayCommand SeleccionarSugerenciaCommand { get; }
    public AsyncRelayCommand? BuscarDirectaCommand { get; set; }

    internal (long Version, CancellationToken Token) ComenzarResolucion()
    {
        CancelarResolucion();
        _cancelacionResolucion = new CancellationTokenSource();
        CargandoStock = true;
        ErrorResolucion = null;
        return (_versionResolucion, _cancelacionResolucion.Token);
    }

    internal bool ResolucionVigente(long version, string codigo)
        => version == _versionResolucion && string.Equals(Sku.Trim(), codigo, StringComparison.OrdinalIgnoreCase);

    internal void FinalizarResolucion(long version)
    {
        if (version != _versionResolucion) return;
        CargandoStock = false;
        _cancelacionResolucion?.Dispose();
        _cancelacionResolucion = null;
    }

    internal void CancelarResolucion()
    {
        _versionResolucion++;
        try { _cancelacionResolucion?.Cancel(); } catch (ObjectDisposedException) { }
        _cancelacionResolucion?.Dispose();
        _cancelacionResolucion = null;
        CargandoStock = false;
    }

    private void NotificarCalculos()
    {
        OnPropertyChanged(nameof(Quedara));
        OnPropertyChanged(nameof(SubtotalMinor));
        OnPropertyChanged(nameof(CantidadInvalida));
        OnPropertyChanged(nameof(CostoInvalido));
        OnPropertyChanged(nameof(AvisoEntrada));
    }
}

public sealed class RecepcionViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private ProveedorRegistrado? _proveedorSeleccionado;
    private string? _comprobanteRef;
    private string _operacionId;
    private IReadOnlyList<LineaRecepcion>? _ultimasLineas;
    private string? _ultimoProveedorId;
    private string? _ultimoComprobante;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private bool _resumenListo;
    private RecepcionConfirmadaPresentacion? _ultimaRecepcionConfirmada;
    private bool _confirmacionPendienteDeLimpiar;
    private readonly Dictionary<FilaRecepcionViewModel, System.ComponentModel.PropertyChangedEventHandler> _handlersFilas = new();

    public RecepcionViewModel(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        Proveedores = new ObservableCollection<ProveedorRegistrado>();
        _operacionId = servicios.GeneradorIds.NuevoId();
        ConfirmarCommand = new AsyncRelayCommand(ConfirmarAsync);
        AgregarFilaCommand = new AsyncRelayCommand(AgregarFilaAsync);
        QuitarFilaCommand = new AsyncRelayCommand(QuitarFilaAsync);
        RefrescarProveedoresCommand = new AsyncRelayCommand(CargarProveedoresAsync);
        VerificarCommand = new AsyncRelayCommand(VerificarAsync);
        Filas = new ObservableCollection<FilaRecepcionViewModel> { CrearFila(), CrearFila() };
        foreach (var fila in Filas) SuscribirFila(fila);
        Filas.CollectionChanged += Filas_Cambiadas;
    }

    /// <summary>Fábrica de filas con buscador asistido ya cableado.</summary>
    private FilaRecepcionViewModel CrearFila()
    {
        var fila = new FilaRecepcionViewModel(IniciarCargaStockSeleccionado)
        {
            MotorVariantes = new MotorSugerencias<SugerenciaVariante>((t, ct) =>
            {
                var sesion = _idSesion();
                // Microsoft.Data.Sqlite completa sus APIs "Async" de forma
                // síncrona. Evitar que la consulta del typeahead ocupe el
                // dispatcher de WPF.
                return Task.Run(() => _servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(t, 10, sesion, ct), ct);
            }),
            BuscarDirectaCommand = VerificarCommand,
        };
        return fila;
    }

    private void ActualizarDuplicados()
    {
        if (Filas is null) return;
        var duplicadas = Filas.Where(f => !string.IsNullOrWhiteSpace(f.VarianteId))
            .GroupBy(f => f.VarianteId!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var fila in Filas)
            fila.TieneDuplicado = fila.VarianteId is not null && duplicadas.Contains(fila.VarianteId);
    }

    private void Filas_Cambiadas(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (FilaRecepcionViewModel fila in e.OldItems)
            {
                fila.PropertyChanged -= _handlersFilas[fila];
                _handlersFilas.Remove(fila);
                fila.CancelarResolucion();
            }
        }
        if (e.NewItems is not null)
            foreach (FilaRecepcionViewModel fila in e.NewItems)
                SuscribirFila(fila);

        ResumenListo = false;
        ActualizarDuplicados();
        NotificarResumen();
    }

    private void SuscribirFila(FilaRecepcionViewModel fila)
    {
        System.ComponentModel.PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName is nameof(FilaRecepcionViewModel.Sku)
                or nameof(FilaRecepcionViewModel.VarianteId)
                or nameof(FilaRecepcionViewModel.CantidadTexto)
                or nameof(FilaRecepcionViewModel.CostoTexto)
                or nameof(FilaRecepcionViewModel.Moneda)
                or nameof(FilaRecepcionViewModel.TieneDuplicado)
                or nameof(FilaRecepcionViewModel.ErrorResolucion)
                or nameof(FilaRecepcionViewModel.StockActual))
            {
                ResumenListo = false;
                if (e.PropertyName is nameof(FilaRecepcionViewModel.Sku) or nameof(FilaRecepcionViewModel.VarianteId))
                    ActualizarDuplicados();
                NotificarResumen();
            }
        };
        _handlersFilas.Add(fila, handler);
        fila.PropertyChanged += handler;
    }

    private void IniciarCargaStockSeleccionado(FilaRecepcionViewModel fila, SugerenciaVariante sugerencia)
    {
        ResumenListo = false;
        ActualizarDuplicados();
        _ = CargarStockSeleccionadoAsync(fila, sugerencia.CodigoInterno);
    }

    private async Task CargarStockSeleccionadoAsync(FilaRecepcionViewModel fila, string codigo)
    {
        var (version, cancellationToken) = fila.ComenzarResolucion();
        try
        {
            var sesion = _idSesion();
            var variante = await Task.Run(
                () => _servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(codigo, sesion, cancellationToken),
                cancellationToken);
            if (!fila.ResolucionVigente(version, codigo)) return;
            if (variante is null || !variante.Activa)
            {
                fila.VarianteId = null;
                fila.StockActual = null;
                fila.Resuelta = false;
                fila.ErrorResolucion = variante is null
                    ? "Código interno no encontrado."
                    : "La variante está inactiva y no puede recibirse.";
                return;
            }

            var ficha = await Task.Run(
                () => _servicios.ConsultasCatalogo.ObtenerFichaAsync(variante.ProductoId, DateTime.UtcNow.ToString("o"), sesion, cancellationToken),
                cancellationToken);
            if (!fila.ResolucionVigente(version, codigo)) return;

            fila.VarianteId = variante.Id;
            fila.Detalle = $"{variante.CodigoInterno} — {variante.Talle ?? "sin talle"}/{variante.Color ?? "sin color"}";
            fila.StockActual = ficha?.Variantes.FirstOrDefault(v => v.Id == variante.Id)?.Existencias;
            fila.Resuelta = true;
            ActualizarDuplicados();
        }
        catch (OperationCanceledException)
        {
            // Una edición o selección más nueva invalida este resultado.
        }
        catch (Exception ex)
        {
            if (!fila.ResolucionVigente(version, codigo)) return;
            fila.VarianteId = null;
            fila.StockActual = null;
            fila.Resuelta = false;
            fila.ErrorResolucion = MensajeError(ex);
        }
        finally
        {
            fila.FinalizarResolucion(version);
        }
    }

    public ObservableCollection<ProveedorRegistrado> Proveedores { get; }
    public ObservableCollection<FilaRecepcionViewModel> Filas { get; }
    public ProveedorRegistrado? ProveedorSeleccionado
    {
        get => _proveedorSeleccionado;
        set
        {
            if (SetProperty(ref _proveedorSeleccionado, value)) NotificarResumen();
        }
    }

    /// <summary>Resumen lateral: solo filas verificadas como válidas.</summary>
    public bool ResumenListo
    {
        get => _resumenListo;
        private set
        {
            if (SetProperty(ref _resumenListo, value))
            {
                OnPropertyChanged(nameof(CantidadLineas));
                OnPropertyChanged(nameof(UnidadesSumar));
                OnPropertyChanged(nameof(TotalEstimado));
                OnPropertyChanged(nameof(ConfirmarTexto));
                OnPropertyChanged(nameof(PuedeConfirmar));
            }
        }
    }

    public int CantidadLineas => Filas.Count(FilaAptaParaResumen);
    public long UnidadesSumar => Filas.Where(FilaAptaParaResumen)
        .Sum(f => long.TryParse(f.CantidadTexto.Trim(), out var cantidad) ? cantidad : 0);
    public bool TieneFilasIncompletas => Filas.Any(FilaConEntrada) && Filas.Any(f => FilaConEntrada(f) && !FilaAptaParaResumen(f));
    public long CostosConocidosMinor => Filas.Where(FilaAptaParaResumen).Sum(f => f.SubtotalMinor ?? 0);

    /// <summary>Solo informa un total exacto si todas las filas ingresadas están completas y tienen costo.</summary>
    public long? TotalEstimado => CantidadLineas == 0 || TieneFilasIncompletas
        || Filas.Where(FilaAptaParaResumen).Any(f => !f.SubtotalMinor.HasValue)
        ? null
        : CostosConocidosMinor;

    public string CostoEstimadoTexto
    {
        get
        {
            if (CantidadLineas == 0) return TieneFilasIncompletas ? "Completá las filas para estimar el costo." : "Sin importes cargados";
            if (!TieneFilasIncompletas && !Filas.Where(FilaAptaParaResumen).Any(f => !f.SubtotalMinor.HasValue))
                return PosViewModel.FormatearArs(CostosConocidosMinor);
            return CostosConocidosMinor > 0
                ? $"{PosViewModel.FormatearArs(CostosConocidosMinor)} conocidos · parcial"
                : "Parcial · hay costos sin informar";
        }
    }

    public string ResumenEstadoTexto => TieneFilasIncompletas
        ? "Completá o corregí las filas incompletas; el resumen incluye solo variantes y cantidades válidas."
        : Filas.Where(FilaAptaParaResumen).Any(f => !f.SubtotalMinor.HasValue)
            ? "El costo mostrado es parcial: hay costos unitarios sin informar."
            : ResumenListo ? string.Empty : "Los datos se calculan al editar; Verificar filas solo revalida antes de confirmar.";

    public string ConfirmarTexto => CantidadLineas > 0
        ? $"Confirmar y sumar {UnidadesSumar} unidades"
        : "Confirmar recepción";

    public bool PuedeConfirmar => !Ocupado && !_confirmacionPendienteDeLimpiar
        && ProveedorSeleccionado is not null && CantidadLineas > 0 && !TieneFilasIncompletas;

    private static bool FilaConEntrada(FilaRecepcionViewModel fila)
        => !string.IsNullOrWhiteSpace(fila.Sku) || !string.IsNullOrWhiteSpace(fila.CantidadTexto)
            || !string.IsNullOrWhiteSpace(fila.CostoTexto);

    private static bool FilaAptaParaResumen(FilaRecepcionViewModel fila)
        => !string.IsNullOrWhiteSpace(fila.VarianteId)
            && !fila.TieneDuplicado
            && string.IsNullOrWhiteSpace(fila.ErrorResolucion)
            && long.TryParse(fila.CantidadTexto.Trim(), out var cantidad) && cantidad > 0
            && !fila.CostoInvalido
            && string.Equals(fila.Moneda.Trim(), "ARS", StringComparison.OrdinalIgnoreCase);

    private void NotificarResumen()
    {
        OnPropertyChanged(nameof(CantidadLineas));
        OnPropertyChanged(nameof(UnidadesSumar));
        OnPropertyChanged(nameof(TieneFilasIncompletas));
        OnPropertyChanged(nameof(CostosConocidosMinor));
        OnPropertyChanged(nameof(TotalEstimado));
        OnPropertyChanged(nameof(CostoEstimadoTexto));
        OnPropertyChanged(nameof(ResumenEstadoTexto));
        OnPropertyChanged(nameof(ConfirmarTexto));
        OnPropertyChanged(nameof(PuedeConfirmar));
    }
    public string? ComprobanteRef { get => _comprobanteRef; set => SetProperty(ref _comprobanteRef, value); }
    public string OperacionId { get => _operacionId; set => SetProperty(ref _operacionId, value); }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado
    {
        get => _ocupado;
        set { if (SetProperty(ref _ocupado, value)) OnPropertyChanged(nameof(PuedeConfirmar)); }
    }

    public AsyncRelayCommand ConfirmarCommand { get; }
    public AsyncRelayCommand AgregarFilaCommand { get; }
    public AsyncRelayCommand QuitarFilaCommand { get; }
    public AsyncRelayCommand RefrescarProveedoresCommand { get; }
    public AsyncRelayCommand VerificarCommand { get; }
    public RecepcionConfirmadaPresentacion? UltimaRecepcionConfirmada
    {
        get => _ultimaRecepcionConfirmada;
        private set
        {
            if (!SetProperty(ref _ultimaRecepcionConfirmada, value)) return;
            OnPropertyChanged(nameof(EtiquetasRecepcionDisponibles));
        }
    }
    public bool EtiquetasRecepcionDisponibles => UltimaRecepcionConfirmada is not null;
    public EtiquetasViewModel CrearEtiquetasRecepcionViewModel()
        => UltimaRecepcionConfirmada is { } ultima
            ? EtiquetasViewModel.ParaRecepcionConfirmada(_servicios, _idSesion, ultima.Id)
            : throw new InvalidOperationException("Todavía no hay una recepción confirmada para imprimir etiquetas.");

    public Task VerificarAsyncPublic() => VerificarAsync();

    /// <summary>
    /// Revalida cada fila con consultas existentes. Los previews se calculan
    /// localmente; la confirmación vuelve a resolver y validar todo.
    /// </summary>
    public async Task VerificarAsync()
    {
        if (Ocupado) return;
        Error = null;
        Ocupado = true;
        try
        {
            var fecha = DateTime.UtcNow.ToString("o");
            foreach (var f in Filas)
            {
                f.Resuelta = false;
                f.Valida = false;
                f.StockActual = null;
                f.VarianteId = null;
                if (string.IsNullOrWhiteSpace(f.Sku)) { f.CancelarResolucion(); continue; }
                var codigo = f.Sku.Trim();
                var (version, cancellationToken) = f.ComenzarResolucion();
                try
                {
                    var sesion = _idSesion();
                    // Las lecturas de SQLite pueden bloquear pese a exponer
                    // métodos Task; ejecutarlas fuera del hilo de interfaz.
                    var vari = await Task.Run(
                        () => _servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(codigo, sesion, cancellationToken), cancellationToken);
                    if (!f.ResolucionVigente(version, codigo)) continue;
                    if (vari is null || !vari.Activa)
                    {
                        f.Detalle = vari is null ? "Código interno no encontrado." : "La variante está inactiva y no puede recibirse.";
                        f.ErrorResolucion = f.Detalle;
                        continue;
                    }
                    f.VarianteId = vari.Id;
                    f.Detalle = $"{vari.CodigoInterno} — {vari.Talle ?? "sin talle"}/{vari.Color ?? "sin color"}";
                    var ficha = await Task.Run(
                        () => _servicios.ConsultasCatalogo.ObtenerFichaAsync(vari.ProductoId, fecha, sesion, cancellationToken), cancellationToken);
                    if (!f.ResolucionVigente(version, codigo)) continue;
                    var existencia = ficha?.Variantes.FirstOrDefault(v => v.Id == vari.Id)?.Existencias;
                    f.StockActual = existencia;
                    if (long.TryParse(f.CantidadTexto.Trim(), out var cant) && cant > 0)
                    {
                        f.Valida = true;
                    }

                    f.Resuelta = true;
                }
                catch (OperationCanceledException)
                {
                    // Una edición posterior de la fila descarta esta verificación.
                }
                catch (Exception ex)
                {
                    if (f.ResolucionVigente(version, codigo))
                    {
                        f.Detalle = MensajeError(ex);
                        f.ErrorResolucion = f.Detalle;
                    }
                }
                finally
                {
                    f.FinalizarResolucion(version);
                }
            }

            ActualizarDuplicados();
            ResumenListo = Filas.Any(f => f.Valida);
            if (!ResumenListo) Mensaje = "Sin filas válidas para resumir.";
        }
        finally { Ocupado = false; }
    }

    public async Task CargarProveedoresAsync()
    {
        Error = null;
        try
        {
            var lista = await _servicios.ConsultasProveedores.ListarAsync(_idSesion(), true);
            Proveedores.Clear();
            foreach (var p in lista) Proveedores.Add(p);
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        await VerificarPendienteAsync();
    }

    /// <summary>Actualiza el catálogo de proveedores sin recuperar ni alterar el borrador de recepción.</summary>
    public async Task RefrescarProveedoresSinTocarRecepcionAsync()
    {
        if (Ocupado) return;
        var proveedorId = ProveedorSeleccionado?.Id;
        try
        {
            var lista = await _servicios.ConsultasProveedores.ListarAsync(_idSesion(), true);
            Proveedores.Clear();
            foreach (var proveedor in lista) Proveedores.Add(proveedor);
            ProveedorSeleccionado = proveedorId is null
                ? null
                : Proveedores.FirstOrDefault(proveedor => proveedor.Id == proveedorId);
            Error = null;
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    public async Task VerificarPendienteAsync()
    {
        var yaOcupado = Ocupado;
        if (!yaOcupado) Ocupado = true;
        try
        {
            RecepcionPendienteDto? pendiente;
            try
            {
                pendiente = AlmacenRecepcionPendiente.Cargar(_servicios.RutaBaseDatos);
            }
            catch (Exception ex)
            {
                Error = $"Archivo pendiente corrupto o ilegible: {MensajeError(ex)}. Bloqueado hasta reconciliar o eliminar manualmente el archivo pendiente.";
                return;
            }
            if (pendiente is null) return;
            bool existe;
            try
            {
                existe = await _servicios.ConsultasRecepciones.ExisteOperacionAsync(pendiente.OperacionId, _idSesion());
            }
            catch (Exception ex)
            {
                Error = $"Pendiente incierto (operacion_id {pendiente.OperacionId}) no se pudo verificar: {MensajeError(ex)}. Conservando para reintento idempotente.";
                _operacionId = pendiente.OperacionId;
                OnPropertyChanged(nameof(OperacionId));
                _ultimasLineas = pendiente.Lineas;
                _ultimoProveedorId = pendiente.ProveedorId;
                _ultimoComprobante = pendiente.ComprobanteRef;
                return;
            }
            if (existe)
            {
                RecepcionRegistrada? registrada;
                IReadOnlyList<LineaRecepcion> lineasPersistidas;
                try
                {
                    registrada = await _servicios.ConsultasRecepciones.ObtenerPorOperacionAsync(pendiente.OperacionId, _idSesion());
                    if (registrada is null)
                        throw new InvalidOperationException("La operación figura registrada, pero no se encontró su recepción.");
                    lineasPersistidas = await _servicios.ConsultasRecepciones.ListarLineasAsync(registrada.Id, _idSesion());
                }
                catch (Exception ex)
                {
                    Error = $"No se pudo recuperar la recepción confirmada del pendiente: {MensajeError(ex)}. Se conserva y no se habilita otra confirmación.";
                    _confirmacionPendienteDeLimpiar = true;
                    OnPropertyChanged(nameof(PuedeConfirmar));
                    return;
                }
                UltimaRecepcionConfirmada = new RecepcionConfirmadaPresentacion(
                    registrada.Id, registrada.ComprobanteRef, lineasPersistidas.Count, lineasPersistidas.Sum(linea => linea.Cantidad));
                Mensaje = $"La recepción anterior ya quedó registrada. No se duplicó. Se sumaron {UltimaRecepcionConfirmada.Unidades} unidades.";
                try
                {
                    AlmacenRecepcionPendiente.Eliminar(_servicios.RutaBaseDatos);
                    _operacionId = _servicios.GeneradorIds.NuevoId();
                    OnPropertyChanged(nameof(OperacionId));
                    _ultimasLineas = null;
                    _ultimoProveedorId = null;
                    _ultimoComprobante = null;
                    _confirmacionPendienteDeLimpiar = false;
                    OnPropertyChanged(nameof(PuedeConfirmar));
                    ReiniciarBorradorTrasConfirmacion();
                }
                catch (Exception ex)
                {
                    _confirmacionPendienteDeLimpiar = true;
                    Error = $"La recepción ya quedó registrada. No se pudo eliminar el pendiente local: {MensajeError(ex)}. Reiniciá la aplicación para reconciliar antes de continuar.";
                    OnPropertyChanged(nameof(PuedeConfirmar));
                }
            }
            else
            {
                Mensaje = $"Pendiente incierto recuperado tras reinicio (operacion_id {pendiente.OperacionId}). Reintentá exactamente mismo proveedor/comprobante/líneas con mismo id.";
                _operacionId = pendiente.OperacionId;
                OnPropertyChanged(nameof(OperacionId));
                _ultimasLineas = pendiente.Lineas;
                _ultimoProveedorId = pendiente.ProveedorId;
                _ultimoComprobante = pendiente.ComprobanteRef;
                Filas.Clear();
                foreach (var l in pendiente.Lineas)
                {
                    // El id interno nunca se presenta como código: se recupera el código
                    // auténtico por variante con sesión vigente.
                    LocalInfantil.Application.Contracts.VarianteRegistrada? variante;
                    try
                    {
                        variante = await _servicios.ConsultasCatalogo.ObtenerVarianteAsync(l.VarianteId, _idSesion());
                    }
                    catch (Exception ex)
                    {
                        Error = $"No se pudo recuperar la variante '{l.VarianteId}' del pendiente: {MensajeError(ex)}. Pendiente conservado, reintentá la recuperación.";
                        return;
                    }
                    if (variante is null)
                    {
                        Error = $"La variante '{l.VarianteId}' del pendiente ya no existe. Pendiente conservado para revisión manual; no se reconstruye el formulario.";
                        return;
                    }
                    var fila = CrearFila();
                    fila.Sku = variante.CodigoInterno;
                    fila.CantidadTexto = l.Cantidad.ToString();
                    fila.CostoTexto = l.CostoUnitarioMinor.HasValue ? (l.CostoUnitarioMinor.Value / 100m).ToString("F2", CultureInfo.InvariantCulture) : string.Empty;
                    fila.Moneda = l.Moneda ?? "ARS";
                    fila.VarianteId = variante.Id;
                    fila.Detalle = $"{variante.CodigoInterno} — {variante.Talle ?? "sin talle"}/{variante.Color ?? "sin color"}";
                    Filas.Add(fila);
                }
                if (Filas.Count == 0) Filas.Add(CrearFila());
                var prov = Proveedores.FirstOrDefault(p => p.Id == pendiente.ProveedorId);
                if (prov is not null) ProveedorSeleccionado = prov;
                ComprobanteRef = pendiente.ComprobanteRef;
            }
        }
        finally { if (!yaOcupado) Ocupado = false; }
    }

    public Task ConfirmarAsyncPublic() => ConfirmarAsync();
    private Task AgregarFilaAsync() { Filas.Add(CrearFila()); return Task.CompletedTask; }
    private Task QuitarFilaAsync() { if (Filas.Count > 1) Filas.RemoveAt(Filas.Count - 1); return Task.CompletedTask; }

    public async Task ConfirmarAsync()
    {
        if (Ocupado) return;
        Ocupado = true;
        try { await ConfirmarConBloqueoAsync(); }
        finally { Ocupado = false; }
    }

    private async Task ConfirmarConBloqueoAsync()
    {
        Error = null; Mensaje = null;
        if (_confirmacionPendienteDeLimpiar)
        {
            Error = "La recepción anterior ya fue confirmada, pero no se pudo cerrar su pendiente local. Reiniciá la aplicación para reconciliarla antes de confirmar otra.";
            return;
        }
        if (ProveedorSeleccionado is null) { Error = "Elegí un proveedor."; return; }
        var lineas = new List<LineaRecepcion>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in Filas)
        {
            if (string.IsNullOrWhiteSpace(f.Sku)) continue;
            if (!long.TryParse(f.CantidadTexto.Trim(), out var cant) || cant <= 0) { Error = $"Cantidad inválida para código {f.Sku}. Entero >0."; return; }
            long? costoMinor = null;
            string? moneda = null;
            if (!string.IsNullOrWhiteSpace(f.CostoTexto))
            {
                if (!TryParseArs(f.CostoTexto, out var cm)) { Error = $"Costo inválido para {f.Sku}. Ej: 1234,50"; return; }
                costoMinor = cm;
                moneda = string.IsNullOrWhiteSpace(f.Moneda) ? "ARS" : f.Moneda.Trim().ToUpperInvariant();
                if (moneda != "ARS") { Error = $"Moneda no admitida para {f.Sku}: {moneda}. Solo ARS."; return; }
            }
            else
            {
                // costo desconocido -> ambos null, moneda vacía se ignora
                if (!string.IsNullOrWhiteSpace(f.Moneda) && f.Moneda.Trim().ToUpperInvariant() != "ARS")
                { Error = $"Moneda sin costo para {f.Sku}."; return; }
            }
            try
            {
                var vari = await _servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(f.Sku.Trim(), _idSesion());
                if (vari is null) { Error = $"Código interno no encontrado: {f.Sku}."; return; }
                if (!vistos.Add(vari.Id)) { Error = $"Variante repetida: {f.Sku}."; return; }
                f.VarianteId = vari.Id;
                f.Detalle = $"{vari.CodigoInterno} — {vari.Talle ?? "sin talle"}/{vari.Color ?? "sin color"}";
                lineas.Add(new LineaRecepcion(vari.Id, cant, costoMinor, moneda));
            }
            catch (Exception ex) { Error = MensajeError(ex); return; }
        }
        if (lineas.Count == 0) { Error = "Agregá al menos una fila válida."; return; }

        var comprobante = string.IsNullOrWhiteSpace(ComprobanteRef) ? null : ComprobanteRef!.Trim();

        // Reconciliación: si hay operación previa incierta, verificar antes de nueva
        if (_ultimasLineas is not null)
        {
            bool existePrevia;
            try
            {
                existePrevia = await _servicios.ConsultasRecepciones.ExisteOperacionAsync(_operacionId, _idSesion());
            }
            catch (Exception ex)
            {
                Error = $"No se pudo verificar la recepción incierta (operacion_id {_operacionId}): {MensajeError(ex)}. Conservando estado incierto, reintentá de forma idempotente.";
                return;
            }
            if (existePrevia)
            {
                RecepcionRegistrada? registrada;
                IReadOnlyList<LineaRecepcion> persistidas;
                try
                {
                    registrada = await _servicios.ConsultasRecepciones.ObtenerPorOperacionAsync(_operacionId, _idSesion());
                    if (registrada is null)
                    {
                        Error = "La operación figura existente, pero no se pudo recuperar su recepción. Se conserva el borrador y el mismo identificador para reconciliar.";
                        return;
                    }
                    persistidas = await _servicios.ConsultasRecepciones.ListarLineasAsync(registrada.Id, _idSesion());
                }
                catch (Exception ex)
                {
                    Error = $"No se pudo recuperar la recepción incierta: {MensajeError(ex)}. Se conserva el mismo identificador para reconciliar.";
                    return;
                }

                if (registrada.ProveedorId != _ultimoProveedorId
                    || !string.Equals(registrada.ComprobanteRef, _ultimoComprobante, StringComparison.Ordinal)
                    || !LineasIgualesSinOrden(_ultimasLineas, persistidas))
                {
                    Error = "La operación existente no coincide con el pendiente local. No se borra el pendiente ni se permite reutilizar su identificador.";
                    return;
                }

                var mismoBorrador = LineasIguales(_ultimasLineas, lineas)
                    && _ultimoProveedorId == ProveedorSeleccionado.Id
                    && _ultimoComprobante == comprobante;
                try
                {
                    AlmacenRecepcionPendiente.Eliminar(_servicios.RutaBaseDatos);
                }
                catch (Exception exElim)
                {
                    UltimaRecepcionConfirmada = new RecepcionConfirmadaPresentacion(
                        registrada.Id, registrada.ComprobanteRef, persistidas.Count, persistidas.Sum(linea => linea.Cantidad));
                    Mensaje = $"Recepción registrada correctamente. Se sumaron {UltimaRecepcionConfirmada.Unidades} unidades.";
                    _confirmacionPendienteDeLimpiar = true;
                    if (mismoBorrador) ReiniciarBorradorTrasConfirmacion();
                    Error = $"La recepción ya quedó registrada. No se pudo eliminar el pendiente local: {MensajeError(exElim)}. Reiniciá la aplicación para reconciliar antes de continuar.";
                    return;
                }
                UltimaRecepcionConfirmada = new RecepcionConfirmadaPresentacion(
                    registrada.Id, registrada.ComprobanteRef, persistidas.Count, persistidas.Sum(linea => linea.Cantidad));
                Mensaje = mismoBorrador
                    ? $"Recepción registrada correctamente. Se sumaron {UltimaRecepcionConfirmada.Unidades} unidades."
                    : $"La recepción anterior ya quedó registrada. El borrador actual no se confirmó y requiere nueva confirmación explícita.";
                _servicios.NotificadorCambios.Publicar(this, AreaDatos.Recepciones, AreaDatos.Recepciones, AreaDatos.Stock);
                _ultimasLineas = null;
                _ultimoProveedorId = null;
                _ultimoComprobante = null;
                _operacionId = _servicios.GeneradorIds.NuevoId();
                OnPropertyChanged(nameof(OperacionId));
                _confirmacionPendienteDeLimpiar = false;
                OnPropertyChanged(nameof(PuedeConfirmar));
                if (mismoBorrador) ReiniciarBorradorTrasConfirmacion();
                return;
            }
            // Si no existe previa, se conserva idempotencia normal abajo (mismo contenido mantiene id, distinto genera nuevo)
        }

        // Idempotencia: si contenido cambió respecto al último intento, rotar id
        if (_ultimasLineas is not null && (!LineasIguales(_ultimasLineas, lineas) || _ultimoProveedorId != ProveedorSeleccionado.Id || _ultimoComprobante != comprobante))
        {
            _operacionId = _servicios.GeneradorIds.NuevoId();
            OnPropertyChanged(nameof(OperacionId));
            _ultimasLineas = null;
            _ultimoProveedorId = null;
            _ultimoComprobante = null;
        }

        // Persistencia durable: guardar pendiente ANTES de enviar; si falla, no ejecutar
        try
        {
            AlmacenRecepcionPendiente.Guardar(_servicios.RutaBaseDatos, _operacionId, ProveedorSeleccionado.Id, comprobante, lineas);
        }
        catch (Exception ex)
        {
            Error = $"No se pudo guardar de forma fiable el estado pendiente antes de enviar: {MensajeError(ex)}. No se ejecutó la recepción.";
            return;
        }

        try
        {
            var id = await _servicios.CasoConfirmarRecepcion.EjecutarAsync(ProveedorSeleccionado.Id, lineas, _operacionId, comprobante, _idSesion());
            UltimaRecepcionConfirmada = new RecepcionConfirmadaPresentacion(
                id, comprobante, lineas.Count, lineas.Sum(linea => linea.Cantidad));
            Mensaje = $"Recepción registrada correctamente. Se sumaron {UltimaRecepcionConfirmada.Unidades} unidades.";
            _servicios.NotificadorCambios.Publicar(this, AreaDatos.Recepciones, AreaDatos.Recepciones, AreaDatos.Stock);
            try
            {
                AlmacenRecepcionPendiente.Eliminar(_servicios.RutaBaseDatos);
                _operacionId = _servicios.GeneradorIds.NuevoId();
                OnPropertyChanged(nameof(OperacionId));
                _ultimasLineas = null;
                _ultimoProveedorId = null;
                _ultimoComprobante = null;
                _confirmacionPendienteDeLimpiar = false;
            }
            catch (Exception exLimpieza)
            {
                // La transacción ya retornó confirmada. No tratar un fallo de
                // limpieza local como fallo de negocio ni permitir otra alta.
                _confirmacionPendienteDeLimpiar = true;
                _ultimasLineas = lineas;
                _ultimoProveedorId = ProveedorSeleccionado.Id;
                _ultimoComprobante = comprobante;
                Error = $"La recepción quedó confirmada. No se pudo eliminar el pendiente local: {MensajeError(exLimpieza)}. Reiniciá la aplicación para reconciliar antes de continuar.";
            }
            ReiniciarBorradorTrasConfirmacion();
        }
        catch (Exception ex)
        {
            _ultimasLineas = lineas;
            _ultimoProveedorId = ProveedorSeleccionado.Id;
            _ultimoComprobante = comprobante;
            // Pendiente ya guardado durablemente, se conserva para reintento tras reinicio
            var msg = ex is InvalidOperationException && ex.Message.Contains("ya fue registrada")
                ? "Esta recepción ya existe con ese operacion_id."
                : MensajeError(ex) + $" (sin éxito parcial; reintentá con mismo operacion_id y mismas filas/proveedor/comprobante: {_operacionId})";
            Error = msg;
        }
    }

    private void ReiniciarBorradorTrasConfirmacion()
    {
        foreach (var fila in Filas) fila.CancelarResolucion();
        Filas.Clear();
        ProveedorSeleccionado = null;
        ComprobanteRef = string.Empty;
        ResumenListo = false;
        NotificarResumen();
    }

    /// <summary>Referencia humana: comprobante si existe; nunca id técnico.</summary>
    internal static string ReferenciaHumana(string? comprobante, string id)
        => string.IsNullOrWhiteSpace(comprobante) ? "sin comprobante" : comprobante.Trim();

    private static bool LineasIguales(IReadOnlyList<LineaRecepcion> a, IReadOnlyList<LineaRecepcion> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].VarianteId != b[i].VarianteId || a[i].Cantidad != b[i].Cantidad || a[i].CostoUnitarioMinor != b[i].CostoUnitarioMinor || a[i].Moneda != b[i].Moneda) return false;
        return true;
    }

    private static bool LineasIgualesSinOrden(IReadOnlyList<LineaRecepcion>? a, IReadOnlyList<LineaRecepcion> b)
    {
        if (a is null || a.Count != b.Count) return false;
        var ordenadasA = a.OrderBy(linea => linea.VarianteId, StringComparer.Ordinal).ToArray();
        var ordenadasB = b.OrderBy(linea => linea.VarianteId, StringComparer.Ordinal).ToArray();
        return LineasIguales(ordenadasA, ordenadasB);
    }
    internal static bool TryParseArs(string texto, out long minor)
    {
        minor = 0;
        var raw = texto.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return false;
        // Rechazar negativo
        if (raw.Contains('-')) return false;
        var t = raw.Replace("ARS", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(t)) return false;
        // Manejo de separadores: admitir 1.234,50 (punto miles, coma decimal), 1234,50 y 1234.50
        // Rechazar ambiguo como 1,234.50 (coma miles, punto decimal)
        bool tieneComa = t.Contains(',');
        bool tienePunto = t.Contains('.');
        string normalizado;
        if (tieneComa && tienePunto)
        {
            // ',' debe ser decimal y estar después del último '.'
            if (t.LastIndexOf(',') < t.LastIndexOf('.')) return false;
            // No debe haber más de una coma
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
        if (!decimal.TryParse(normalizado, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var dec)) return false;
        if (dec < 0) return false;
        // Fuera de rango para centavos
        if (dec > (decimal)long.MaxValue / 100m) return false;
        // Rechazar más de dos decimales significativos sin redondeo silencioso
        var decRedondeado = Math.Round(dec, 2, MidpointRounding.AwayFromZero);
        if (dec != decRedondeado) return false;
        // Verificar que no tenga más de 2 decimales en texto original (evitar 1.234,567)
        // Ya cubierto por comparación anterior
        minor = (long)(dec * 100m);
        return true;
    }
    private static string MensajeError(Exception ex) => ex is LocalInfantil.Application.Abstractions.AccesoDenegadoException ? "Permiso denegado." : ex is LocalInfantil.Application.Abstractions.SesionInvalidaException ? "Sesión inválida." : ex.Message;
}
