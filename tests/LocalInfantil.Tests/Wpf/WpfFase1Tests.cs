using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Infrastructure.Seguridad;
using LocalInfantil.Tests.Fakes;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// Fase 1 visual: comportamiento que puede romperse (comandos, navegación
/// por selector, visibilidad y no-duplicación de confirmaciones). Sin
/// comparar colores ni posiciones de píxeles.
/// </summary>
public sealed class WpfFase1Tests
{
    [Fact]
    public async Task Catalogo_permiso_denegado_con_mensaje_humano()
    {
        using var e = await Entorno.CrearAsync();
        var anaId = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        Assert.NotEmpty(anaId);
        var svcVm = new ServiciosComerciales(e.Ruta);
        var sesAna = await svcVm.CasoIniciarSesion.EjecutarAsync("ana", "clave-ana-00000000");
        var vm = new CatalogoViewModel(svcVm, () => sesAna.IdSesion);
        vm.NuevoProductoNombre = "Remera";
        await vm.CrearProductoAsyncPublic();
        Assert.Contains("No tenés permiso para crear productos", vm.Error ?? "");
        Assert.Contains("Administración", vm.Error ?? "");
    }

    [Fact]
    public async Task Apartado_selector_muestra_un_solo_panel()
    {
        using var e = await Entorno.CrearAsync();
        var svcVm = new ServiciosComerciales(e.Ruta);
        var sesVm = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new ApartadoViewModel(svcVm, () => sesVm.IdSesion);

        Assert.Equal("Apartar", vm.AccionSeleccionada);
        Assert.True(vm.PanelApartarVisible);
        Assert.False(vm.PanelReincorporarVisible);
        Assert.False(vm.PanelBajaVisible);

        vm.SeleccionarAccionCommand.Execute("Reincorporar");
        Assert.True(vm.PanelReincorporarVisible);
        Assert.False(vm.PanelApartarVisible);
        Assert.False(vm.PanelBajaVisible);

        vm.SeleccionarAccionCommand.Execute("Baja");
        Assert.True(vm.PanelBajaVisible);
        Assert.False(vm.PanelApartarVisible);
        Assert.False(vm.PanelReincorporarVisible);

        vm.SeleccionarAccionCommand.Execute("Inexistente");
        Assert.Equal("Baja", vm.AccionSeleccionada);
        Assert.True(vm.PanelBajaVisible);
    }

    [Fact]
    public async Task Apartado_confirmar_sin_solicitar_no_ejecuta()
    {
        using var e = await Entorno.CrearAsync();
        var svcVm = new ServiciosComerciales(e.Ruta);
        var sesVm = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new ApartadoViewModel(svcVm, () => sesVm.IdSesion);

        await vm.ConfirmarBajaAsyncPublic();
        Assert.False(vm.ConfirmacionBajaVisible);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Conteo_aprobar_sin_solicitar_no_ejecuta()
    {
        using var e = await Entorno.CrearAsync();
        var svcVm = new ServiciosComerciales(e.Ruta);
        var sesVm = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new ConteoViewModel(svcVm, () => sesVm.IdSesion);

        await vm.AprobarAsyncPublic();
        Assert.False(vm.ConfirmacionAprobarVisible);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task IrAApartado_preselecciona_accion_no_vendible()
    {
        using var e = await Entorno.CrearAsync();
        var svcVm = new ServiciosComerciales(e.Ruta);
        var sesVm = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        Func<string> id = () => sesVm.IdSesion;
        var shell = new ShellDuenaViewModel(
            sesVm,
            new AdministracionUsuariosViewModel(svcVm, id),
            new CatalogoViewModel(svcVm, id),
            new ProveedoresViewModel(svcVm, id),
            new RecepcionViewModel(svcVm, id),
            new RecepcionesConsultaViewModel(svcVm, id),
            new ConteoViewModel(svcVm, id),
            new ApartadoViewModel(svcVm, id),
            () => Task.CompletedTask);

        shell.IrAApartadoCommand.Execute("Apartar");
        Assert.Same(shell.Apartado, shell.VistaActual);
        Assert.Equal("Apartar", shell.Apartado.AccionSeleccionada);

        shell.MostrarVistaCommand.Execute(shell.Catalogo);
        shell.IrAApartadoCommand.Execute(null);
        Assert.Same(shell.Apartado, shell.VistaActual);

        shell.IrAApartadoCommand.Execute("Inexistente");
        Assert.Same(shell.Apartado, shell.VistaActual);
        Assert.Equal("Apartar", shell.Apartado.AccionSeleccionada);
    }

    [Fact]
    public async Task Cerrar_sesion_invoca_hook_sin_login_embebido()
    {
        using var e = await Entorno.CrearAsync();
        var svc = new ServiciosComerciales(e.Ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var root = new AppRootViewModel(svc, e.Ruta, null, new InicioViewModel());
        await root.AutenticadoAsync(ses);
        Assert.IsType<ShellDuenaViewModel>(root.Contenido);

        var hookInvocado = false;
        root.AlCerrarSesion = () => { hookInvocado = true; return Task.CompletedTask; };
        await root.CerrarSesionAsync();

        Assert.True(hookInvocado);
        Assert.Null(root.Sesion);
        Assert.Null(svc.Gestor.Obtener(ses.IdSesion));
        Assert.IsNotType<LoginViewModel>(root.Contenido);
    }

    [Fact]
    public async Task Paginacion_muestra_desde_1_y_no_0()
    {
        using var e = await Entorno.CrearAsync();
        var svcVm = new ServiciosComerciales(e.Ruta);
        var sesVm = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new CatalogoViewModel(svcVm, () => sesVm.IdSesion);
        Assert.Equal(1, vm.PaginaVisible);
        var vmConteo = new ConteoViewModel(svcVm, () => sesVm.IdSesion);
        Assert.Equal(0, vmConteo.Desplazamiento);
    }

    private sealed class Entorno : IDisposable
    {
        public string Ruta { get; }
        public CrearUsuario Alta { get; }
        public IniciarSesion Login { get; }
        public string SesionDuena { get; }
        public string DuenaId { get; }
        private readonly string _dir;

        private Entorno(string dir, string ruta, CrearUsuario alta, IniciarSesion login, string sesion, string duenaId)
        {
            _dir = dir;
            Ruta = ruta;
            Alta = alta;
            Login = login;
            SesionDuena = sesion;
            DuenaId = duenaId;
        }

        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "fase1.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var ids = new GeneradorIds();
            var reloj = new RelojFijo(DateTimeOffset.Parse("2026-09-22T00:00:00Z"));
            var sesiones = new GestorSesiones();
            var usuarios = new RepositorioUsuarios(ids);
            var permisos = new RepositorioPermisos();
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
            var matriz = MatrizPermisos.ConValoresConfirmados();
            var autorizador = new Autorizador(sesiones, usuarios, permisos, fabrica, matriz);
            var hasher = new Pbkdf2HashContrasenas();
            var alta = new CrearUsuario(usuarios, hasher, autorizador, fabrica, auditoria, ids, reloj);
            var login = new IniciarSesion(usuarios, hasher, sesiones, fabrica, auditoria, ids, reloj, new OpcionesAutenticacion(0));
            var duenaId = await alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await login.EjecutarAsync("duena", "clave-duena-000000");
            return new Entorno(dir, ruta, alta, login, sesion.IdSesion, duenaId);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
