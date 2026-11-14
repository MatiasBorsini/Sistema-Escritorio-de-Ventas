using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Catalogo;

/// <summary>
/// Sugerencias de variantes para typeahead con códigos internos automáticos:
/// límite, relevancia en una sola lectura (código o nombre), contexto humano
/// con categoría controlada y salto directo en Catálogo e Inventario.
/// </summary>
public sealed class SugerenciasVariantesTests
{
    [Fact]
    public async Task Buscar_limita_a_10_y_ordena_exactitud_primero()
    {
        using var e = await Entorno.CrearAsync();
        var top = await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("001-0", 10, e.Sesion);
        Assert.Equal(10, top.Count);
        Assert.All(top, s => Assert.False(string.IsNullOrWhiteSpace(s.CodigoInterno)));

        var codigoPrimera = (await e.Servicios.ConsultasCatalogo.ListarPorProductoAsync(e.P1, e.Sesion))[0].CodigoInterno;
        var exacto = await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(codigoPrimera, 10, e.Sesion);
        Assert.NotEmpty(exacto);
        Assert.Equal(codigoPrimera, exacto[0].CodigoInterno);

        var prefijo = await e.ConsultasVariantesCheckAsync(e.PrefijoP1 + "-0");
        Assert.Equal(8, prefijo.Count);
        Assert.All(prefijo, s => Assert.StartsWith(e.PrefijoP1 + "-0", s.CodigoInterno));
    }

    [Fact]
    public async Task Buscar_por_nombre_devuelve_contexto_humano()
    {
        using var e = await Entorno.CrearAsync();
        var lista = await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("Remera", 10, e.Sesion);
        Assert.NotEmpty(lista);
        var primera = lista[0];
        Assert.Equal("Remera Sug", primera.NombreProducto);
        Assert.NotEmpty(primera.VarianteId);
        Assert.NotEmpty(primera.ProductoId);
        Assert.Equal("Nube · Remeras y tops", primera.ContextoProducto);
    }

    [Fact]
    public async Task Catalogo_seleccionar_sugerencia_carga_producto_y_vista()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new CatalogoViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        var codigo = (await e.Servicios.ConsultasCatalogo.ListarPorProductoAsync(e.P1, e.Sesion))[1].CodigoInterno;
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(codigo, 10, e.Sesion)).First(s => s.CodigoInterno == codigo);
        await vm.SeleccionarSugerenciaAsyncPublic(sug);
        Assert.Null(vm.Error);
        Assert.NotNull(vm.ProductoSeleccionado);
        Assert.Equal(sug.ProductoId, vm.ProductoSeleccionado!.Id);
        Assert.NotNull(vm.Ficha);
        Assert.Single(vm.Filas);
    }

    [Fact]
    public async Task Codigo_exacto_valido_resuelve_sin_elegir_sugerencia()
    {
        using var e = await Entorno.CrearAsync();
        var codigo = (await e.Servicios.ConsultasCatalogo.ListarPorProductoAsync(e.P1, e.Sesion))[2].CodigoInterno;
        var vm = new CatalogoViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.BuscarDirectaAsyncPublic(codigo);
        Assert.Null(vm.Error);
        Assert.NotNull(vm.ProductoSeleccionado);
        Assert.Equal(e.P1, vm.ProductoSeleccionado!.Id);
    }

    [Fact]
    public async Task Inventario_seleccionar_sugerencia_carga_existencia()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ApartadoViewModel(e.Servicios, () => e.Sesion);
        var codigo = (await e.Servicios.ConsultasCatalogo.ListarPorProductoAsync(e.P1, e.Sesion))[3].CodigoInterno;
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(codigo, 10, e.Sesion)).First(s => s.CodigoInterno == codigo);
        await vm.SeleccionarSugerenciaAsyncPublic(sug);
        Assert.Null(vm.Error);
        Assert.Equal(codigo, vm.SkuBusqueda);
        Assert.NotNull(vm.Existencia);
        Assert.Equal(codigo, vm.Existencia!.Sku);
    }

    [Fact]
    public async Task Recepcion_aplicar_sugerencia_usa_codigo_real()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new RecepcionViewModel(e.Servicios, () => e.Sesion);
        var fila = vm.Filas[0];
        Assert.NotNull(fila.MotorVariantes);
        var codigo = (await e.Servicios.ConsultasCatalogo.ListarPorProductoAsync(e.P2, e.Sesion))[0].CodigoInterno;
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(codigo, 10, e.Sesion)).First(s => s.CodigoInterno == codigo);
        fila.SeleccionarSugerenciaCommand.Execute(sug);
        Assert.Equal(codigo, fila.Sku);
        Assert.NotNull(fila.VarianteId);
        Assert.Contains(codigo, fila.Detalle ?? "");
    }

    private sealed class DialogosNulos : IDialogosCatalogo
    {
        public void MostrarFichaProducto(string? productoId, Func<Task>? alCerrar = null, Func<string, Task>? alProductoCreado = null) { }
    }

    private sealed class Entorno : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        public DialogosNulos Dialogos { get; } = new();
        public string P1 { get; }
        public string P2 { get; }
        public string PrefijoP1 { get; }
        private readonly string _dir;

        private Entorno(string dir, ServiciosComerciales servicios, string sesion, string p1, string p2, string prefijoP1)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
            P1 = p1;
            P2 = p2;
            PrefijoP1 = prefijoP1;
        }

        public async Task<IReadOnlyList<SugerenciaVariante>> ConsultasVariantesCheckAsync(string texto)
            => await Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(texto, 10, Sesion);

        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "sug.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            var idSesion = sesion.IdSesion;
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.ProductosGestionar, true, idSesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.VariantesGestionar, true, idSesion);
            var p1 = await servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera Sug", "Nube", "cat-rem"), idSesion);
            var p2 = await servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Buzo Sug", null, "cat-buz"), idSesion);
            for (int i = 1; i <= 8; i++)
                await servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(p1, i.ToString(), null), idSesion);
            foreach (var talle in new[] { "S", "M", "L", "XL" })
                await servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(p2, talle, null), idSesion);
            var prod1 = await servicios.ConsultasCatalogo.ObtenerProductoAsync(p1, idSesion);
            return new Entorno(dir, servicios, idSesion, p1, p2, prod1!.CodigoInterno);
        }

        public void Dispose()
        {
            // Puede quedar una tarea de fondo del ViewModel usando la DB unos
            // instantes; reintentamos el borrado para no fallar por lock.
            for (var intento = 0; intento < 30; intento++)
            {
                try
                {
                    if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
                    return;
                }
                catch (IOException) when (intento < 29)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(200);
                }
            }
        }
    }
}
