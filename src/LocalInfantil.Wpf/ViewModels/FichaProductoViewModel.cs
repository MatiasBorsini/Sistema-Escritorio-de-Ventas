using System.Collections.ObjectModel;
using System.Globalization;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

/// <summary>
/// Ficha de producto en ventana secundaria (creación o edición). Reutiliza los
/// mismos casos de uso que el master; no mueve reglas comerciales al ViewModel.
/// Creación exige el orden del dominio: primero el producto, luego variantes,
/// precio y existencia inicial. Carga inicial ESTABLECE (no suma).
/// </summary>
public sealed class FichaProductoViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private string? _productoId;
    private FichaProducto? _ficha;
    private FichaVariante? _varianteSeleccionada;
    private string _solapa = "Datos";
    private string _nombre = string.Empty;
    private string? _marca;
    private string? _categoria;
    private string _nuevoVarianteSku = string.Empty;
    private string? _nuevoVarianteTalle;
    private string? _nuevoVarianteColor;
    private string _precioTexto = string.Empty;
    private string _operacionIdCarga = string.Empty;
    private IReadOnlyList<FilaCargaInicial>? _ultimasFilasEnviadas;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;

    public FichaProductoViewModel(ServiciosComerciales servicios, Func<string?> idSesion, string? productoId)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        _productoId = productoId;
        FilasCarga = new ObservableCollection<FilaCargaViewModel> { new(), new() };
        _operacionIdCarga = servicios.GeneradorIds.NuevoId();
        SeleccionarSolapaCommand = new RelayCommand(SeleccionarSolapa);
        GuardarDatosCommand = new AsyncRelayCommand(GuardarDatosAsync);
        CrearVarianteCommand = new AsyncRelayCommand(CrearVarianteAsync);
        EditarVarianteCommand = new AsyncRelayCommand(EditarVarianteAsync);
        DesactivarVarianteCommand = new AsyncRelayCommand(DesactivarVarianteAsync);
        FijarPrecioCommand = new AsyncRelayCommand(FijarPrecioAsync);
        DesactivarProductoCommand = new AsyncRelayCommand(DesactivarProductoAsync);
        AgregarFilaCargaCommand = new AsyncRelayCommand(() => { FilasCarga.Add(new FilaCargaViewModel()); return Task.CompletedTask; });
        QuitarFilaCargaCommand = new AsyncRelayCommand(() => { if (FilasCarga.Count > 1) FilasCarga.RemoveAt(FilasCarga.Count - 1); return Task.CompletedTask; });
        EjecutarCargaCommand = new AsyncRelayCommand(EjecutarCargaAsync);
    }

    public bool EsCreacion => _productoId is null;
    public bool SolapasSecundariasHabilitadas => !EsCreacion;
    public string? ProductoId => _productoId;
    public ObservableCollection<FilaCargaViewModel> FilasCarga { get; }
    public FichaProducto? Ficha { get => _ficha; private set => SetProperty(ref _ficha, value); }
    public FichaVariante? VarianteSeleccionada { get => _varianteSeleccionada; set => SetProperty(ref _varianteSeleccionada, value); }
    public string Nombre { get => _nombre; set => SetProperty(ref _nombre, value); }
    public string? Marca { get => _marca; set => SetProperty(ref _marca, value); }
    public string? Categoria { get => _categoria; set => SetProperty(ref _categoria, value); }
    public string NuevoVarianteSku { get => _nuevoVarianteSku; set => SetProperty(ref _nuevoVarianteSku, value); }
    public string? NuevoVarianteTalle { get => _nuevoVarianteTalle; set => SetProperty(ref _nuevoVarianteTalle, value); }
    public string? NuevoVarianteColor { get => _nuevoVarianteColor; set => SetProperty(ref _nuevoVarianteColor, value); }
    public string PrecioTexto { get => _precioTexto; set => SetProperty(ref _precioTexto, value); }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; set => SetProperty(ref _ocupado, value); }
    public string OperacionIdCarga => _operacionIdCarga;

    public string TituloVentana => EsCreacion ? "Nuevo producto" : $"Producto · {Ficha?.Producto.Nombre ?? "…"}";
    public string SubtituloVentana => EsCreacion ? "Completá los datos para crear el producto" : "Ficha y variantes";

    public string Solapa
    {
        get => _solapa;
        private set
        {
            if (SetProperty(ref _solapa, value))
            {
                OnPropertyChanged(nameof(SolapaDatosVisible));
                OnPropertyChanged(nameof(SolapaVariantesVisible));
                OnPropertyChanged(nameof(SolapaEstadoVisible));
            }
        }
    }

    public bool SolapaDatosVisible => _solapa == "Datos";
    public bool SolapaVariantesVisible => _solapa == "Variantes";
    public bool SolapaEstadoVisible => _solapa == "Estado";

    public RelayCommand SeleccionarSolapaCommand { get; }
    public AsyncRelayCommand GuardarDatosCommand { get; }
    public AsyncRelayCommand CrearVarianteCommand { get; }
    public AsyncRelayCommand EditarVarianteCommand { get; }
    public AsyncRelayCommand DesactivarVarianteCommand { get; }
    public AsyncRelayCommand FijarPrecioCommand { get; }
    public AsyncRelayCommand DesactivarProductoCommand { get; }
    public AsyncRelayCommand AgregarFilaCargaCommand { get; }
    public AsyncRelayCommand QuitarFilaCargaCommand { get; }
    public AsyncRelayCommand EjecutarCargaCommand { get; }

    public Task InicializarAsyncPublic() => InicializarAsync();
    public Task GuardarDatosAsyncPublic() => GuardarDatosAsync();
    public Task CrearVarianteAsyncPublic() => CrearVarianteAsync();
    public Task FijarPrecioAsyncPublic() => FijarPrecioAsync();
    public Task DesactivarProductoAsyncPublic() => DesactivarProductoAsync();
    public Task EjecutarCargaAsyncPublic() => EjecutarCargaAsync();

    public async Task InicializarAsync()
    {
        if (EsCreacion) return;
        await RecargarFichaAsync();
        if (Ficha is not null)
        {
            Nombre = Ficha.Producto.Nombre;
            Marca = Ficha.Producto.Marca;
            Categoria = Ficha.Producto.Categoria;
            OnPropertyChanged(nameof(Nombre));
            OnPropertyChanged(nameof(Marca));
            OnPropertyChanged(nameof(Categoria));
        }
    }

    private void SeleccionarSolapa(object? solapa)
    {
        if (solapa is string s && (s == "Datos" || s == "Variantes" || s == "Estado"))
        {
            if (EsCreacion && s != "Datos") return;
            Solapa = s;
        }
    }

    private async Task RecargarFichaAsync()
    {
        if (_productoId is null) return;
        Error = null; Ocupado = true;
        try
        {
            Ficha = await _servicios.ConsultasCatalogo.ObtenerFichaAsync(_productoId, DateTime.UtcNow.ToString("o"), _idSesion());
            OnPropertyChanged(nameof(TituloVentana));
            OnPropertyChanged(nameof(SubtituloVentana));
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task GuardarDatosAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null;
        if (string.IsNullOrWhiteSpace(Nombre)) { Error = "Nombre obligatorio."; return; }
        Ocupado = true;
        try
        {
            if (EsCreacion)
            {
                _productoId = await _servicios.CasoCrearProducto.EjecutarAsync(
                    new NuevoProducto(Nombre.Trim(), NullSiVacio(Marca), NullSiVacio(Categoria)), _idSesion());
                OnPropertyChanged(nameof(EsCreacion));
                OnPropertyChanged(nameof(SolapasSecundariasHabilitadas));
                OnPropertyChanged(nameof(ProductoId));
                Mensaje = "Producto creado. Ahora podés agregar variantes.";
                await RecargarFichaAsync();
                Solapa = "Variantes";
            }
            else
            {
                await _servicios.CasoEditarProducto.EjecutarAsync(
                    _productoId!, new EdicionProducto(Nombre.Trim(), NullSiVacio(Marca), NullSiVacio(Categoria)), _idSesion());
                Mensaje = "Producto editado.";
                await RecargarFichaAsync();
            }
        }
        catch (Exception ex) { Error = MensajeError(ex, EsCreacion ? "crear productos" : "editar productos"); }
        finally { Ocupado = false; }
    }

    private async Task CrearVarianteAsync()
    {
        if (_productoId is null) { Error = "Creá primero el producto."; return; }
        if (string.IsNullOrWhiteSpace(NuevoVarianteSku)) { Error = "SKU obligatorio e inmutable."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoCrearVariante.EjecutarAsync(
                new NuevaVariante(_productoId, NuevoVarianteSku.Trim(), NullSiVacio(NuevoVarianteTalle), NullSiVacio(NuevoVarianteColor)), _idSesion());
            Mensaje = "Variante creada.";
            NuevoVarianteSku = string.Empty; NuevoVarianteTalle = null; NuevoVarianteColor = null;
            OnPropertyChanged(nameof(NuevoVarianteSku)); OnPropertyChanged(nameof(NuevoVarianteTalle)); OnPropertyChanged(nameof(NuevoVarianteColor));
            await RecargarFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex, "crear variantes"); }
        finally { Ocupado = false; }
    }

    private async Task EditarVarianteAsync()
    {
        if (VarianteSeleccionada is null) { Error = "Seleccioná una variante."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoEditarVariante.EjecutarAsync(
                VarianteSeleccionada.Id, new EdicionVariante(NullSiVacio(NuevoVarianteTalle), NullSiVacio(NuevoVarianteColor)), _idSesion());
            Mensaje = "Variante editada (talle/color). SKU no se reasigna.";
            await RecargarFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex, "editar variantes"); }
        finally { Ocupado = false; }
    }

    private async Task DesactivarVarianteAsync()
    {
        if (VarianteSeleccionada is null) { Error = "Seleccioná una variante."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoDesactivarVariante.EjecutarAsync(VarianteSeleccionada.Id, _idSesion());
            Mensaje = "Variante desactivada (historial conservado).";
            await RecargarFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex, "desactivar variantes"); }
        finally { Ocupado = false; }
    }

    private async Task FijarPrecioAsync()
    {
        if (VarianteSeleccionada is null) { Error = "Seleccioná una variante para fijar precio."; return; }
        if (Ocupado) return;
        if (!TryParseArs(PrecioTexto, out var minor)) { Error = "Precio ARS inválido. Ej: 1234,50"; return; }
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoFijarPrecio.EjecutarAsync(VarianteSeleccionada.Id, minor, _idSesion());
            Mensaje = $"Precio fijado {CatalogoViewModel.FormatearArs(minor)} (historial conservado).";
            await RecargarFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex, "fijar precios"); }
        finally { Ocupado = false; }
    }

    private async Task DesactivarProductoAsync()
    {
        if (_productoId is null) { Error = "Creá primero el producto."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoDesactivarProducto.EjecutarAsync(_productoId, _idSesion());
            Mensaje = "Producto desactivado.";
            await RecargarFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex, "desactivar productos"); }
        finally { Ocupado = false; }
    }

    private async Task EjecutarCargaAsync()
    {
        if (_productoId is null) { Error = "Creá primero el producto y sus variantes."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null;
        var filas = new List<FilaCargaInicial>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in FilasCarga)
        {
            if (string.IsNullOrWhiteSpace(f.Sku)) continue;
            if (!long.TryParse(f.CantidadTexto.Trim(), out var cant) || cant <= 0) { Error = $"Cantidad inválida para SKU {f.Sku}. Debe ser entero >0."; return; }
            try
            {
                var vari = await _servicios.ConsultasCatalogo.BuscarPorSkuAsync(f.Sku.Trim(), _idSesion());
                if (vari is null) { Error = $"SKU no encontrado: {f.Sku}."; return; }
                if (!vistos.Add(vari.Id)) { Error = $"SKU repetido en grilla: {f.Sku}."; return; }
                f.VarianteId = vari.Id;
                f.Detalle = $"{vari.Sku} — {vari.Talle ?? "sin talle"}/{vari.Color ?? "sin color"}";
                filas.Add(new FilaCargaInicial(vari.Id, cant));
            }
            catch (Exception ex) { Error = MensajeError(ex); return; }
        }
        if (filas.Count == 0) { Error = "Agregá al menos una fila válida (SKU y cantidad)."; return; }
        if (_ultimasFilasEnviadas is not null && !FilasIguales(_ultimasFilasEnviadas, filas))
        {
            _operacionIdCarga = _servicios.GeneradorIds.NuevoId();
            OnPropertyChanged(nameof(OperacionIdCarga));
            _ultimasFilasEnviadas = null;
        }
        Ocupado = true;
        try
        {
            await _servicios.CasoCargarExistenciaInicial.EjecutarAsync(filas, _operacionIdCarga, _idSesion());
            Mensaje = $"ESTABLECER EXISTENCIA INICIAL: {filas.Count} variante(s) establecidas. No es recepción ni suma.";
            _operacionIdCarga = _servicios.GeneradorIds.NuevoId();
            OnPropertyChanged(nameof(OperacionIdCarga));
            _ultimasFilasEnviadas = null;
            await RecargarFichaAsync();
        }
        catch (Exception ex)
        {
            _ultimasFilasEnviadas = filas;
            Error = ex is InvalidOperationException && ex.Message.Contains("ya fue registrada")
                ? "Esta carga ya quedó registrada; no se volvió a crear."
                : MensajeError(ex, "cargar stock inicial") + " (sin éxito parcial; si el resultado es incierto, reintentá con las mismas filas)";
        }
        finally { Ocupado = false; }
    }

    private static string? NullSiVacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool TryParseArs(string texto, out long minor)
    {
        minor = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var t = texto.Trim().Replace("ARS", "", StringComparison.OrdinalIgnoreCase).Trim().Replace(',', '.');
        if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec) || dec < 0) return false;
        minor = (long)Math.Round(dec * 100m, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool FilasIguales(IReadOnlyList<FilaCargaInicial> a, IReadOnlyList<FilaCargaInicial> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].VarianteId != b[i].VarianteId || a[i].Cantidad != b[i].Cantidad) return false;
        return true;
    }

    private static string MensajeError(Exception ex, string? accion = null) => ex switch
    {
        LocalInfantil.Application.Abstractions.AccesoDenegadoException when accion is not null
            => $"No tenés permiso para {accion}. La Dueña puede habilitarlo desde Administración → Permisos.",
        LocalInfantil.Application.Abstractions.AccesoDenegadoException
            => "Permiso denegado. La Dueña puede habilitar permisos comerciales desde Administración → Permisos.",
        LocalInfantil.Application.Abstractions.SesionInvalidaException
            => "Sesión inválida. Iniciá sesión nuevamente.",
        _ => ex.Message,
    };
}
