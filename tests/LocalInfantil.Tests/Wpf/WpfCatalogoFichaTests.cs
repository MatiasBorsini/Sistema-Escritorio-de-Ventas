using System.Reflection;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// E2 Catálogo: apertura de ficha (creación/edición) vía diálogos fake,
/// flujo de alta ordenado por el dominio y registro anti-doble-editor.
/// </summary>
public sealed class WpfCatalogoFichaTests
{
    [Fact]
    public async Task Abrir_ficha_edicion_registra_id()
    {
        using var e = await Entorno.CrearAsync();
        var pid = await e.CrearProductoAsync("Remera Ficha", "cat-rem");
        var vm = new CatalogoViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.BuscarAsyncPublic();
        Assert.NotEmpty(vm.Filas);
        vm.FilaSeleccionada = vm.Filas.First(f => f.Producto.Id == pid);
        await vm.AbrirFichaAsyncPublic();
        Assert.Equal(pid, e.Dialogos.UltimoId);
        Assert.False(e.Dialogos.UltimaFueCreacion);
    }

    [Fact]
    public async Task Abrir_sin_seleccion_no_llama_dialogo()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new CatalogoViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.BuscarAsyncPublic();
        await vm.AbrirFichaAsyncPublic();
        Assert.False(e.Dialogos.Llamado);
        Assert.Contains("Seleccioná", vm.Error ?? "");
    }

    [Fact]
    public async Task Ficha_creacion_bloquea_solapas_secundarias()
    {
        using var e = await Entorno.CrearAsync();
        var ficha = new FichaProductoViewModel(e.Servicios, () => e.Sesion, null);
        Assert.True(ficha.EsCreacion);
        Assert.Equal("Datos", ficha.Solapa);
        ficha.SeleccionarSolapaCommand.Execute("Variantes");
        Assert.Equal("Datos", ficha.Solapa);
    }

    [Fact]
    public async Task Nueva_ficha_desde_master_registra_creacion()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new CatalogoViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.NuevaFichaAsyncPublic();
        Assert.True(e.Dialogos.Llamado);
        Assert.True(e.Dialogos.UltimaFueCreacion);
        Assert.Null(e.Dialogos.UltimoId);
    }

    [Fact]
    public async Task Ficha_creacion_respeta_orden_producto_antes_que_variantes()
    {
        using var e = await Entorno.CrearAsync();
        var ficha = new FichaProductoViewModel(e.Servicios, () => e.Sesion, null);
        Assert.True(ficha.EsCreacion);
        Assert.False(ficha.SolapasSecundariasHabilitadas);

        await ficha.CrearVarianteAsyncPublic();
        Assert.Contains("Creá primero", ficha.ErrorVariantes ?? "");

        ficha.Nombre = "Buzo Nuevo";
        await ficha.GuardarDatosAsyncPublic();
        Assert.Equal("Seleccioná una categoría.", ficha.Error);
        Assert.Null(ficha.ProductoId);

        // "Otros" no se asigna solo: la elección es explícita.
        await ficha.CargarCategoriasAsync();
        Assert.Null(ficha.CategoriaSeleccionada);
        ficha.CategoriaSeleccionada = ficha.Categorias.First(c => c.Id == "cat-buz");
        await ficha.GuardarDatosAsyncPublic();
        Assert.Null(ficha.Error);
        Assert.NotNull(ficha.ProductoId);
        Assert.False(ficha.EsCreacion);
        Assert.True(ficha.SolapasSecundariasHabilitadas);
        Assert.Equal("Variantes", ficha.Solapa);
        Assert.Contains("agregar variantes", ficha.MensajeVariantes ?? "");

        // Código interno automático: crear sin escribir código funciona.
        ficha.SolicitarAltaCommand.Execute(null);
        Assert.True(ficha.MostrarFormularioAlta);
        await ficha.CrearVarianteAsyncPublic();
        Assert.Null(ficha.ErrorVariantes);
        Assert.False(ficha.MostrarFormularioAlta);
        Assert.NotNull(ficha.Ficha);
        var creada = Assert.Single(ficha.Ficha!.Variantes);
        Assert.False(string.IsNullOrWhiteSpace(creada.CodigoInterno));
        Assert.Equal(creada.CodigoInterno, ficha.SkuSeleccionado);
        Assert.NotNull(ficha.UltimoCodigoVarianteCreado);

        ficha.PrecioTexto = "100,00";
        await ficha.FijarPrecioAsyncPublic();
        Assert.Null(ficha.ErrorVariantes);
        Assert.Contains("100", ficha.PrecioVigenteSel?.ToString() ?? "");
    }

    [Fact]
    public async Task Ficha_seleccion_precarga_edicion_y_errores_van_a_seccion()
    {
        using var e = await Entorno.CrearAsync();
        var pid = await e.CrearProductoAsync("Remera Sel", "cat-rem");
        var ficha = new FichaProductoViewModel(e.Servicios, () => e.Sesion, pid);
        await ficha.InicializarAsyncPublic();
        Assert.False(ficha.HayVarianteSeleccionada);

        await ficha.EditarVarianteAsyncPublic();
        Assert.Contains("Seleccioná una variante", ficha.ErrorVariantes ?? "");
        Assert.Null(ficha.Error);

        ficha.SolicitarAltaCommand.Execute(null);
        ficha.NuevoVarianteTalle = "M";
        ficha.NuevoVarianteColor = "Rojo";
        await ficha.CrearVarianteAsyncPublic();
        Assert.False(string.IsNullOrWhiteSpace(ficha.SkuSeleccionado));
        Assert.NotEqual("—", ficha.SkuSeleccionado);
        Assert.Equal("M · Rojo", ficha.TalleColorSeleccionado);
        Assert.Equal("Activa", ficha.EstadoSeleccionado);
        Assert.Equal("M", ficha.EditarTalle);
        Assert.Equal("Rojo", ficha.EditarColor);
    }

    [Fact]
    public async Task Ficha_edicion_guarda_y_desactiva()
    {
        using var e = await Entorno.CrearAsync();
        var pid = await e.CrearProductoAsync("Remera Edit", "cat-rem");
        var ficha = new FichaProductoViewModel(e.Servicios, () => e.Sesion, pid);
        await ficha.InicializarAsyncPublic();
        Assert.NotNull(ficha.Ficha);
        Assert.Equal("Remera Edit", ficha.Nombre);

        ficha.Nombre = "Remera Editada";
        await ficha.GuardarDatosAsyncPublic();
        Assert.Null(ficha.Error);
        Assert.Equal("Remera Editada", ficha.Ficha!.Producto.Nombre);

        await ficha.DesactivarProductoAsyncPublic();
        Assert.Null(ficha.Error);
        Assert.False(ficha.Ficha!.Producto.Activo);
    }

    [Fact]
    public async Task Paginacion_peek_detecta_fin_en_multiplo_exacto()
    {
        using var e = await Entorno.CrearAsync();
        for (int i = 0; i < 4; i++) await e.CrearProductoAsync($"Pag-{i:D2}");
        var vm = new CatalogoViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        vm.TextoBusqueda = "Pag-";
        vm.TamanoPagina = 2;
        await EsperarAsync(() => !vm.Ocupado && vm.Filas.Count == 2 && vm.Resumen.Contains("Pag-"));
        Assert.Equal(2, vm.Filas.Count);
        Assert.True(vm.HaySiguiente);
        Assert.False(vm.EsUltimaPagina);

        var primeroPagina1 = vm.Filas[0].Producto.Id;
        vm.SiguienteCommand.Execute(null);
        await EsperarAsync(() => !vm.Ocupado && vm.Filas.Count == 2 && vm.Filas[0].Producto.Id != primeroPagina1);
        Assert.Equal(2, vm.Filas.Count);
        Assert.False(vm.HaySiguiente);
        Assert.True(vm.EsUltimaPagina);
    }

    [Fact]
    public async Task Master_muestra_agregados_reales_en_una_lectura()
    {
        using var e = await Entorno.CrearAsync();
        var pid = await e.CrearProductoAsync("Agregado");
        var v1 = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(pid, "M", null), e.Sesion);
        var v2 = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(pid, null, null), e.Sesion);
        await e.Servicios.CasoCargarExistenciaInicial.EjecutarAsync(
            new[] { new FilaCargaInicial(v1, 5), new FilaCargaInicial(v2, 7) }, Guid.NewGuid().ToString("N"), e.Sesion);
        var vm = new CatalogoViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        vm.TextoBusqueda = "Agregado";
        await vm.BuscarAsyncPublic();
        var fila = Assert.Single(vm.Filas);
        Assert.Equal(2, fila.CantidadVariantes);
        Assert.Equal(12, fila.StockTotal);
    }

    private static async Task EsperarAsync(Func<bool> listo)
    {
        for (var t = 0; t < 100 && !listo(); t++) await Task.Delay(50);
    }

    [Fact]
    public async Task Wizard_creacion_avanza_y_cierra_con_confirmacion()
    {
        using var e = await Entorno.CrearAsync();
        var ficha = new FichaProductoViewModel(e.Servicios, () => e.Sesion, null);
        Assert.True(ficha.ModoCreacion);
        Assert.Equal(1, ficha.PasoCreacion);
        Assert.False(ficha.MostrarNavVariantes);
        Assert.False(ficha.MostrarNavEstado);

        await ficha.IrAPasoCreacionAsyncPublic(2);
        Assert.Equal(1, ficha.PasoCreacion);

        ficha.Nombre = "Wizard Uno";
        await ficha.CargarCategoriasAsync();
        ficha.CategoriaSeleccionada = ficha.Categorias.First(c => c.Id == "cat-otr");
        await ficha.GuardarDatosAsyncPublic();
        Assert.Equal(2, ficha.PasoCreacion);
        Assert.True(ficha.MostrarNavVariantes);
        Assert.False(ficha.MostrarNavEstado);
        Assert.Contains("agregar variantes", ficha.MensajeVariantes ?? "");

        // Cerrar sin variantes: no cierra, muestra confirmación humana.
        Assert.True(ficha.RequiereConfirmacionCierre);
        Assert.False(ficha.AlIntentarCerrar());
        Assert.True(ficha.ConfirmacionCierreVisible);
        var cerro = false;
        ficha.SolicitarCierre += () => cerro = true;
        ficha.SeguirCompletandoCommand.Execute(null);
        Assert.False(ficha.ConfirmacionCierreVisible);
        Assert.False(cerro);

        // Con una variante ya no requiere confirmación.
        await ficha.CrearVarianteAsyncPublic();
        Assert.False(ficha.RequiereConfirmacionCierre);
        Assert.True(ficha.AlIntentarCerrar());

        // Paso Listo con resumen real.
        await ficha.IrAPasoCreacionAsyncPublic(3);
        Assert.Equal(3, ficha.PasoCreacion);
        Assert.Equal("Wizard Uno", ficha.Nombre);
        Assert.Equal(1, ficha.CantVariantesResumen);

        // Terminar pide cierre y el catálogo puede seleccionarlo.
        ficha.TerminarCommand.Execute(null);
        Assert.True(cerro);
        Assert.NotNull(ficha.ProductoIdCreado);
        var vm = new CatalogoViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await e.Dialogos.InformarCreadoAsync(ficha.ProductoIdCreado!);
        Assert.Equal(ficha.ProductoIdCreado, e.Dialogos.CreadoInformado);
        _ = vm;
    }

    [Fact]
    public async Task Establecer_stock_inicial_sin_reingresar_codigo()
    {
        using var e = await Entorno.CrearAsync();
        var pid = await e.CrearProductoAsync("Remera Stock", "cat-rem");
        var ficha = new FichaProductoViewModel(e.Servicios, () => e.Sesion, pid);
        await ficha.InicializarAsyncPublic();
        ficha.SolicitarAltaCommand.Execute(null);
        await ficha.CrearVarianteAsyncPublic();
        var codigo = ficha.SkuSeleccionado;
        Assert.False(string.IsNullOrWhiteSpace(codigo));

        await ficha.EstablecerStockInicialAsyncPublic();
        Assert.Contains("Cantidad", ficha.ErrorVariantes ?? "");

        ficha.CantidadInicialTexto = "7";
        await ficha.EstablecerStockInicialAsyncPublic();
        Assert.Null(ficha.ErrorVariantes);
        Assert.Contains("establecida", (ficha.MensajeVariantes ?? "").ToLowerInvariant());
        Assert.Equal(7, ficha.Ficha!.Variantes.First(v => v.CodigoInterno == codigo).Existencias);
        Assert.Equal("", ficha.CantidadInicialTexto);
        Assert.True(ficha.EtiquetasCargaInicialDisponibles);
        var etiquetas = ficha.CrearEtiquetasCargaInicialViewModel();
        await etiquetas.InicializarAsync();
        Assert.Equal(7, Assert.Single(etiquetas.Filas).Cantidad);
        Assert.Equal(7, (await e.Servicios.ConsultasInventario.ObtenerStockVendibleAsync(ficha.Ficha.Variantes.Single().Id, e.Sesion))!.Disponible);
    }

    [Fact]
    public async Task Catalogo_selecciona_recien_creado_al_cerrar_ficha()
    {
        using var e = await Entorno.CrearAsync();
        var pid = await e.CrearProductoAsync("Recién Creado");
        var vm = new CatalogoViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.BuscarAsyncPublic();
        Assert.Empty(vm.Filas.Where(f => f.Producto.Id == pid && vm.FilaSeleccionada == f));
        var metodo = typeof(CatalogoViewModel).GetMethod("SeleccionarCreadoAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)metodo.Invoke(vm, [pid])!;
        Assert.NotNull(vm.FilaSeleccionada);
        Assert.Equal(pid, vm.FilaSeleccionada!.Producto.Id);
        Assert.NotNull(vm.Ficha);
    }

    [Fact]
    public void Registro_ventanas_evita_doble_editor()
    {
        var registro = new RegistroVentanasAbiertas();
        Assert.True(registro.IntentarAbrir("abc"));
        Assert.False(registro.IntentarAbrir("abc"));
        Assert.True(registro.EstaAbierta("abc"));
        Assert.True(registro.IntentarAbrir(DialogosCatalogoWpf.ClaveCreacion));
        registro.Liberar("abc");
        Assert.False(registro.EstaAbierta("abc"));
        Assert.True(registro.IntentarAbrir("abc"));
    }

    private sealed class DialogosFake : IDialogosCatalogo
    {
        public bool Llamado { get; private set; }
        public string? UltimoId { get; private set; }
        public bool UltimaFueCreacion { get; private set; }
        public string? CreadoInformado { get; private set; }

        public void MostrarFichaProducto(string? productoId, Func<Task>? alCerrar = null, Func<string, Task>? alProductoCreado = null)
        {
            Llamado = true;
            UltimoId = productoId;
            UltimaFueCreacion = productoId is null;
        }

        public Task InformarCreadoAsync(string id)
        {
            CreadoInformado = id;
            return Task.CompletedTask;
        }
    }

    private sealed class Entorno : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        public DialogosFake Dialogos { get; } = new();
        private readonly string _dir;

        private Entorno(string dir, ServiciosComerciales servicios, string sesion)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
        }

        public Task<string> CrearProductoAsync(string nombre, string categoriaId = "cat-otr")
            => Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto(nombre, null, categoriaId), Sesion);

        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "ficha.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            Assert.NotEmpty(duenaId);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            var e = new Entorno(dir, servicios, sesion.IdSesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.ProductosGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.VariantesGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.PreciosGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosInventario.CargaInicialEstablecer, true, e.Sesion);
            return e;
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
