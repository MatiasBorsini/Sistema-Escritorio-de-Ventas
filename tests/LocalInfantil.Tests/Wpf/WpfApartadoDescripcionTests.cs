using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// IteraciÃ³n Inventario: identidad comercial de la variante y ausencia del
/// mensaje redundante "FÃ­sico N = ...". Sin tocar reglas de stock.
/// </summary>
public sealed class WpfApartadoDescripcionTests
{
    private sealed class Entorno : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        public string Codigo { get; }
        private readonly string _dir;

        private Entorno(string dir, ServiciosComerciales servicios, string sesion, string codigo)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
            Codigo = codigo;
        }

        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "apartado-desc.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            foreach (var permiso in new[]
            {
                PermisosCatalogo.ProductosGestionar,
                PermisosCatalogo.VariantesGestionar,
                PermisosCatalogo.PreciosGestionar,
                PermisosInventario.CargaInicialEstablecer,
            })
            {
                await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, permiso, true, sesion.IdSesion);
            }

            var prod = await servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Buzo canguro", null, "cat-rem"), sesion.IdSesion);
            var v1 = await servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "L", "Azul"), sesion.IdSesion);
            await servicios.CasoFijarPrecio.EjecutarAsync(v1, 10000, sesion.IdSesion);
            await servicios.CasoCargarExistenciaInicial.EjecutarAsync(
                new[] { new FilaCargaInicial(v1, 5) },
                Guid.NewGuid().ToString("N"),
                sesion.IdSesion);
            var codigo = (await servicios.ConsultasCatalogo.ObtenerVarianteAsync(v1, sesion.IdSesion))!.CodigoInterno;
            return new Entorno(dir, servicios, sesion.IdSesion, codigo);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task Buscar_muestra_descripcion_comercial_y_sin_frase_redundante()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ApartadoViewModel(e.Servicios, () => e.Sesion);
        vm.SkuBusqueda = e.Codigo;
        await vm.BuscarAsyncPublic();

        Assert.Null(vm.Error);
        Assert.NotNull(vm.Existencia);
        Assert.Equal(e.Codigo, vm.Existencia!.Sku);
        Assert.Equal("Buzo canguro · L · Azul", vm.DescripcionVariante);
        Assert.Null(vm.Mensaje);
    }

    [Fact]
    public async Task Descripcion_omite_talle_color_nulos_y_nula_sin_existencia()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ApartadoViewModel(e.Servicios, () => e.Sesion);
        Assert.Null(vm.DescripcionVariante);
        vm.Existencia = new ExistenciaClasificada("v", "X-1", 1, 1, 0, "Remera", null, null);
        Assert.Equal("Remera", vm.DescripcionVariante);
        vm.Existencia = new ExistenciaClasificada("v", "X-1", 1, 1, 0, "Remera", "M", null);
        Assert.Equal("Remera · M", vm.DescripcionVariante);
    }

    [Fact]
    public async Task Errores_se_presentan_junto_al_campo_sin_cambiar_validacion()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ApartadoViewModel(e.Servicios, () => e.Sesion);
        vm.Error = "Cantidad inválida: entero mayor a cero.";
        Assert.Equal(vm.Error, vm.ErrorCantidad);
        Assert.Null(vm.ErrorMotivo);
        Assert.Null(vm.ErrorGeneral);
        vm.Error = "El motivo es obligatorio.";
        Assert.Equal(vm.Error, vm.ErrorMotivo);
        Assert.Null(vm.ErrorCantidad);
        vm.Error = "Código interno no encontrado.";
        Assert.Equal(vm.Error, vm.ErrorGeneral);
        vm.Error = null;
        Assert.Null(vm.ErrorGeneral);
        Assert.Null(vm.ErrorCantidad);
        Assert.Null(vm.ErrorMotivo);
    }
}
