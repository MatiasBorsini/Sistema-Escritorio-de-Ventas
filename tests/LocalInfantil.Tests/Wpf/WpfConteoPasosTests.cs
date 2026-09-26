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
/// E4-A Conteo: stepper PasoActual con validaciones de avance (atrás libre,
/// adelante validado, clic solo retrocede) y resumen del paso 4.
/// </summary>
public sealed class WpfConteoPasosTests
{
    [Fact]
    public async Task Paso1_exige_variante_y_no_avanza_vacio()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        Assert.Equal(1, vm.PasoActual);
        Assert.True(vm.Paso1Visible);
        await vm.SiguientePasoAsyncPublic();
        Assert.Equal(1, vm.PasoActual);
        Assert.Contains("al menos una variante", vm.Error ?? "");
    }

    [Fact]
    public async Task Flujo_1_2_3_con_borrador_real()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        vm.FilasSeleccion[0].Sku = "STP-01";
        vm.FilasSeleccion[0].CantidadTexto = "5";
        await vm.SiguientePasoAsyncPublic();
        Assert.Equal(2, vm.PasoActual);
        Assert.True(vm.Paso2Activo);
        Assert.True(vm.Paso1Listo);

        await vm.SiguientePasoAsyncPublic();
        Assert.Equal(3, vm.PasoActual);
        Assert.NotNull(vm.Seleccionado);
        await EsperarAsync(() => vm.Informe.Count == 1);
        Assert.Equal("STP-01", vm.Informe[0].Sku);

        await vm.SiguientePasoAsyncPublic();
        Assert.Equal(4, vm.PasoActual);
        Assert.Equal(1, vm.VariantesContadas);
    }

    [Fact]
    public async Task Paso4_resume_diferencias()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        vm.FilasSeleccion[0].Sku = "STP-01";
        vm.FilasSeleccion[0].CantidadTexto = "5";
        await vm.SiguientePasoAsyncPublic();
        await vm.SiguientePasoAsyncPublic();
        await EsperarAsync(() => vm.Informe.Count == 1);
        await vm.SiguientePasoAsyncPublic();
        // stock inicial 10, contado 5 → 1 con ajuste, 0 sin diferencia
        Assert.Equal(1, vm.VariantesContadas);
        Assert.Equal(0, vm.SinDiferencia);
        Assert.Equal(1, vm.ConAjuste);
    }

    [Fact]
    public async Task Stepper_solo_retrocede_y_atras_es_libre()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        vm.FilasSeleccion[0].Sku = "STP-01";
        vm.FilasSeleccion[0].CantidadTexto = "5";
        await vm.SiguientePasoAsyncPublic();
        Assert.Equal(2, vm.PasoActual);

        vm.IrAPasoCommand.Execute("4");
        Assert.Equal(2, vm.PasoActual);
        Assert.Contains("paso actual", vm.Error ?? "");

        vm.IrAPasoCommand.Execute("1");
        Assert.Equal(1, vm.PasoActual);
        Assert.Null(vm.Error);
    }

    private static async Task EsperarAsync(Func<bool> listo)
    {
        for (var t = 0; t < 100 && !listo(); t++) await Task.Delay(50);
    }

    private sealed class Entorno : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        private readonly string _dir;

        private Entorno(string dir, ServiciosComerciales servicios, string sesion)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
        }

        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "pasos.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            var e = new Entorno(dir, servicios, sesion.IdSesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.ProductosGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.VariantesGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosInventario.CargaInicialEstablecer, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosInventario.ConteoRegistrar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosInventario.ConteoAprobar, true, e.Sesion);
            var pid = await servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Prod Paso", null, null), e.Sesion);
            var vid = await servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(pid, "STP-01", null, null), e.Sesion);
            await servicios.CasoCargarExistenciaInicial.EjecutarAsync(new[] { new FilaCargaInicial(vid, 10) }, Guid.NewGuid().ToString("N"), e.Sesion);
            return e;
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
