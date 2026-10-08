using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// Iteración Conteo: selección diferenciada, CTA contextual y estado humano
/// de conteos guardados sin detalles de paginación técnica.
/// </summary>
public sealed class WpfConteoSeleccionTests
{
    private static string RaizRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "LocalInfantil.sln"))) return dir.FullName;
        throw new InvalidOperationException("No se encontró la raíz del repositorio.");
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
            var ruta = Path.Combine(dir, "conteo-sel.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.ProductosGestionar, true, sesion.IdSesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.VariantesGestionar, true, sesion.IdSesion);
            return new Entorno(dir, servicios, sesion.IdSesion);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task Cta_deshabilitado_sin_elegidas_y_dinamico_con_elegidas()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        Assert.Equal(0, vm.VariantesElegidas);
        Assert.False(vm.PuedeContinuarPaso1);
        Assert.Equal("Continuar al conteo →", vm.TextoContinuarPaso1);

        vm.FilasSeleccion[0].Sku = "BUZ-001-01";
        Assert.Equal(1, vm.VariantesElegidas);
        Assert.True(vm.PuedeContinuarPaso1);
        Assert.Equal("Continuar con 1 variante →", vm.TextoContinuarPaso1);

        vm.AgregarFilaCommand.Execute(null);
        vm.FilasSeleccion[1].Sku = "  ";
        Assert.Equal(1, vm.VariantesElegidas);
        vm.FilasSeleccion[1].Sku = "REM-002-03";
        Assert.Equal(2, vm.VariantesElegidas);
        Assert.Equal("Continuar con 2 variantes →", vm.TextoContinuarPaso1);
    }

    [Fact]
    public async Task Variantes_seleccionadas_se_pueden_quitar_y_no_se_duplican()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        var variante = new SugerenciaVariante("var-1", "prod-1", "BUZ-001-01", "Buzo Nube", null, null, "4", "Azul", true);
        var fila = vm.FilasSeleccion[0];
        fila.AplicarSugerencia(variante);

        Assert.Single(vm.VariantesSeleccionadas);
        Assert.Equal("Buzo Nube", fila.NombreProducto);
        Assert.Equal("4", fila.Talle);
        Assert.Equal("Azul", fila.Color);

        vm.AgregarVarianteCommand.Execute(variante);
        Assert.Single(vm.VariantesSeleccionadas);
        Assert.Contains("ya está en la lista", vm.Error ?? string.Empty);

        vm.QuitarVarianteSeleccionadaCommand.Execute(fila);
        Assert.Empty(vm.VariantesSeleccionadas);
        Assert.False(vm.PuedeContinuarPaso1);
    }

    [Fact]
    public async Task Buscar_agregar_tres_variantes_limpia_sugerencias_y_conserva_seleccionadas()
    {
        using var e = await Entorno.CrearAsync();
        var producto = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Buzo Nube", null, "cat-otr"), e.Sesion);
        await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(producto, "4", "Azul"), e.Sesion);
        await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(producto, "6", "Verde"), e.Sesion);
        await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(producto, "8", "Rojo"), e.Sesion);

        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        var agregadas = new HashSet<string>(StringComparer.Ordinal);
        for (var indice = 0; indice < 3; indice++)
        {
            await vm.MotorBusquedaPaso1.TextoCambiadoAsync("buz");
            Assert.True(vm.MotorBusquedaPaso1.Abierto);
            Assert.Equal(3, vm.MotorBusquedaPaso1.Sugerencias.Count);
            var sugerencia = vm.MotorBusquedaPaso1.Sugerencias[indice];

            vm.AgregarVarianteCommand.Execute(sugerencia);

            Assert.Equal(string.Empty, vm.MotorBusquedaPaso1.Texto);
            Assert.False(vm.MotorBusquedaPaso1.Abierto);
            Assert.Empty(vm.MotorBusquedaPaso1.Sugerencias);
            Assert.True(agregadas.Add(sugerencia.VarianteId));
            Assert.Equal(indice + 1, vm.VariantesSeleccionadas.Count);
            Assert.Contains(vm.VariantesSeleccionadas, fila => fila.VarianteId == sugerencia.VarianteId);
        }

        await vm.MotorBusquedaPaso1.TextoCambiadoAsync(string.Empty);
        Assert.Empty(vm.MotorBusquedaPaso1.Sugerencias);
        Assert.Equal(3, vm.VariantesSeleccionadas.Count);

        var xaml = File.ReadAllText(Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", "ConteoView.xaml"));
        Assert.Contains("Modo=\"VarianteAgregar\"", xaml);
        Assert.Contains("LimpiarAlSeleccionar=\"True\"", xaml);
        Assert.Contains("PlacementTarget=\"{Binding ElementName=Caja}\"", File.ReadAllText(Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", "BuscadorAsistido.xaml")));
        Assert.Contains("Dispatcher.BeginInvoke", File.ReadAllText(Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", "BuscadorAsistido.xaml.cs")));
    }

    [Fact]
    public async Task Estado_conteos_guardados_es_humano_sin_desplazamiento_tecnico()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new ConteoViewModel(e.Servicios, () => e.Sesion);
        await vm.CargarConteosAsync();
        Assert.Null(vm.Error);
        Assert.Equal("Todavía no hay conteos guardados.", vm.EstadoPaginaConteos);
        await vm.SiguienteAsyncPublic();
        Assert.Equal(0, vm.Desplazamiento);
        Assert.DoesNotContain("desplazamiento", vm.EstadoPaginaConteos);
        Assert.DoesNotContain("offset", vm.EstadoPaginaConteos, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("página", vm.EstadoPaginaConteos, StringComparison.OrdinalIgnoreCase);
    }
}
