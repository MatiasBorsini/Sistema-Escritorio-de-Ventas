using System.Collections.ObjectModel;
using System.Globalization;
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
    public string Sku { get => _sku; set => SetProperty(ref _sku, value); }
    public string CantidadTexto { get => _cantidadTexto; set => SetProperty(ref _cantidadTexto, value); }
    public string CostoTexto { get => _costoTexto; set => SetProperty(ref _costoTexto, value); }
    public string Moneda { get => _moneda; set => SetProperty(ref _moneda, value); }
    public string? VarianteId { get => _varianteId; set => SetProperty(ref _varianteId, value); }
    public string? Detalle { get => _detalle; set => SetProperty(ref _detalle, value); }
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

    public RecepcionViewModel(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        Proveedores = new ObservableCollection<ProveedorRegistrado>();
        Filas = new ObservableCollection<FilaRecepcionViewModel> { new(), new() };
        _operacionId = servicios.GeneradorIds.NuevoId();
        ConfirmarCommand = new AsyncRelayCommand(ConfirmarAsync);
        AgregarFilaCommand = new AsyncRelayCommand(AgregarFilaAsync);
        QuitarFilaCommand = new AsyncRelayCommand(QuitarFilaAsync);
        RefrescarProveedoresCommand = new AsyncRelayCommand(CargarProveedoresAsync);
    }

    public ObservableCollection<ProveedorRegistrado> Proveedores { get; }
    public ObservableCollection<FilaRecepcionViewModel> Filas { get; }
    public ProveedorRegistrado? ProveedorSeleccionado { get => _proveedorSeleccionado; set => SetProperty(ref _proveedorSeleccionado, value); }
    public string? ComprobanteRef { get => _comprobanteRef; set => SetProperty(ref _comprobanteRef, value); }
    public string OperacionId { get => _operacionId; set => SetProperty(ref _operacionId, value); }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; set => SetProperty(ref _ocupado, value); }

    public AsyncRelayCommand ConfirmarCommand { get; }
    public AsyncRelayCommand AgregarFilaCommand { get; }
    public AsyncRelayCommand QuitarFilaCommand { get; }
    public AsyncRelayCommand RefrescarProveedoresCommand { get; }

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
    }

    public Task ConfirmarAsyncPublic() => ConfirmarAsync();
    private Task AgregarFilaAsync() { Filas.Add(new FilaRecepcionViewModel()); return Task.CompletedTask; }
    private Task QuitarFilaAsync() { if (Filas.Count > 1) Filas.RemoveAt(Filas.Count - 1); return Task.CompletedTask; }

    public async Task ConfirmarAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null;
        if (ProveedorSeleccionado is null) { Error = "Elegí un proveedor."; return; }
        var lineas = new List<LineaRecepcion>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in Filas)
        {
            if (string.IsNullOrWhiteSpace(f.Sku)) continue;
            if (!long.TryParse(f.CantidadTexto.Trim(), out var cant) || cant <= 0) { Error = $"Cantidad inválida para SKU {f.Sku}. Entero >0."; return; }
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
                var vari = await _servicios.ConsultasCatalogo.BuscarPorSkuAsync(f.Sku.Trim(), _idSesion());
                if (vari is null) { Error = $"SKU no encontrado: {f.Sku}."; return; }
                if (!vistos.Add(vari.Id)) { Error = $"Variante repetida: {f.Sku}."; return; }
                f.VarianteId = vari.Id;
                f.Detalle = $"{vari.Sku} — {vari.Talle ?? "sin talle"}/{vari.Color ?? "sin color"}";
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
                var mismoContenido = LineasIguales(_ultimasLineas, lineas) && _ultimoProveedorId == ProveedorSeleccionado.Id && _ultimoComprobante == comprobante;
                if (mismoContenido)
                {
                    Mensaje = $"Reconciliación: la recepción con operacion_id {_operacionId} ya quedó registrada. No se crea duplicado.";
                    _ultimasLineas = null;
                    _ultimoProveedorId = null;
                    _ultimoComprobante = null;
                    _operacionId = _servicios.GeneradorIds.NuevoId();
                    OnPropertyChanged(nameof(OperacionId));
                    return;
                }
                else
                {
                    Mensaje = $"Reconciliación: la recepción previa con operacion_id {_operacionId} ya existe con otro contenido. Mostrando resultado anterior; nueva recepción requiere nueva confirmación explícita.";
                    _ultimasLineas = null;
                    _ultimoProveedorId = null;
                    _ultimoComprobante = null;
                    _operacionId = _servicios.GeneradorIds.NuevoId();
                    OnPropertyChanged(nameof(OperacionId));
                    return;
                }
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

        Ocupado = true;
        try
        {
            var id = await _servicios.CasoConfirmarRecepcion.EjecutarAsync(ProveedorSeleccionado.Id, lineas, _operacionId, comprobante, _idSesion());
            Mensaje = $"SUMAR INGRESO DE MERCADERÍA — Recepción {id} confirmada: {lineas.Count} variante(s) sumadas. No es ESTABLECER.";
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
            var msg = ex is InvalidOperationException && ex.Message.Contains("ya fue registrada")
                ? "Esta recepción ya existe con ese operacion_id."
                : MensajeError(ex) + $" (sin éxito parcial; reintentá con mismo operacion_id y mismas filas/proveedor/comprobante: {_operacionId})";
            Error = msg;
        }
        finally { Ocupado = false; }
    }

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
        var t = texto.Trim().Replace("ARS", "", StringComparison.OrdinalIgnoreCase).Trim().Replace(',', '.');
        if (!decimal.TryParse(t, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var dec) || dec < 0) return false;
        minor = (long)Math.Round(dec * 100m, MidpointRounding.AwayFromZero);
        return true;
    }
    private static string MensajeError(Exception ex) => ex is LocalInfantil.Application.Abstractions.AccesoDenegadoException ? "Permiso denegado." : ex is LocalInfantil.Application.Abstractions.SesionInvalidaException ? "Sesión inválida." : ex.Message;
}
