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
        var pid = await e.CrearProductoAsync("Remera Ficha");
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
        Assert.Contains("Creá primero", ficha.Error ?? "");

        ficha.Nombre = "Buzo Nuevo";
        await ficha.GuardarDatosAsyncPublic();
        Assert.Null(ficha.Error);
        Assert.NotNull(ficha.ProductoId);
        Assert.False(ficha.EsCreacion);
        Assert.True(ficha.SolapasSecundariasHabilitadas);
        Assert.Equal("Variantes", ficha.Solapa);

        await ficha.CrearVarianteAsyncPublic();
        Assert.Contains("SKU", ficha.Error ?? "");
        ficha.NuevoVarianteSku = "FICHA-001";
        await ficha.CrearVarianteAsyncPublic();
        Assert.Null(ficha.Error);
        Assert.NotNull(ficha.Ficha);
        Assert.Contains(ficha.Ficha!.Variantes, v => v.Sku == "FICHA-001");

        ficha.VarianteSeleccionada = ficha.Ficha.Variantes.First(v => v.Sku == "FICHA-001");
        ficha.PrecioTexto = "100,00";
        await ficha.FijarPrecioAsyncPublic();
        Assert.Null(ficha.Error);
    }

    [Fact]
    public async Task Ficha_edicion_guarda_y_desactiva()
    {
        using var e = await Entorno.CrearAsync();
        var pid = await e.CrearProductoAsync("Remera Edit");
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
        var v1 = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(pid, "AG-1", "M", null), e.Sesion);
        var v2 = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(pid, "AG-2", null, null), e.Sesion);
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

        public void MostrarFichaProducto(string? productoId, Func<Task>? alCerrar = null)
        {
            Llamado = true;
            UltimoId = productoId;
            UltimaFueCreacion = productoId is null;
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

        public Task<string> CrearProductoAsync(string nombre)
            => Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto(nombre, null, null), Sesion);

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
