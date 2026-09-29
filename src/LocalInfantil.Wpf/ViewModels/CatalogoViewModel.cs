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

/// <summary>
/// Fila densa del master: resumen oficial por página (una sola lectura batch).
/// Agregados siempre conocidos (COUNT/SUM con COALESCE); sin N+1.
/// </summary>
public sealed class FilaCatalogo : ViewModelBase
{
    public FilaCatalogo(ResumenProducto resumen)
    {
        Producto = new ProductoRegistrado(resumen.Id, resumen.CodigoInterno, resumen.Nombre, resumen.Marca, resumen.Categoria, resumen.CategoriaId, resumen.Activo);
        CantidadVariantes = resumen.CantidadVariantes;
        StockTotal = resumen.StockTotal;
    }

    public FilaCatalogo(ProductoRegistrado producto, int cantidadVariantes, long stockTotal)
    {
        Producto = producto;
        CantidadVariantes = cantidadVariantes;
        StockTotal = stockTotal;
    }

    public ProductoRegistrado Producto { get; }
    public int CantidadVariantes { get; }
    public long StockTotal { get; }
    public string VariantesTexto => CantidadVariantes.ToString();
    public string StockTexto => StockTotal.ToString();
    public string EstadoTexto => Producto.Activo ? "Activo" : "Inactivo";
}

public sealed class CatalogoViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private readonly IDialogosCatalogo? _dialogos;
    private string _textoBusqueda = string.Empty;
    private string _skuBusqueda = string.Empty;
    private int _pagina;
    private int _tamanoPagina = 20;
    public int PaginaVisible => _pagina + 1;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private ProductoRegistrado? _productoSeleccionado;
    private FichaProducto? _ficha;
    private string _nuevoProductoNombre = string.Empty;
    private string? _nuevoProductoMarca;
    private string? _nuevoProductoCategoria;
    private string? _nuevoVarianteTalle;
    private string? _nuevoVarianteColor;
    private string _precioTexto = string.Empty;
    private string _operacionIdCarga = string.Empty;
    private IReadOnlyList<FilaCargaInicial>? _ultimasFilasEnviadas;

    private enum EstadoInicializacion { NoIniciada, Inicializando, Inicializada, Fallida }

    private EstadoInicializacion _estadoInicializacion = EstadoInicializacion.NoIniciada;

    public CatalogoViewModel(ServiciosComerciales servicios, Func<string?> idSesion, IDialogosCatalogo? dialogos = null)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        _dialogos = dialogos;
        Productos = new ObservableCollection<ProductoRegistrado>();
        Filas = new ObservableCollection<FilaCatalogo>();
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
        AbrirFichaCommand = new AsyncRelayCommand(AbrirFichaAsync);
        NuevaFichaCommand = new AsyncRelayCommand(NuevaFichaAsync);
        InicializarCommand = new AsyncRelayCommand(InicializarAsync);
        MotorSugerencias = new MotorSugerencias<SugerenciaVariante>((t, ct) =>
            _servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(t, 10, _idSesion(), ct));
        SeleccionarSugerenciaCommand = new AsyncRelayCommand(p => SeleccionarSugerenciaAsync(p as SugerenciaVariante));
        BuscarDirectaCommand = new AsyncRelayCommand(() => BuscarDirectaAsync(MotorSugerencias.Texto));
    }

    /// <summary>Motor del buscador asistido (modo Producto). El VM es dueño.</summary>
    public MotorSugerencias<SugerenciaVariante> MotorSugerencias { get; }

    public ObservableCollection<ProductoRegistrado> Productos { get; }
    public ObservableCollection<FilaCatalogo> Filas { get; }
    public ObservableCollection<FilaCargaViewModel> FilasCarga { get; }
    public int[] TamanosPagina { get; } = [20, 50, 100];
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
            {
                SincronizarFilaSeleccionada();
                _ = VerFichaAsync();
            }
        }
    }

    private FilaCatalogo? _filaSeleccionada;

    /// <summary>Selección del DataGrid. Sincroniza ProductoSeleccionado (y su ficha).</summary>
    public FilaCatalogo? FilaSeleccionada
    {
        get => _filaSeleccionada;
        set
        {
            if (SetProperty(ref _filaSeleccionada, value) && value is not null)
                ProductoSeleccionado = value.Producto;
        }
    }

    private void SincronizarFilaSeleccionada()
    {
        if (_productoSeleccionado is null) return;
        foreach (var f in Filas)
        {
            if (f.Producto.Id == _productoSeleccionado.Id)
            {
                SetProperty(ref _filaSeleccionada, f, nameof(FilaSeleccionada));
                return;
            }
        }
    }

    public FichaProducto? Ficha
    {
        get => _ficha;
        set
        {
            if (SetProperty(ref _ficha, value))
            {
                OnPropertyChanged(nameof(HayFicha));
                OnPropertyChanged(nameof(CantidadVariantesVista));
                OnPropertyChanged(nameof(StockVistaTotal));
                OnPropertyChanged(nameof(PrimerasVariantesVista));
                OnPropertyChanged(nameof(VistaTop3));
                OnPropertyChanged(nameof(VariantesRestantes));
                OnPropertyChanged(nameof(HayMasVariantes));
            }
        }
    }

    public bool HayFicha => Ficha is not null;

    /// <summary>Vista rápida readonly: agregados de la ficha ya cargada.</summary>
    public int CantidadVariantesVista => Ficha?.Variantes.Count ?? 0;

    public long? StockVistaTotal => Ficha is null ? null : Ficha.Variantes.Sum(v => v.Existencias);

    public IReadOnlyList<FichaVariante> PrimerasVariantesVista => Ficha is null
        ? Array.Empty<FichaVariante>()
        : Ficha.Variantes.Take(4).ToList();

    /// <summary>Vista rápida tabular: hasta 3 variantes + conteo restante.</summary>
    public IReadOnlyList<FichaVariante> VistaTop3 => Ficha is null
        ? Array.Empty<FichaVariante>()
        : Ficha.Variantes.Take(3).ToList();

    public int VariantesRestantes => Math.Max(0, CantidadVariantesVista - 3);
    public bool HayMasVariantes => VariantesRestantes > 0;
    public string NuevoProductoNombre { get => _nuevoProductoNombre; set => SetProperty(ref _nuevoProductoNombre, value); }
    public string? NuevoProductoMarca { get => _nuevoProductoMarca; set => SetProperty(ref _nuevoProductoMarca, value); }
    public string? NuevoProductoCategoria { get => _nuevoProductoCategoria; set => SetProperty(ref _nuevoProductoCategoria, value); }
    public string? NuevoVarianteTalle { get => _nuevoVarianteTalle; set => SetProperty(ref _nuevoVarianteTalle, value); }
    public string? NuevoVarianteColor { get => _nuevoVarianteColor; set => SetProperty(ref _nuevoVarianteColor, value); }
    public string PrecioTexto { get => _precioTexto; set => SetProperty(ref _precioTexto, value); }
    public int Pagina => _pagina;

    /// <summary>Tamaño de página real: se envía como límite a la consulta
    /// SQLite (1–100). Cambiarlo reinicia a la primera página.</summary>
    public int TamanoPagina
    {
        get => _tamanoPagina;
        set
        {
            if (value < 1 || value > 100 || value == _tamanoPagina) return;
            _tamanoPagina = value;
            _pagina = 0;
            OnPropertyChanged(nameof(TamanoPagina));
            OnPropertyChanged(nameof(Pagina));
            OnPropertyChanged(nameof(PaginaVisible));
            _ = BuscarAsync();
        }
    }

    private bool _haySiguiente;

    /// <summary>
    /// Peek +1: la consulta trae hasta TamanoPagina+1 y se muestran N. Si
    /// llegó la extra hay siguiente (correcto aunque el total sea múltiplo
    /// exacto del tamaño). Sin COUNT (*).
    /// </summary>
    public bool HaySiguiente
    {
        get => _haySiguiente;
        private set => SetProperty(ref _haySiguiente, value);
    }

    public bool EsUltimaPagina => !HaySiguiente;

    public bool HayAnterior => _pagina > 0;

    /// <summary>Cantidad fiable: filas de la página actual (sin total global).</summary>
    public string Resumen => Productos.Count == 0
        ? "Sin resultados."
        : $"{Productos.Count} resultado(s) · Página {PaginaVisible}" + (EsUltimaPagina ? " · Fin del listado" : string.Empty);

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
    public AsyncRelayCommand AbrirFichaCommand { get; }
    public AsyncRelayCommand NuevaFichaCommand { get; }
    public AsyncRelayCommand InicializarCommand { get; }
    public AsyncRelayCommand SeleccionarSugerenciaCommand { get; }
    public AsyncRelayCommand BuscarDirectaCommand { get; }

    public Task SeleccionarSugerenciaAsyncPublic(SugerenciaVariante? s) => SeleccionarSugerenciaAsync(s);
    public Task BuscarDirectaAsyncPublic(string? texto) => BuscarDirectaAsync(texto);

    /// <summary>
    /// Salto directo desde una sugerencia: filtra por el producto sugerido con
    /// el mismo flujo de búsqueda (sin flujo paralelo) y lo selecciona.
    /// </summary>
    private async Task SeleccionarSugerenciaAsync(SugerenciaVariante? sugerencia)
    {
        if (sugerencia is null) return;
        if (Ocupado) return;
        Error = null;
        TextoBusqueda = sugerencia.NombreProducto;
        OnPropertyChanged(nameof(TextoBusqueda));
        _pagina = 0;
        OnPropertyChanged(nameof(Pagina));
        OnPropertyChanged(nameof(PaginaVisible));
        await BuscarAsync();
        var fila = Filas.FirstOrDefault(f => f.Producto.Id == sugerencia.ProductoId);
        if (fila is not null)
            FilaSeleccionada = fila;
    }

    /// <summary>
    /// Enter sin sugerencia activa: si el texto es un código interno exacto
    /// y válido, salta directo a su producto (ingreso rápido / lector, sin
    /// obligar a elegir sugerencia); si no, búsqueda por texto normal.
    /// </summary>
    private async Task BuscarDirectaAsync(string? texto)
    {
        if (Ocupado) return;
        if (string.IsNullOrWhiteSpace(texto)) return;
        var t = texto.Trim();
        try
        {
            var exacta = await _servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(t, _idSesion());
            if (exacta is not null)
            {
                // Código exacto válido: salto directo a su producto, sin
                // obligar a elegir sugerencia (mismo flujo que Buscar código).
                SkuBusqueda = t;
                OnPropertyChanged(nameof(SkuBusqueda));
                await BuscarSkuAsync();
                return;
            }
        }
        catch
        {
            // Si la resolución exacta falla, se sigue con búsqueda por texto.
        }

        TextoBusqueda = t;
        OnPropertyChanged(nameof(TextoBusqueda));
        _pagina = 0;
        OnPropertyChanged(nameof(Pagina));
        OnPropertyChanged(nameof(PaginaVisible));
        await BuscarAsync();
    }

    private async Task InicializarAsync()
    {
        if (_estadoInicializacion == EstadoInicializacion.Inicializada) return;
        if (_estadoInicializacion == EstadoInicializacion.Inicializando) return;

        _estadoInicializacion = EstadoInicializacion.Inicializando;
        await BuscarAsync();
        // BuscarAsync no propaga: informa el fallo en Error. Solo se marca
        // Inicializada si la carga terminó sin error; si falló queda Fallida
        // y una navegación posterior puede reintentar.
        if (Error is not null)
        {
            _estadoInicializacion = EstadoInicializacion.Fallida;
            return;
        }

        if (Filas.Count > 0)
            FilaSeleccionada = Filas[0];
        _estadoInicializacion = EstadoInicializacion.Inicializada;
    }

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
            var lista = await _servicios.ConsultasCatalogo.BuscarResumenAsync(TextoBusqueda ?? string.Empty, _tamanoPagina, _pagina * _tamanoPagina, sesion);
            HaySiguiente = lista.Count > _tamanoPagina;
            Productos.Clear();
            Filas.Clear();
            SetProperty(ref _filaSeleccionada, null, nameof(FilaSeleccionada));
            foreach (var r in lista.Take(_tamanoPagina))
            {
                Productos.Add(new ProductoRegistrado(r.Id, r.CodigoInterno, r.Nombre, r.Marca, r.Categoria, r.CategoriaId, r.Activo));
                Filas.Add(new FilaCatalogo(r));
            }

            Mensaje = null;
            OnPropertyChanged(nameof(Resumen));
            OnPropertyChanged(nameof(EsUltimaPagina));
            OnPropertyChanged(nameof(HayAnterior));
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task BuscarSkuAsync()
    {
        if (Ocupado || string.IsNullOrWhiteSpace(SkuBusqueda)) { Error = "Ingresá un código interno."; return; }
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            var sesion = _idSesion();
            var v = await _servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(SkuBusqueda.Trim(), sesion);
            if (v is null) { Error = "Código interno no encontrado."; return; }
            var ficha = await _servicios.ConsultasCatalogo.ObtenerFichaAsync(v.ProductoId, DateTime.UtcNow.ToString("o"), sesion);
            Ficha = ficha;
            var prod = ficha?.Producto;
            if (prod is not null)
            {
                Productos.Clear();
                Filas.Clear();
                SetProperty(ref _filaSeleccionada, null, nameof(FilaSeleccionada));
                Productos.Add(prod);
                Filas.Add(new FilaCatalogo(prod, ficha!.Variantes.Count, ficha.Variantes.Sum(x => x.Existencias)));
                HaySiguiente = false;
                ProductoSeleccionado = prod;
            }
            Mensaje = $"Código {v.CodigoInterno} encontrado.";
            OnPropertyChanged(nameof(Resumen));
            OnPropertyChanged(nameof(EsUltimaPagina));
            OnPropertyChanged(nameof(HayAnterior));
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CambiarPaginaAsync(int delta)
    {
        if (delta > 0 && EsUltimaPagina) return;
        var nueva = _pagina + delta;
        if (nueva < 0) return;
        _pagina = nueva;
        OnPropertyChanged(nameof(Pagina));
        OnPropertyChanged(nameof(PaginaVisible));
        await BuscarAsync();
    }

    private Task AbrirFichaAsync()
    {
        Error = null;
        if (ProductoSeleccionado is null) { Error = "Seleccioná un producto."; return Task.CompletedTask; }
        if (_dialogos is null) { Error = "Apertura de ficha no disponible."; return Task.CompletedTask; }
        _dialogos.MostrarFichaProducto(ProductoSeleccionado.Id, () => BuscarAsync(), SeleccionarCreadoAsync);
        return Task.CompletedTask;
    }

    private Task NuevaFichaAsync()
    {
        Error = null;
        if (_dialogos is null) { Error = "Alta de producto no disponible."; return Task.CompletedTask; }
        _dialogos.MostrarFichaProducto(null, () => BuscarAsync(), SeleccionarCreadoAsync);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Tras cerrar el wizard con un producto recién creado, lo deja
    /// seleccionado en el master ya refrescado (sin consultas nuevas).
    /// </summary>
    private Task SeleccionarCreadoAsync(string productoId)
    {
        var fila = Filas.FirstOrDefault(f => f.Producto.Id == productoId);
        if (fila is not null)
            FilaSeleccionada = fila;
        return Task.CompletedTask;
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

    /// <summary>
    /// Resuelve el texto libre heredado a categoría controlada por nombre;
    /// null = el caso de uso asigna "Otros". Las categorías controladas
    /// reemplazan el texto libre para productos nuevos (la ficha usa ComboBox).
    /// </summary>
    private async Task<string?> ResolverCategoriaIdAsync(string? nombre)
    {
        var n = NullSiVacio(nombre);
        if (n is null) return null;
        try
        {
            var cats = await _servicios.ConsultasCatalogo.ListarCategoriasAsync(_idSesion(), true);
            return cats.FirstOrDefault(c => string.Equals(c.Nombre, n, StringComparison.OrdinalIgnoreCase))?.Id;
        }
        catch
        {
            return null;
        }
    }

    private async Task CrearProductoAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null;
        if (string.IsNullOrWhiteSpace(NuevoProductoNombre)) { Error = "Nombre obligatorio."; return; }
        Ocupado = true;
        try
        {
            var catId = await ResolverCategoriaIdAsync(NuevoProductoCategoria);
            var id = await _servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto(NuevoProductoNombre.Trim(), NullSiVacio(NuevoProductoMarca), catId), _idSesion());
            Mensaje = "Producto creado.";
            NuevoProductoNombre = string.Empty; NuevoProductoMarca = null; NuevoProductoCategoria = null;
            OnPropertyChanged(nameof(NuevoProductoNombre)); OnPropertyChanged(nameof(NuevoProductoMarca)); OnPropertyChanged(nameof(NuevoProductoCategoria));
            await BuscarAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex, "crear productos"); }
        finally { Ocupado = false; }
    }

    private async Task EditarProductoAsync()
    {
        if (ProductoSeleccionado is null) { Error = "Seleccioná un producto."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            var catId = await ResolverCategoriaIdAsync(NuevoProductoCategoria) ?? ProductoSeleccionado.CategoriaId;
            await _servicios.CasoEditarProducto.EjecutarAsync(ProductoSeleccionado.Id, new EdicionProducto(NuevoProductoNombre.Trim(), NullSiVacio(NuevoProductoMarca), catId), _idSesion());
            Mensaje = "Producto editado.";
            await BuscarAsync();
            await VerFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex, "editar productos"); }
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
        catch (Exception ex) { Error = MensajeError(ex, "desactivar productos"); }
        finally { Ocupado = false; }
    }

    private async Task CrearVarianteAsync()
    {
        if (ProductoSeleccionado is null) { Error = "Seleccioná un producto para su variante."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            // Código interno automático: la usuaria no lo escribe.
            var id = await _servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(ProductoSeleccionado.Id, NullSiVacio(NuevoVarianteTalle), NullSiVacio(NuevoVarianteColor)), _idSesion());
            Mensaje = "Variante creada (código interno automático).";
            NuevoVarianteTalle = null; NuevoVarianteColor = null;
            OnPropertyChanged(nameof(NuevoVarianteTalle)); OnPropertyChanged(nameof(NuevoVarianteColor));
            await VerFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex, "crear variantes"); }
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
            Mensaje = "Variante editada (talle/color). El código interno no se reasigna.";
            await VerFichaAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex, "editar variantes"); }
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
        catch (Exception ex) { Error = MensajeError(ex, "desactivar variantes"); }
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
        catch (Exception ex) { Error = MensajeError(ex, "fijar precios"); }
        finally { Ocupado = false; }
    }

    public Task BuscarAsyncPublic() => BuscarAsync();
    public Task VerFichaAsyncPublic() => VerFichaAsync();
    public Task AbrirFichaAsyncPublic() => AbrirFichaAsync();
    public Task NuevaFichaAsyncPublic() => NuevaFichaAsync();
    public Task InicializarAsyncPublic() => InicializarAsync();
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
            if (!long.TryParse(f.CantidadTexto.Trim(), out var cant) || cant <= 0) { Error = $"Cantidad inválida para código {f.Sku}. Debe ser entero >0."; return; }
            // Resolver variante por código interno exacto (ingreso rápido / lector)
            try
            {
                var vari = await _servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(f.Sku.Trim(), _idSesion());
                if (vari is null) { Error = $"Código interno no encontrado: {f.Sku}."; return; }
                if (!vistos.Add(vari.Id)) { Error = $"Código repetido en grilla: {f.Sku}."; return; }
                f.VarianteId = vari.Id;
                f.Detalle = $"{vari.CodigoInterno} — {vari.Talle ?? "sin talle"}/{vari.Color ?? "sin color"}";
                filas.Add(new FilaCargaInicial(vari.Id, cant));
            }
            catch (Exception ex) { Error = MensajeError(ex); return; }
        }
        if (filas.Count == 0) { Error = "Agregá al menos una fila válida (código y cantidad)."; return; }
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
                ? "Esta carga ya quedó registrada; no se volvió a crear."
                : MensajeError(ex, "cargar stock inicial") + " (sin éxito parcial; si el resultado es incierto, reintentá con las mismas filas)";
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
