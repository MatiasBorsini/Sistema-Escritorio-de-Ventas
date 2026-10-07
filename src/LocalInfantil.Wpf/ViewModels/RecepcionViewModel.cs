using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

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
    private long? _quedara;
    private long? _subtotalMinor;
    private bool _resuelta;
    private bool _valida;
    public string Sku
    {
        get => _sku;
        set
        {
            if (SetProperty(ref _sku, value) && MotorVariantes is not null && MotorVariantes.Texto != value)
                MotorVariantes.EstablecerTexto(value);
        }
    }
    public string CantidadTexto { get => _cantidadTexto; set => SetProperty(ref _cantidadTexto, value); }
    public string CostoTexto { get => _costoTexto; set => SetProperty(ref _costoTexto, value); }
    public string Moneda { get => _moneda; set => SetProperty(ref _moneda, value); }
    public string? VarianteId { get => _varianteId; set => SetProperty(ref _varianteId, value); }
    public string? Detalle { get => _detalle; set => SetProperty(ref _detalle, value); }

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
            _sku = _motorVariantes.Texto;
            OnPropertyChanged(nameof(Sku));
        }
    }
    public RelayCommand SeleccionarSugerenciaCommand { get; }
    public AsyncRelayCommand? BuscarDirectaCommand { get; set; }

    public FilaRecepcionViewModel()
    {
        SeleccionarSugerenciaCommand = new RelayCommand(p =>
        {
            if (p is SugerenciaVariante s)
                AplicarSugerencia(s);
        });
    }

    /// <summary>
    /// Aplica una sugerencia con el código/id real y el detalle humano, sin
    /// validaciones nuevas (Verificar/Confirmar siguen validando todo).
    /// </summary>
    public void AplicarSugerencia(SugerenciaVariante s)
    {
        Sku = s.CodigoInterno;
        VarianteId = s.VarianteId;
        Detalle = $"{s.CodigoInterno} — {s.DetalleTalleColor}";
    }

    /// <summary>
    /// Lectura derivada de consultas existentes (verificación bajo demanda).
    /// StockActual/Quedará/Subtotal no recalculan reglas: Quedará es la suma
    /// aritmética que la recepción SUMA confirmará.
    /// </summary>
    public long? StockActual { get => _stockActual; set => SetProperty(ref _stockActual, value); }
    public long? Quedara { get => _quedara; set => SetProperty(ref _quedara, value); }
    public long? SubtotalMinor { get => _subtotalMinor; set => SetProperty(ref _subtotalMinor, value); }
    public bool Resuelta { get => _resuelta; set => SetProperty(ref _resuelta, value); }
    public bool Valida { get => _valida; set => SetProperty(ref _valida, value); }
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
    private IReadOnlyDictionary<string, int>? _cantidadesRecepcionConfirmada;

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
        Filas.CollectionChanged += (_, _) => ResumenListo = false;
    }

    /// <summary>Fábrica de filas con buscador asistido ya cableado.</summary>
    private FilaRecepcionViewModel CrearFila()
    {
        return new FilaRecepcionViewModel
        {
            MotorVariantes = new MotorSugerencias<SugerenciaVariante>((t, ct) =>
                _servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(t, 10, _idSesion(), ct)),
            BuscarDirectaCommand = VerificarCommand,
        };
    }

    public ObservableCollection<ProveedorRegistrado> Proveedores { get; }
    public ObservableCollection<FilaRecepcionViewModel> Filas { get; }
    public ProveedorRegistrado? ProveedorSeleccionado { get => _proveedorSeleccionado; set => SetProperty(ref _proveedorSeleccionado, value); }

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
            }
        }
    }

    public int CantidadLineas => ResumenListo ? Filas.Count(f => f.Valida) : 0;
    public long UnidadesSumar => ResumenListo ? Filas.Where(f => f.Valida).Sum(f => long.TryParse(f.CantidadTexto.Trim(), out var c) ? c : 0) : 0;

    /// <summary>Total exacto solo si todas las filas válidas tienen costo.</summary>
    public long? TotalEstimado => !ResumenListo || CantidadLineas == 0 || Filas.Any(f => f.Valida && !f.SubtotalMinor.HasValue)
        ? null
        : Filas.Where(f => f.Valida).Sum(f => f.SubtotalMinor!.Value);

    public string ConfirmarTexto => ResumenListo && CantidadLineas > 0
        ? $"Confirmar y sumar {UnidadesSumar} unidades"
        : "Confirmar recepción";
    public string? ComprobanteRef { get => _comprobanteRef; set => SetProperty(ref _comprobanteRef, value); }
    public string OperacionId { get => _operacionId; set => SetProperty(ref _operacionId, value); }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; set => SetProperty(ref _ocupado, value); }

    public AsyncRelayCommand ConfirmarCommand { get; }
    public AsyncRelayCommand AgregarFilaCommand { get; }
    public AsyncRelayCommand QuitarFilaCommand { get; }
    public AsyncRelayCommand RefrescarProveedoresCommand { get; }
    public AsyncRelayCommand VerificarCommand { get; }
    public bool EtiquetasRecepcionDisponibles => _cantidadesRecepcionConfirmada is { Count: > 0 };
    public EtiquetasViewModel CrearEtiquetasRecepcionViewModel()
        => new(_servicios, _idSesion, _cantidadesRecepcionConfirmada ?? new Dictionary<string, int>());

    public Task VerificarAsyncPublic() => VerificarAsync();

    /// <summary>
    /// Resuelve cada fila con código usando consultas existentes (variante +
    /// ficha con existencias). No valida reglas de negocio: solo expone
    /// StockActual/Quedará/Subtotal de lectura para operar con contexto.
    /// La confirmación vuelve a resolver y validar todo (lógica intacta).
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
                f.Quedara = null;
                f.SubtotalMinor = null;
                if (string.IsNullOrWhiteSpace(f.Sku)) continue;
                try
                {
                    var vari = await _servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(f.Sku.Trim(), _idSesion());
                    if (vari is null) { f.Detalle = "Código interno no encontrado."; continue; }
                    f.VarianteId = vari.Id;
                    f.Detalle = $"{vari.CodigoInterno} — {vari.Talle ?? "sin talle"}/{vari.Color ?? "sin color"}";
                    var ficha = await _servicios.ConsultasCatalogo.ObtenerFichaAsync(vari.ProductoId, fecha, _idSesion());
                    var existencia = ficha?.Variantes.FirstOrDefault(v => v.Id == vari.Id)?.Existencias;
                    f.StockActual = existencia;
                    if (long.TryParse(f.CantidadTexto.Trim(), out var cant) && cant > 0)
                    {
                        f.Quedara = existencia.HasValue ? existencia.Value + cant : null;
                        f.Valida = true;
                    }

                    if (!string.IsNullOrWhiteSpace(f.CostoTexto) && TryParseArs(f.CostoTexto, out var cm)
                        && long.TryParse(f.CantidadTexto.Trim(), out var cant2) && cant2 > 0)
                    {
                        f.SubtotalMinor = cant2 * cm;
                    }

                    f.Resuelta = true;
                }
                catch (Exception ex)
                {
                    f.Detalle = MensajeError(ex);
                }
            }

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
                Mensaje = $"Reconciliación tras reinicio: la recepción con operacion_id {pendiente.OperacionId} ya quedó registrada. No se duplicó.";
                try { AlmacenRecepcionPendiente.Eliminar(_servicios.RutaBaseDatos); }
                catch (Exception ex) { Error = $"No se pudo eliminar el pendiente tras reconciliación: {MensajeError(ex)}. Requiere reconciliación manual."; return; }
                _operacionId = _servicios.GeneradorIds.NuevoId();
                OnPropertyChanged(nameof(OperacionId));
                _ultimasLineas = null;
                _ultimoProveedorId = null;
                _ultimoComprobante = null;
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
        Error = null; Mensaje = null;
        if (ProveedorSeleccionado is null) { Error = "Elegí un proveedor."; return; }
        var lineas = new List<LineaRecepcion>();
        var cantidadesParaEtiquetas = new Dictionary<string, int>(StringComparer.Ordinal);
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
                cantidadesParaEtiquetas[vari.Id] = (int)Math.Clamp(cant, 0, int.MaxValue);
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
                // El JSON del pendiente se elimina de forma controlada ANTES de
                // limpiar el estado: si la eliminación falla, se conserva el
                // bloqueo y nunca se habilita otra recepción como si estuviera resuelto.
                try
                {
                    AlmacenRecepcionPendiente.Eliminar(_servicios.RutaBaseDatos);
                }
                catch (Exception exElim)
                {
                    Error = $"La recepción con operacion_id {_operacionId} ya quedó registrada. No se pudo eliminar el pendiente: {MensajeError(exElim)}. Bloqueado hasta reconciliación manual; no se crea duplicado ni se habilita otra recepción.";
                    return;
                }
                var mismoContenido = LineasIguales(_ultimasLineas, lineas) && _ultimoProveedorId == ProveedorSeleccionado.Id && _ultimoComprobante == comprobante;
                if (mismoContenido)
                {
                    Mensaje = $"Reconciliación: la recepción con operacion_id {_operacionId} ya quedó registrada. No se crea duplicado.";
                }
                else
                {
                    Mensaje = $"Reconciliación: la recepción previa con operacion_id {_operacionId} ya existe con otro contenido. Mostrando resultado anterior; nueva recepción requiere nueva confirmación explícita.";
                }
                _ultimasLineas = null;
                _ultimoProveedorId = null;
                _ultimoComprobante = null;
                _operacionId = _servicios.GeneradorIds.NuevoId();
                OnPropertyChanged(nameof(OperacionId));
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

        Ocupado = true;
        try
        {
            var id = await _servicios.CasoConfirmarRecepcion.EjecutarAsync(ProveedorSeleccionado.Id, lineas, _operacionId, comprobante, _idSesion());
            Mensaje = $"Recepción {ReferenciaHumana(comprobante, id)} confirmada: {lineas.Count} variante(s) sumadas. No es ESTABLECER.";
            _cantidadesRecepcionConfirmada = cantidadesParaEtiquetas;
            OnPropertyChanged(nameof(EtiquetasRecepcionDisponibles));
            ResumenListo = false;
            AlmacenRecepcionPendiente.Eliminar(_servicios.RutaBaseDatos);
            _operacionId = _servicios.GeneradorIds.NuevoId();
            OnPropertyChanged(nameof(OperacionId));
            _ultimasLineas = null;
            _ultimoProveedorId = null;
            _ultimoComprobante = null;
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
        finally { Ocupado = false; }
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
    private static bool TryParseArs(string texto, out long minor)
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
