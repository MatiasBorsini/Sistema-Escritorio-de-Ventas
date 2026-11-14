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
        vm.FilasSeleccion[0].Sku = e.Codigo;
        vm.FilasSeleccion[0].CantidadTexto = "5";
        await vm.SiguientePasoAsyncPublic();
        Assert.Equal(2, vm.PasoActual);
        Assert.True(vm.Paso2Activo);
        Assert.True(vm.Paso1Listo);

        await vm.SiguientePasoAsyncPublic();
        Assert.Equal(3, vm.PasoActual);
        Assert.NotNull(vm.Seleccionado);
        Assert.Contains("1 conteo guardado en esta página", vm.EstadoPaginaConteos);
        await EsperarAsync(() => vm.Informe.Count == 1);
        Assert.Equal(e.Codigo, vm.Informe[0].Sku);

        await vm.SiguientePasoAsyncPublic();
        Assert.Equal(4, vm.PasoActual);
        Assert.Equal(1, vm.VariantesContadas);
    }

    [Fact]
    public async Task Paso4_resume_diferencias()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        vm.FilasSeleccion[0].Sku = e.Codigo;
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
        vm.FilasSeleccion[0].Sku = e.Codigo;
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

    [Fact]
    public async Task Conteo_aprobado_no_avanza_a_una_segunda_aprobacion()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        vm.FilasSeleccion[0].Sku = e.Codigo;
        vm.FilasSeleccion[0].CantidadTexto = "5";
        await vm.SiguientePasoAsyncPublic();
        await vm.SiguientePasoAsyncPublic();
        await EsperarAsync(() => vm.Informe.Count == 1);
        Assert.Equal(3, vm.PasoActual);

        await vm.SolicitarAprobarAsyncPublic();
        await vm.AprobarAsyncPublic();
        Assert.Equal("aprobado", vm.Seleccionado?.Estado);

        await vm.SiguientePasoAsyncPublic();
        Assert.Equal(3, vm.PasoActual);
        Assert.Contains("ya está aprobado", vm.Error ?? string.Empty);
    }

    [Fact]
    public async Task Correccion_precarga_lineas_separa_estado_y_preserva_snapshot()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        vm.FilasSeleccion[0].Sku = e.Codigo;
        vm.FilasSeleccion[0].CantidadTexto = "5";
        await vm.SiguientePasoAsyncPublic();
        await vm.SiguientePasoAsyncPublic();

        await vm.CorregirAsyncPublic();

        Assert.Single(vm.LineasCorreccion);
        Assert.Equal(e.Codigo, vm.LineasCorreccion[0].Sku);
        Assert.Equal("5", vm.LineasCorreccion[0].CantidadTexto);
        Assert.NotSame(vm.FilasSeleccion[0], vm.LineasCorreccion[0]);
        Assert.Equal(10, vm.LineasCorreccion[0].Esperado);
        Assert.Equal(0, vm.CambiosCorreccion);

        await vm.SolicitarGuardarCorreccionAsyncPublic();
        Assert.False(vm.ConfirmacionCorreccionVisible);
        Assert.Contains("No hay cambios", vm.Error ?? string.Empty);

        vm.LineasCorreccion[0].CantidadTexto = "7";
        Assert.Equal(1, vm.CambiosCorreccion);
        await vm.SolicitarGuardarCorreccionAsyncPublic();
        Assert.True(vm.ConfirmacionCorreccionVisible);
        await vm.GuardarCorreccionAsyncPublic();

        Assert.Null(vm.Error);
        Assert.Equal("borrador", vm.Seleccionado?.Estado);
        await EsperarAsync(() => vm.Informe.Count == 1 && vm.Informe[0].Contado == 7);
        Assert.Equal(10, vm.Informe[0].Esperado);
        Assert.Equal(7, vm.Informe[0].Contado);
        Assert.Equal(-3, vm.Informe[0].Diferencia);
    }

    [Fact]
    public async Task Cancelar_correccion_descarta_cambios_locales()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        vm.FilasSeleccion[0].Sku = e.Codigo;
        vm.FilasSeleccion[0].CantidadTexto = "5";
        await vm.SiguientePasoAsyncPublic();
        await vm.SiguientePasoAsyncPublic();
        await vm.CorregirAsyncPublic();
        vm.LineasCorreccion[0].CantidadTexto = "7";

        await vm.CancelarCorreccionAsyncPublic();
        Assert.False(vm.ModoCorreccion);
        var informe = await e.Servicios.ConsultasConteos.InformeAsync(vm.Seleccionado!.Id, e.Sesion);
        Assert.Equal(5, informe[0].Contado);
    }

    [Fact]
    public async Task Cambiar_borrador_limpia_el_estado_de_correccion_anterior()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        vm.FilasSeleccion[0].Sku = e.Codigo;
        vm.FilasSeleccion[0].CantidadTexto = "5";
        await vm.CrearAsyncPublic();
        var primero = vm.Seleccionado!;

        vm.FilasSeleccion[0].CantidadTexto = "6";
        await vm.CrearAsyncPublic();
        var segundo = vm.Seleccionado!;
        Assert.NotEqual(primero.Id, segundo.Id);

        await vm.CorregirAsyncPublic();
        Assert.True(vm.ModoCorreccion);
        vm.LineasCorreccion[0].CantidadTexto = "7";

        vm.Seleccionado = primero;
        Assert.False(vm.ModoCorreccion);
        Assert.Empty(vm.LineasCorreccion);
    }

    private static async Task EsperarAsync(Func<bool> listo)
    {
        for (var t = 0; t < 100 && !listo(); t++) await Task.Delay(50);
    }

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
            var ruta = Path.Combine(dir, "pasos.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            var e = new Entorno(dir, servicios, sesion.IdSesion, string.Empty);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.ProductosGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.VariantesGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosInventario.CargaInicialEstablecer, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosInventario.ConteoRegistrar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosInventario.ConteoAprobar, true, e.Sesion);
            var pid = await servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Prod Paso", null, "cat-otr"), e.Sesion);
            var vid = await servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(pid, null, null), e.Sesion);
            await servicios.CasoCargarExistenciaInicial.EjecutarAsync(new[] { new FilaCargaInicial(vid, 10) }, Guid.NewGuid().ToString("N"), e.Sesion);
            var vari = await servicios.ConsultasCatalogo.ObtenerVarianteAsync(vid, e.Sesion);
            return new Entorno(dir, servicios, sesion.IdSesion, vari!.CodigoInterno);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
