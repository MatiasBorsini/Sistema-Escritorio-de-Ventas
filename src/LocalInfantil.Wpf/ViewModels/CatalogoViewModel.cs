using System.Collections.ObjectModel;
using System.Globalization;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class FilaCargaViewModel : ViewModelBase
{
    private string _sku = string.Empty;
    private string _cantidadTexto = string.Empty;
    private string? _varianteId;
    private string? _detalle;
    public string Sku { get => _sku; set => SetProperty(ref _sku, value); }
    public string CantidadTexto { get => _cantidadTexto; set => SetProperty(ref _cantidadTexto, value); }
    public string? VarianteId { get => _varianteId; set => SetProperty(ref _varianteId, value); }
    public string? Detalle { get => _detalle; set => SetProperty(ref _detalle, value); }
}

public sealed class CatalogoViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private string _textoBusqueda = string.Empty;
    private string _skuBusqueda = string.Empty;
    private int _pagina;
    private const int TamanoPagina = 20;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private ProductoRegistrado? _productoSeleccionado;
    private FichaProducto? _ficha;
    private string _nuevoProductoNombre = string.Empty;
    private string? _nuevoProductoMarca;
    private string? _nuevoProductoCategoria;
    private string _nuevoVarianteSku = string.Empty;
    private string? _nuevoVarianteTalle;
    private string? _nuevoVarianteColor;
    private string _precioTexto = string.Empty;
    private string _operacionIdCarga = string.Empty;
    private IReadOnlyList<FilaCargaInicial>? _ultimasFilasEnviadas;

    public CatalogoViewModel(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        Productos = new ObservableCollection<ProductoRegistrado>();
        FilasCarga = new ObservableCollection<FilaCargaViewModel> { new(), new() };
        _operacionIdCarga = servicios.GeneradorIds.NuevoId();
        BuscarCommand = new AsyncRelayCommand(BuscarAsync);
        BuscarSkuCommand = new AsyncRelayCommand(BuscarSkuAsync);
        SiguienteCommand = new AsyncRelayCommand(() => CambiarPaginaAsync(1));
        AnteriorCommand = new AsyncRelayCommand(() => CambiarPaginaAsync(-1));
        VerFichaCommand = new AsyncRelayCommand(VerFichaAsync);
        CrearProductoCommand = new AsyncRelayCommand(CrearProductoAsync);
        EditarProductoCommand = new AsyncRelayCommand(EditarProductoAsync);
        DesactivarProductoCommand = new AsyncRelayCommand(DesactivarProductoAsync);
        CrearVarianteCommand = new AsyncRelayCommand(CrearVarianteAsync);
        EditarVarianteCommand = new AsyncRelayCommand(EditarVarianteAsync);
        DesactivarVarianteCommand = new AsyncRelayCommand(DesactivarVarianteAsync);
        FijarPrecioCommand = new AsyncRelayCommand(FijarPrecioAsync);
        AgregarFilaCargaCommand = new AsyncRelayCommand(AgregarFilaAsync);
        QuitarFilaCargaCommand = new AsyncRelayCommand(QuitarFilaAsync);
        EjecutarCargaCommand = new AsyncRelayCommand(EjecutarCargaAsync);
    }

    public ObservableCollection<ProductoRegistrado> Productos { get; }
    public ObservableCollection<FilaCargaViewModel> FilasCarga { get; }
    public string TextoBusqueda { get => _textoBusqueda; set => SetProperty(ref _textoBusqueda, value); }
    public string SkuBusqueda { get => _skuBusqueda; set => SetProperty(ref _skuBusqueda, value); }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; set => SetProperty(ref _ocupado, value); }
    public ProductoRegistrado? ProductoSeleccionado
    {
        get => _productoSeleccionado;
        set
        {
            if (SetProperty(ref _productoSeleccionado, value))
                _ = VerFichaAsync();
        }
    }
    public FichaProducto? Ficha { get => _ficha; set => SetProperty(ref _ficha, value); }
    public string NuevoProductoNombre { get => _nuevoProductoNombre; set => SetProperty(ref _nuevoProductoNombre, value); }
    public string? NuevoProductoMarca { get => _nuevoProductoMarca; set => SetProperty(ref _nuevoProductoMarca, value); }
    public string? NuevoProductoCategoria { get => _nuevoProductoCategoria; set => SetProperty(ref _nuevoProductoCategoria, value); }
    public string NuevoVarianteSku { get => _nuevoVarianteSku; set => SetProperty(ref _nuevoVarianteSku, value); }
    public string? NuevoVarianteTalle { get => _nuevoVarianteTalle; set => SetProperty(ref _nuevoVarianteTalle, value); }
    public string? NuevoVarianteColor { get => _nuevoVarianteColor; set => SetProperty(ref _nuevoVarianteColor, value); }
    public string PrecioTexto { get => _precioTexto; set => SetProperty(ref _precioTexto, value); }
    public int Pagina => _pagina;
    public string OperacionIdCarga => _operacionIdCarga;

    public AsyncRelayCommand BuscarCommand { get; }
    public AsyncRelayCommand BuscarSkuCommand { get; }
    public AsyncRelayCommand SiguienteCommand { get; }
    public AsyncRelayCommand AnteriorCommand { get; }
    public AsyncRelayCommand VerFichaCommand { get; }
    public AsyncRelayCommand CrearProductoCommand { get; }
    public AsyncRelayCommand EditarProductoCommand { get; }
    public AsyncRelayCommand DesactivarProductoCommand { get; }
    public AsyncRelayCommand CrearVarianteCommand { get; }
    public AsyncRelayCommand EditarVarianteCommand { get; }
    public AsyncRelayCommand DesactivarVarianteCommand { get; }
    public AsyncRelayCommand FijarPrecioCommand { get; }
    public AsyncRelayCommand AgregarFilaCargaCommand { get; }
    public AsyncRelayCommand QuitarFilaCargaCommand { get; }
    public AsyncRelayCommand EjecutarCargaCommand { get; }

    public static string FormatearArs(long? minor)
    {
        if (minor is null) return "—";
        var dec = (decimal)minor.Value / 100m;
        return $"ARS {dec.ToString("N2", new CultureInfo("es-AR"))}";
    }

    private async Task BuscarAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            var sesion = _idSesion();
            var lista = await _servicios.ConsultasCatalogo.BuscarProductosAsync(TextoBusqueda ?? string.Empty, TamanoPagina, _pagina * TamanoPagina, sesion);
            Productos.Clear();
            foreach (var p in lista) Productos.Add(p);
            Mensaje = lista.Count == 0 ? "Sin resultados." : $"{lista.Count} producto(s). Página {_pagina + 1}.";
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task BuscarSkuAsync()
    {
        if (Ocupado || string.IsNullOrWhiteSpace(SkuBusqueda)) { Error = "Ingresá un SKU."; return; }
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            var sesion = _idSesion();
            var v = await _servicios.ConsultasCatalogo.BuscarPorSkuAsync(SkuBusqueda.Trim(), sesion);
            if (v is null) { Error = "SKU no encontrado."; return; }
            var ficha = await _servicios.ConsultasCatalogo.ObtenerFichaAsync(v.ProductoId, DateTime.UtcNow.ToString("o"), sesion);
            Ficha = ficha;
            var prod = ficha?.Producto;
            if (prod is not null)
            {
                Productos.Clear();
                Productos.Add(prod);
                ProductoSeleccionado = prod;
            }
            Mensaje = $"SKU {v.Sku} encontrado.";
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CambiarPaginaAsync(int delta)
    {
        var nueva = _pagina + delta;
        if (nueva < 0) return;
        _pagina = nueva;
        OnPropertyChanged(nameof(Pagina));
        await BuscarAsync();
    }

    private async Task VerFichaAsync()
    {
        if (ProductoSeleccionado is null) return;
        Error = null; Ocupado = true;
        try
        {
            var sesion = _idSesion();
            Ficha = await _servicios.ConsultasCatalogo.ObtenerFichaAsync(ProductoSeleccionado.Id, DateTime.UtcNow.ToString("o"), sesion);
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CrearProductoAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null;
        if (string.IsNullOrWhiteSpace(NuevoProductoNombre)) { Error = "Nombre obligatorio."; return; }
        Ocupado = true;
        try
        {
            var id = await _servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto(NuevoProductoNombre.Trim(), NullSiVacio(NuevoProductoMarca), NullSiVacio(NuevoProductoCategoria)), _idSesion());
            Mensaje = $"Producto creado {id}.";
            NuevoProductoNombre = string.Empty; NuevoProductoMarca = null; NuevoProductoCategoria = null;
            OnPropertyChanged(nameof(NuevoProductoNombre)); OnPropertyChanged(nameof(NuevoProductoMarca)); OnPropertyChanged(nameof(NuevoProductoCategoria));
            await BuscarAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task EditarProductoAsync()
    {
        if (ProductoSeleccionado is null) { Error = "Seleccioná un producto."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoEditarProducto.EjecutarAsync(ProductoSeleccionado.Id, new EdicionProducto(NuevoProductoNombre.Trim(), NullSiVacio(NuevoProductoMarca), NullSiVacio(NuevoProductoCategoria)), _idSesion());
            Mensaje = "Producto editado.";
            await BuscarAsync();
            await VerFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task DesactivarProductoAsync()
    {
        if (ProductoSeleccionado is null) { Error = "Seleccioná un producto."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoDesactivarProducto.EjecutarAsync(ProductoSeleccionado.Id, _idSesion());
            Mensaje = "Producto desactivado.";
            await BuscarAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CrearVarianteAsync()
    {
        if (ProductoSeleccionado is null) { Error = "Seleccioná un producto para su variante."; return; }
        if (string.IsNullOrWhiteSpace(NuevoVarianteSku)) { Error = "SKU obligatorio e inmutable."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            var id = await _servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(ProductoSeleccionado.Id, NuevoVarianteSku.Trim(), NullSiVacio(NuevoVarianteTalle), NullSiVacio(NuevoVarianteColor)), _idSesion());
            Mensaje = $"Variante creada {id}.";
            NuevoVarianteSku = string.Empty; NuevoVarianteTalle = null; NuevoVarianteColor = null;
            OnPropertyChanged(nameof(NuevoVarianteSku)); OnPropertyChanged(nameof(NuevoVarianteTalle)); OnPropertyChanged(nameof(NuevoVarianteColor));
            await VerFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task EditarVarianteAsync()
    {
        var v = Ficha?.Variantes.FirstOrDefault();
        if (v is null) { Error = "Seleccioná una variante (usa ficha)."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoEditarVariante.EjecutarAsync(v.Id, new EdicionVariante(NullSiVacio(NuevoVarianteTalle), NullSiVacio(NuevoVarianteColor)), _idSesion());
            Mensaje = "Variante editada (talle/color). SKU no se reasigna.";
            await VerFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task DesactivarVarianteAsync()
    {
        var v = Ficha?.Variantes.FirstOrDefault();
        if (v is null) { Error = "Seleccioná una variante."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoDesactivarVariante.EjecutarAsync(v.Id, _idSesion());
            Mensaje = "Variante desactivada (historial conservado).";
            await VerFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task FijarPrecioAsync()
    {
        var v = Ficha?.Variantes.FirstOrDefault();
        if (v is null) { Error = "Seleccioná una variante para fijar precio."; return; }
        if (Ocupado) return;
        if (!TryParseArs(PrecioTexto, out var minor)) { Error = "Precio ARS inválido. Ej: 1234,50"; return; }
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoFijarPrecio.EjecutarAsync(v.Id, minor, _idSesion());
            Mensaje = $"Precio fijado {FormatearArs(minor)} (historial conservado).";
            await VerFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    public Task BuscarAsyncPublic() => BuscarAsync();
    public Task VerFichaAsyncPublic() => VerFichaAsync();
    public Task CrearVarianteAsyncPublic() => CrearVarianteAsync();
    public Task FijarPrecioAsyncPublic() => FijarPrecioAsync();
    public Task CrearProductoAsyncPublic() => CrearProductoAsync();
    public Task EditarVarianteAsyncPublic() => EditarVarianteAsync();
    private Task AgregarFilaAsync() { FilasCarga.Add(new FilaCargaViewModel()); return Task.CompletedTask; }
    private Task QuitarFilaAsync() { if (FilasCarga.Count > 1) FilasCarga.RemoveAt(FilasCarga.Count - 1); return Task.CompletedTask; }

    private async Task EjecutarCargaAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null;
        var filas = new List<FilaCargaInicial>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in FilasCarga)
        {
            if (string.IsNullOrWhiteSpace(f.Sku)) continue;
            if (!long.TryParse(f.CantidadTexto.Trim(), out var cant) || cant <= 0) { Error = $"Cantidad inválida para SKU {f.Sku}. Debe ser entero >0."; return; }
            // Resolver variante por SKU
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
        // Idempotencia: si el contenido cambió respecto al último intento fallido, rotar id
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
            // Rotar operacion_id solo tras éxito confirmado
            _operacionIdCarga = _servicios.GeneradorIds.NuevoId();
            OnPropertyChanged(nameof(OperacionIdCarga));
            _ultimasFilasEnviadas = null;
            await VerFichaAsync();
        }
        catch (Exception ex)
        {
            // Conservar mismo operacion_id y filas para reintento idempotente si es error incierto
            _ultimasFilasEnviadas = filas;
            var msg = ex is InvalidOperationException && ex.Message.Contains("ya fue registrada")
                ? "Esta carga ya quedó registrada con ese operacion_id; no se volvió a crear."
                : MensajeError(ex) + " (sin éxito parcial; si el resultado es incierto, reintentá con mismo operacion_id y mismas filas: " + _operacionIdCarga + ")";
            Error = msg;
        }
        finally { Ocupado = false; }
    }

    private static string? NullSiVacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static bool TryParseArs(string texto, out long minor)
    {
        minor = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var t = texto.Trim().Replace("ARS", "", StringComparison.OrdinalIgnoreCase).Trim();
        // Permitir coma o punto
        t = t.Replace(',', '.');
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
    private static string MensajeError(Exception ex) => ex is LocalInfantil.Application.Abstractions.AccesoDenegadoException ? "Permiso denegado." : ex is LocalInfantil.Application.Abstractions.SesionInvalidaException ? "Sesión inválida. Iniciá sesión nuevamente." : ex.Message;
}
