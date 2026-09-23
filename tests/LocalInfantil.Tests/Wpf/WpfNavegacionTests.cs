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
/// Navegación por barra superior con menús y Perfil (piloto visual, bases
/// temporales). Verifica que cada opción lleva a su pantalla real y que
/// Cerrar sesión invalida la sesión de verdad.
/// </summary>
public sealed class WpfNavegacionTests
{
    [Fact]
    public async Task Duena_arranca_en_portada_y_navega_a_cada_pantalla()
    {
        using var e = await Entorno.CrearAsync();
        var shell = e.CrearShellDuena();

        Assert.IsType<PortadaViewModel>(shell.VistaActual);
        Assert.Contains("duena", shell.Portada.Bienvenida);

        shell.MostrarVistaCommand.Execute(shell.Catalogo);
        Assert.Same(shell.Catalogo, shell.VistaActual);
        shell.MostrarVistaCommand.Execute(shell.Proveedores);
        Assert.Same(shell.Proveedores, shell.VistaActual);
        shell.MostrarVistaCommand.Execute(shell.Recepcion);
        Assert.Same(shell.Recepcion, shell.VistaActual);
        shell.MostrarVistaCommand.Execute(shell.Recepciones);
        Assert.Same(shell.Recepciones, shell.VistaActual);
        shell.MostrarVistaCommand.Execute(shell.Conteo);
        Assert.Same(shell.Conteo, shell.VistaActual);
        shell.MostrarVistaCommand.Execute(shell.Apartado);
        Assert.Same(shell.Apartado, shell.VistaActual);
        shell.MostrarVistaCommand.Execute(shell.Administracion);
        Assert.Same(shell.Administracion, shell.VistaActual);
        shell.MostrarVistaCommand.Execute(shell.Portada);
        Assert.Same(shell.Portada, shell.VistaActual);
    }

    [Fact]
    public async Task Parametro_invalido_no_cambia_la_vista()
    {
        using var e = await Entorno.CrearAsync();
        var shell = e.CrearShellDuena();
        var actual = shell.VistaActual;

        shell.MostrarVistaCommand.Execute("texto cualquiera");
        Assert.Same(actual, shell.VistaActual);
        shell.MostrarVistaCommand.Execute(null);
        Assert.Same(actual, shell.VistaActual);
    }

    [Fact]
    public async Task Empleada_sin_administracion_y_con_portada_propia()
    {
        using var e = await Entorno.CrearAsync();
        var anaId = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        Assert.NotEmpty(anaId);
        var sesion = await e.Login.EjecutarAsync("ana", "clave-ana-00000000");
        var shell = e.CrearShellEmpleada(sesion);

        Assert.IsType<PortadaViewModel>(shell.VistaActual);
        Assert.Contains("dueña", shell.Portada.NotaRol.ToLowerInvariant());
        shell.MostrarVistaCommand.Execute(shell.Conteo);
        Assert.Same(shell.Conteo, shell.VistaActual);
    }

    [Fact]
    public async Task Perfil_cerrar_sesion_invalida_la_sesion_real()
    {
        using var e = await Entorno.CrearAsync();
        var shell = e.CrearShellDuena();
        var idSesion = e.SesionDuena;

        Assert.NotNull(e.Servicios.Gestor.Obtener(idSesion));
        await shell.CerrarSesion();
        Assert.True(e.SesionCerrada);
        Assert.Null(e.Servicios.Gestor.Obtener(idSesion));
    }

    private sealed class Entorno : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public CrearUsuario Alta { get; }
        public IniciarSesion Login { get; }
        public string SesionDuena { get; }
        public string DuenaId { get; }
        public bool SesionCerrada { get; private set; }
        private readonly string _dir;

        private Entorno(string dir, ServiciosComerciales servicios, CrearUsuario alta, IniciarSesion login, string sesion, string duenaId)
        {
            _dir = dir;
            Servicios = servicios;
            Alta = alta;
            Login = login;
            SesionDuena = sesion;
            DuenaId = duenaId;
        }

        public ShellDuenaViewModel CrearShellDuena()
        {
            var id = SesionDuena;
            Func<string?> sesion = () => id;
            return new ShellDuenaViewModel(
                Servicios.Gestor.Obtener(id)!,
                new AdministracionUsuariosViewModel(Servicios, sesion!),
                new CatalogoViewModel(Servicios, sesion),
                new ProveedoresViewModel(Servicios, sesion),
                new RecepcionViewModel(Servicios, sesion),
                new RecepcionesConsultaViewModel(Servicios, sesion),
                new ConteoViewModel(Servicios, sesion),
                new ApartadoViewModel(Servicios, sesion),
                async () =>
                {
                    await Servicios.CasoCerrarSesion.EjecutarAsync(id);
                    SesionCerrada = true;
                });
        }

        public ShellEmpleadaViewModel CrearShellEmpleada(SesionUsuario sesionUsuario)
        {
            Func<string?> sesion = () => sesionUsuario.IdSesion;
            return new ShellEmpleadaViewModel(
                sesionUsuario,
                new CatalogoViewModel(Servicios, sesion),
                new ProveedoresViewModel(Servicios, sesion),
                new RecepcionViewModel(Servicios, sesion),
                new RecepcionesConsultaViewModel(Servicios, sesion),
                new ConteoViewModel(Servicios, sesion),
                new ApartadoViewModel(Servicios, sesion),
                async () =>
                {
                    await Servicios.CasoCerrarSesion.EjecutarAsync(sesionUsuario.IdSesion);
                    SesionCerrada = true;
                });
        }

        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "navegacion.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var alta = servicios.CasoCrearUsuario;
            var login = servicios.CasoIniciarSesion;
            var duenaId = await alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-00000000", null);
            var sesion = await login.EjecutarAsync("duena", "clave-duena-00000000");
            return new Entorno(dir, servicios, alta, login, sesion.IdSesion, duenaId);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
