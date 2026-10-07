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
    private string? _nuevoVarianteTalle;
    private string? _nuevoVarianteColor;
    private string? _editarTalle;
    private string? _editarColor;
    private bool _mostrarFormularioAlta;
    private string? _errorVariantes;
    private string? _mensajeVariantes;
    private string? _errorEstado;
    private string? _mensajeEstado;
    private string _precioTexto = string.Empty;
    private string _operacionIdCarga = string.Empty;
    private IReadOnlyList<FilaCargaInicial>? _ultimasFilasEnviadas;
    private IReadOnlyDictionary<string, int>? _cantidadesCargaConfirmada;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;

    public FichaProductoViewModel(ServiciosComerciales servicios, Func<string?> idSesion, string? productoId)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        _productoId = productoId;
        ModoCreacion = productoId is null;
        _pasoCreacion = 1;
        FilasCarga = new ObservableCollection<FilaCargaViewModel> { new(), new() };
        _operacionIdCarga = servicios.GeneradorIds.NuevoId();
        SeleccionarSolapaCommand = new RelayCommand(SeleccionarSolapa);
        SolicitarAltaCommand = new RelayCommand(_ => { ErrorVariantes = null; MensajeVariantes = null; VarianteSeleccionada = null; MostrarFormularioAlta = true; });
        CancelarAltaCommand = new RelayCommand(_ => { MostrarFormularioAlta = false; });
        EstablecerStockInicialCommand = new AsyncRelayCommand(EstablecerStockInicialAsync);
        IrAPasoCreacionCommand = new RelayCommand(async p => await IrAPasoCreacionAsync(p is string s && int.TryParse(s, out var n) ? n : 0));
        TerminarCommand = new RelayCommand(_ => Terminar());
        SeguirCompletandoCommand = new RelayCommand(_ => ConfirmacionCierreVisible = false);
        ConfirmarCierreCommand = new RelayCommand(_ => ConfirmarCierre());
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

    /// <summary>
    /// Modo wizard: la ventana se abrió desde + Nuevo producto. La navegación
    /// de edición completa aparece solo en ficha normal (no creación).
    /// </summary>
    public bool ModoCreacion { get; }
    public bool EtiquetasCargaInicialDisponibles => _cantidadesCargaConfirmada is { Count: > 0 };

    /// <summary>Id creado en este wizard (para seleccionar en Catálogo al cerrar).</summary>
    public string? ProductoIdCreado => ModoCreacion ? _productoId : null;

    private int _pasoCreacion;

    /// <summary>Pasos de creación: 1 Producto → 2 Variantes → 3 Listo.</summary>
    public int PasoCreacion
    {
        get => _pasoCreacion;
        private set
        {
            if (value < 1 || value > 3 || value == _pasoCreacion) return;
            _pasoCreacion = value;
            OnPropertyChanged(nameof(PasoCreacion));
            OnPropertyChanged(nameof(PasoC1Activo));
            OnPropertyChanged(nameof(PasoC2Activo));
            OnPropertyChanged(nameof(PasoC3Activo));
            OnPropertyChanged(nameof(PasoC1Listo));
            OnPropertyChanged(nameof(PasoC2Listo));
            OnPropertyChanged(nameof(MostrarContinuarCreacion));
            OnPropertyChanged(nameof(PasoC3Visible));
        }
    }

    public bool PasoC1Activo => PasoCreacion == 1;
    public bool PasoC2Activo => PasoCreacion == 2;
    public bool PasoC3Activo => PasoCreacion == 3;
    public bool MostrarContinuarCreacion => ModoCreacion && PasoCreacion == 2;
    public bool PasoC1Listo => PasoCreacion > 1;
    public bool PasoC2Listo => PasoCreacion > 2;
    public bool PasoC3Visible => ModoCreacion && PasoCreacion == 3;

    /// <summary>En creación no se muestra Estado; Variantes recién al crear.</summary>
    public bool MostrarNavVariantes => !ModoCreacion || _productoId is not null;
    public bool MostrarNavEstado => !ModoCreacion;

    /// <summary>Resumen del paso Listo (datos reales ya cargados, sin lógica nueva).</summary>
    public int CantVariantesResumen => Ficha?.Variantes.Count ?? 0;
    public long? StockTotalResumen => Ficha?.Variantes.Sum(v => v.Existencias);
    public string MarcaCategoriaResumen
    {
        get
        {
            var partes = new[] { Marca, Categoria }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            return partes.Length == 0 ? "—" : string.Join(" · ", partes);
        }
    }

    /// <summary>Cierre con producto persistido pero sin variantes: confirma.</summary>
    public bool ConfirmacionCierreVisible { get => _confirmacionCierreVisible; set => SetProperty(ref _confirmacionCierreVisible, value); }
    private bool _confirmacionCierreVisible;
    public bool CierreConfirmado { get; private set; }

    public bool RequiereConfirmacionCierre =>
        ModoCreacion && !CierreConfirmado && _productoId is not null && (Ficha?.Variantes.Count ?? 0) == 0;

    /// <summary>Code-behind cierra la ventana al invocarse.</summary>
    public event Action? SolicitarCierre;
    public ObservableCollection<FilaCargaViewModel> FilasCarga { get; }
    public FichaProducto? Ficha { get => _ficha; private set => SetProperty(ref _ficha, value); }

    public FichaVariante? VarianteSeleccionada
    {
        get => _varianteSeleccionada;
        set
        {
            if (SetProperty(ref _varianteSeleccionada, value))
            {
                EditarTalle = value?.Talle;
                EditarColor = value?.Color;
                if (value is not null)
                    MostrarFormularioAlta = false;
                OnPropertyChanged(nameof(HayVarianteSeleccionada));
                OnPropertyChanged(nameof(SkuSeleccionado));
                OnPropertyChanged(nameof(TalleColorSeleccionado));
                OnPropertyChanged(nameof(EstadoSeleccionado));
                OnPropertyChanged(nameof(PrecioVigenteSel));
                OnPropertyChanged(nameof(MostrarVacioDetalle));
                OnPropertyChanged(nameof(MostrarDetalleVariante));
            }
        }
    }

    /// <summary>Modos excluyentes del panel derecho (master/detail).</summary>
    public bool MostrarVacioDetalle => !HayVarianteSeleccionada && !MostrarFormularioAlta;
    public bool MostrarDetalleVariante => HayVarianteSeleccionada && !MostrarFormularioAlta;

    /// <summary>Encabezado de la variante seleccionada (solo lectura).</summary>
    public bool HayVarianteSeleccionada => VarianteSeleccionada is not null;
    public string SkuSeleccionado => VarianteSeleccionada?.CodigoInterno ?? "—";
    public string CodigoSeleccionado => VarianteSeleccionada?.CodigoInterno ?? "—";
    public string TalleColorSeleccionado => VarianteSeleccionada?.TalleColorTexto ?? "—";
    public string EstadoSeleccionado => VarianteSeleccionada is null ? "—" : VarianteSeleccionada.Activa ? "Activa" : "Inactiva";
    public long? PrecioVigenteSel => VarianteSeleccionada?.PrecioMinor;
    public string Nombre { get => _nombre; set => SetProperty(ref _nombre, value); }
    public string? Marca { get => _marca; set => SetProperty(ref _marca, value); }
    public string? Categoria { get => _categoria; set => SetProperty(ref _categoria, value); }
    /// <summary>Categorías controladas para el ComboBox (reemplazan texto libre).</summary>
    public ObservableCollection<CategoriaRegistrada> Categorias { get; } = new();
    private CategoriaRegistrada? _categoriaSeleccionada;
    public CategoriaRegistrada? CategoriaSeleccionada
    {
        get => _categoriaSeleccionada;
        set
        {
            if (SetProperty(ref _categoriaSeleccionada, value))
                Categoria = value?.Nombre;
        }
    }
    /// <summary>Código interno de la última variante creada (solo lectura).</summary>
    public string? UltimoCodigoVarianteCreado { get => _ultimoCodigoVarianteCreado; set => SetProperty(ref _ultimoCodigoVarianteCreado, value); }
    private string? _ultimoCodigoVarianteCreado;
    public string? NuevoVarianteTalle { get => _nuevoVarianteTalle; set => SetProperty(ref _nuevoVarianteTalle, value); }
    public string? NuevoVarianteColor { get => _nuevoVarianteColor; set => SetProperty(ref _nuevoVarianteColor, value); }

    /// <summary>Campos de edición (separados de los de alta; se precargan al seleccionar).</summary>
    public string? EditarTalle { get => _editarTalle; set => SetProperty(ref _editarTalle, value); }
    public string? EditarColor { get => _editarColor; set => SetProperty(ref _editarColor, value); }

    /// <summary>Progressive disclosure: el formulario de alta solo al pulsarlo.</summary>
    public bool MostrarFormularioAlta
    {
        get => _mostrarFormularioAlta;
        set
        {
            if (SetProperty(ref _mostrarFormularioAlta, value))
            {
                OnPropertyChanged(nameof(MostrarVacioDetalle));
                OnPropertyChanged(nameof(MostrarDetalleVariante));
            }
        }
    }

    private string _cantidadInicialTexto = string.Empty;

    /// <summary>Cantidad para establecer stock inicial de la seleccionada (sin reingresar el código).</summary>
    public string CantidadInicialTexto { get => _cantidadInicialTexto; set => SetProperty(ref _cantidadInicialTexto, value); }

    /// <summary>Mensajes de la sección Variantes (no contaminan otras solapas).</summary>
    public string? ErrorVariantes { get => _errorVariantes; set => SetProperty(ref _errorVariantes, value); }
    public string? MensajeVariantes { get => _mensajeVariantes; set => SetProperty(ref _mensajeVariantes, value); }

    /// <summary>Mensajes de la sección Estado.</summary>
    public string? ErrorEstado { get => _errorEstado; set => SetProperty(ref _errorEstado, value); }
    public string? MensajeEstado { get => _mensajeEstado; set => SetProperty(ref _mensajeEstado, value); }
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
    public RelayCommand SolicitarAltaCommand { get; }
    public RelayCommand CancelarAltaCommand { get; }
    public AsyncRelayCommand EstablecerStockInicialCommand { get; }

    public Task EstablecerStockInicialAsyncPublic() => EstablecerStockInicialAsync();

    /// <summary>
    /// Existencia inicial de la variante seleccionada sin reingresar el código:
    /// reutiliza el caso existente con una colección de una sola fila
    /// (misma validación, idempotencia y semántica ESTABLECE).
    /// </summary>
    private async Task EstablecerStockInicialAsync()
    {
        if (VarianteSeleccionada is null) { ErrorVariantes = "Seleccioná una variante de la tabla para establecer su stock inicial."; return; }
        if (Ocupado) return;
        ErrorVariantes = null; MensajeVariantes = null;
        FilasCarga.Clear();
        FilasCarga.Add(new FilaCargaViewModel { Sku = VarianteSeleccionada.CodigoInterno, CantidadTexto = CantidadInicialTexto });
        await EjecutarCargaAsync();
        if (ErrorVariantes is null)
            CantidadInicialTexto = string.Empty;
    }
    public RelayCommand IrAPasoCreacionCommand { get; }
    public RelayCommand TerminarCommand { get; }
    public RelayCommand SeguirCompletandoCommand { get; }
    public RelayCommand ConfirmarCierreCommand { get; }

    public Task IrAPasoCreacionAsyncPublic(int paso) => IrAPasoCreacionAsync(paso);

    /// <summary>
    /// Navegación del wizard: 2 solo con producto creado; 3 recarga el
    /// resumen. Volver (3→2, 2→1) siempre permitido.
    /// </summary>
    public async Task IrAPasoCreacionAsync(int paso)
    {
        if (!ModoCreacion || paso < 1 || paso > 3) return;
        if (paso == 2 && _productoId is null) return;
        if (paso == 3)
        {
            if (_productoId is null) return;
            await RecargarFichaAsync();
            OnPropertyChanged(nameof(CantVariantesResumen));
            OnPropertyChanged(nameof(StockTotalResumen));
            OnPropertyChanged(nameof(MarcaCategoriaResumen));
        }

        PasoCreacion = paso;
    }

    /// <summary>
    /// Decisión de cierre para el code-behind (Closing) y Terminar: si el
    /// producto ya fue persistido pero no tiene variantes, muestra la
    /// confirmación humana en lugar de cerrar. No prohíbe cerrar.
    /// </summary>
    public bool AlIntentarCerrar()
    {
        if (CierreConfirmado) return true;
        if (RequiereConfirmacionCierre)
        {
            ConfirmacionCierreVisible = true;
            return false;
        }

        return true;
    }

    public void Terminar()
    {
        if (!AlIntentarCerrar()) return;
        ConfirmarCierre();
    }

    public void ConfirmarCierre()
    {
        CierreConfirmado = true;
        ConfirmacionCierreVisible = false;
        SolicitarCierre?.Invoke();
    }
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
    public MatrizVariantesViewModel CrearMatrizVariantesViewModel()
        => new(_servicios, _idSesion, _productoId ?? throw new InvalidOperationException("Primero guardá el producto."));
    public EtiquetasViewModel CrearEtiquetasViewModel(IReadOnlyDictionary<string, int>? cantidadesIniciales = null)
        => new(_servicios, _idSesion, _productoId ?? throw new InvalidOperationException("Primero guardá el producto."), cantidadesIniciales);
    public EtiquetasViewModel CrearEtiquetasCargaInicialViewModel()
        => new(_servicios, _idSesion, _cantidadesCargaConfirmada ?? new Dictionary<string, int>());
    public Task RecargarFichaAsyncPublic() => RecargarFichaAsync();
    public Task GuardarDatosAsyncPublic() => GuardarDatosAsync();
    public Task CrearVarianteAsyncPublic() => CrearVarianteAsync();
    public Task EditarVarianteAsyncPublic() => EditarVarianteAsync();
    public Task FijarPrecioAsyncPublic() => FijarPrecioAsync();
    public Task DesactivarProductoAsyncPublic() => DesactivarProductoAsync();
    public Task EjecutarCargaAsyncPublic() => EjecutarCargaAsync();

    /// <summary>
    /// Carga las categorías controladas para el ComboBox. Sin default
    /// automático: en creación la usuaria debe elegir explícitamente
    /// (incluso "Otros"); si no, el guardado informa "Elegí una categoría.".
    /// </summary>
    public async Task CargarCategoriasAsync()
    {
        try
        {
            var lista = await _servicios.ConsultasCatalogo.ListarCategoriasAsync(_idSesion(), true);
            Categorias.Clear();
            foreach (var c in lista) Categorias.Add(c);
            if (Ficha?.Producto.CategoriaId is string cid)
                CategoriaSeleccionada = Categorias.FirstOrDefault(c => c.Id == cid);
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    public async Task InicializarAsync()
    {
        await CargarCategoriasAsync();
        if (EsCreacion) return;
        await RecargarFichaAsync();
        if (Ficha is not null)
        {
            Nombre = Ficha.Producto.Nombre;
            Marca = Ficha.Producto.Marca;
            Categoria = Ficha.Producto.Categoria;
            CategoriaSeleccionada = Categorias.FirstOrDefault(c => c.Id == Ficha.Producto.CategoriaId);
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
            // Feedback temporal por contexto: al cambiar de sección no
            // arrastrar mensajes de otra tarea.
            Error = null; Mensaje = null;
            ErrorVariantes = null; MensajeVariantes = null;
            ErrorEstado = null; MensajeEstado = null;
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
            OnPropertyChanged(nameof(CantVariantesResumen));
            OnPropertyChanged(nameof(StockTotalResumen));
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
                if (CategoriaSeleccionada is null)
                {
                    if (Categorias.Count == 0)
                        await CargarCategoriasAsync();
                    if (CategoriaSeleccionada is null) { Error = "Seleccioná una categoría."; return; }
                }

                _productoId = await _servicios.CasoCrearProducto.EjecutarAsync(
                    new NuevoProducto(Nombre.Trim(), NullSiVacio(Marca), CategoriaSeleccionada.Id), _idSesion());
                OnPropertyChanged(nameof(EsCreacion));
                OnPropertyChanged(nameof(SolapasSecundariasHabilitadas));
                OnPropertyChanged(nameof(ProductoId));
                await RecargarFichaAsync();
                Solapa = "Variantes";
                PasoCreacion = 2;
                OnPropertyChanged(nameof(MostrarNavVariantes));
                OnPropertyChanged(nameof(CantVariantesResumen));
                OnPropertyChanged(nameof(StockTotalResumen));
                OnPropertyChanged(nameof(MarcaCategoriaResumen));
                MensajeVariantes = "Producto creado. Ahora podés agregar variantes.";
            }
            else
            {
                await _servicios.CasoEditarProducto.EjecutarAsync(
                    _productoId!, new EdicionProducto(Nombre.Trim(), NullSiVacio(Marca), CategoriaSeleccionada?.Id ?? Ficha?.Producto.CategoriaId), _idSesion());
                Mensaje = "Producto editado.";
                await RecargarFichaAsync();
            }
        }
        catch (Exception ex) { Error = MensajeError(ex, EsCreacion ? "crear productos" : "editar productos"); }
        finally { Ocupado = false; }
    }

    private async Task CrearVarianteAsync()
    {
        if (_productoId is null) { ErrorVariantes = "Creá primero el producto."; return; }
        if (Ocupado) return;
        ErrorVariantes = null; MensajeVariantes = null; Ocupado = true;
        try
        {
            // Código interno automático: la usuaria solo indica talle/color.
            var id = await _servicios.CasoCrearVariante.EjecutarAsync(
                new NuevaVariante(_productoId, NullSiVacio(NuevoVarianteTalle), NullSiVacio(NuevoVarianteColor)), _idSesion());
            NuevoVarianteTalle = null; NuevoVarianteColor = null;
            OnPropertyChanged(nameof(NuevoVarianteTalle)); OnPropertyChanged(nameof(NuevoVarianteColor));
            MostrarFormularioAlta = false;
            await RecargarFichaAsync();
            VarianteSeleccionada = Ficha?.Variantes.FirstOrDefault(v => v.Id == id);
            UltimoCodigoVarianteCreado = VarianteSeleccionada?.CodigoInterno;
            MensajeVariantes = VarianteSeleccionada is null
                ? "Variante creada."
                : $"Variante creada con código interno {VarianteSeleccionada.CodigoInterno}.";
        }
        catch (Exception ex) { ErrorVariantes = MensajeError(ex, "crear variantes"); }
        finally { Ocupado = false; }
    }

    private async Task EditarVarianteAsync()
    {
        if (VarianteSeleccionada is null) { ErrorVariantes = "Seleccioná una variante de la tabla para editarla."; return; }
        if (Ocupado) return;
        ErrorVariantes = null; MensajeVariantes = null; Ocupado = true;
        try
        {
            var id = VarianteSeleccionada.Id;
            await _servicios.CasoEditarVariante.EjecutarAsync(
                id, new EdicionVariante(NullSiVacio(EditarTalle), NullSiVacio(EditarColor)), _idSesion());
            MensajeVariantes = "Cambios guardados.";
            await RecargarFichaAsync();
            VarianteSeleccionada = Ficha?.Variantes.FirstOrDefault(v => v.Id == id);
        }
        catch (Exception ex) { ErrorVariantes = MensajeError(ex, "editar variantes"); }
        finally { Ocupado = false; }
    }

    private async Task DesactivarVarianteAsync()
    {
        if (VarianteSeleccionada is null) { ErrorVariantes = "Seleccioná una variante de la tabla para desactivarla."; return; }
        if (Ocupado) return;
        ErrorVariantes = null; MensajeVariantes = null; Ocupado = true;
        try
        {
            await _servicios.CasoDesactivarVariante.EjecutarAsync(VarianteSeleccionada.Id, _idSesion());
            MensajeVariantes = "Variante desactivada (historial conservado).";
            await RecargarFichaAsync();
        }
        catch (Exception ex) { ErrorVariantes = MensajeError(ex, "desactivar variantes"); }
        finally { Ocupado = false; }
    }

    private async Task FijarPrecioAsync()
    {
        if (VarianteSeleccionada is null) { ErrorVariantes = "Seleccioná una variante de la tabla para actualizar su precio."; return; }
        if (Ocupado) return;
        if (!TryParseArs(PrecioTexto, out var minor)) { ErrorVariantes = "Precio ARS inválido. Ej: 1234,50"; return; }
        ErrorVariantes = null; MensajeVariantes = null; Ocupado = true;
        try
        {
            var id = VarianteSeleccionada.Id;
            await _servicios.CasoFijarPrecio.EjecutarAsync(id, minor, _idSesion());
            MensajeVariantes = $"Precio actualizado a {CatalogoViewModel.FormatearArs(minor)} (historial conservado).";
            PrecioTexto = string.Empty;
            OnPropertyChanged(nameof(PrecioTexto));
            await RecargarFichaAsync();
            VarianteSeleccionada = Ficha?.Variantes.FirstOrDefault(v => v.Id == id);
        }
        catch (Exception ex) { ErrorVariantes = MensajeError(ex, "fijar precios"); }
        finally { Ocupado = false; }
    }

    private async Task DesactivarProductoAsync()
    {
        if (_productoId is null) { ErrorEstado = "Creá primero el producto."; return; }
        if (Ocupado) return;
        ErrorEstado = null; MensajeEstado = null; Ocupado = true;
        try
        {
            await _servicios.CasoDesactivarProducto.EjecutarAsync(_productoId, _idSesion());
            MensajeEstado = "Producto desactivado.";
            await RecargarFichaAsync();
        }
        catch (Exception ex) { ErrorEstado = MensajeError(ex, "desactivar productos"); }
        finally { Ocupado = false; }
    }

    private async Task EjecutarCargaAsync()
    {
        if (_productoId is null) { ErrorVariantes = "Creá primero el producto y sus variantes."; return; }
        if (Ocupado) return;
        ErrorVariantes = null; MensajeVariantes = null;
        var filas = new List<FilaCargaInicial>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in FilasCarga)
        {
            if (string.IsNullOrWhiteSpace(f.Sku)) continue;
            if (!long.TryParse(f.CantidadTexto.Trim(), out var cant) || cant <= 0) { ErrorVariantes = $"Cantidad inválida para código {f.Sku}. Debe ser entero >0."; return; }
            try
            {
                var vari = await _servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(f.Sku.Trim(), _idSesion());
                if (vari is null) { ErrorVariantes = $"Código interno no encontrado: {f.Sku}."; return; }
                if (!vistos.Add(vari.Id)) { ErrorVariantes = $"Código repetido en grilla: {f.Sku}."; return; }
                f.VarianteId = vari.Id;
                f.Detalle = $"{vari.CodigoInterno} — {vari.Talle ?? "sin talle"}/{vari.Color ?? "sin color"}";
                filas.Add(new FilaCargaInicial(vari.Id, cant));
            }
            catch (Exception ex) { ErrorVariantes = MensajeError(ex); return; }
        }
        if (filas.Count == 0) { ErrorVariantes = "Agregá al menos una fila válida (código y cantidad)."; return; }
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
            MensajeVariantes = $"Existencia inicial establecida: {filas.Count} variante(s). No es recepción ni suma.";
            _cantidadesCargaConfirmada = filas.ToDictionary(f => f.VarianteId, f => (int)Math.Clamp(f.Cantidad, 0, int.MaxValue));
            OnPropertyChanged(nameof(EtiquetasCargaInicialDisponibles));
            _operacionIdCarga = _servicios.GeneradorIds.NuevoId();
            OnPropertyChanged(nameof(OperacionIdCarga));
            _ultimasFilasEnviadas = null;
            await RecargarFichaAsync();
        }
        catch (Exception ex)
        {
            _ultimasFilasEnviadas = filas;
            ErrorVariantes = ex is InvalidOperationException && ex.Message.Contains("ya fue registrada")
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
