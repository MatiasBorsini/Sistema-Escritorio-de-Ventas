using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Infrastructure.Seguridad;
using LocalInfantil.Tests.Fakes;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Tests.Wpf;

// Bases exclusivamente temporales; nunca toca la provisional ni datos reales.
public sealed class WpfLoginOwnerUsersTests
{
    [Fact]
    public async Task Base_sin_usuarios_permite_bootstrap_Duena()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "wpf-bootstrap.db");
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var entorno = CrearEntorno(fabrica);
            var id = await entorno.Alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            Assert.NotEmpty(id);
            await using var s = await fabrica.IniciarLecturaAsync();
            var u = await new RepositorioUsuarios(new GeneradorIds()).ObtenerPorNombreAsync(s, "duena");
            Assert.NotNull(u);
            Assert.Equal(RolesLocal.Duena, u!.Rol);
            Assert.True(u.Activo);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Base_con_usuarios_rechaza_segundo_bootstrap_y_permite_login()
    {
        using var e = await Entorno.CrearAsync();
        await Assert.ThrowsAsync<SesionInvalidaException>(() =>
            e.Alta.EjecutarAsync(new NuevoUsuario("otro", RolesLocal.Duena), "clave-otro-000000", null));
        var sesion = await e.Login.EjecutarAsync("duena", "clave-duena-000000");
        Assert.NotNull(sesion);
        Assert.Equal("duena", sesion.Nombre);
    }

    [Fact]
    public async Task Contrasena_incorrecta_error_generico()
    {
        using var e = await Entorno.CrearAsync();
        await Assert.ThrowsAsync<CredencialesInvalidasException>(() =>
            e.Login.EjecutarAsync("duena", "clave-incorrecta"));
        await Assert.ThrowsAsync<CredencialesInvalidasException>(() =>
            e.Login.EjecutarAsync("inexistente", "cualquier-clave-000"));
    }

    [Fact]
    public async Task Login_con_empleado_ok()
    {
        using var e = await Entorno.CrearAsync();
        var empId = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        var sesion = await e.Login.EjecutarAsync("ana", "clave-ana-00000000");
        Assert.Equal(empId, sesion.UsuarioId);
        Assert.Equal(RolesLocal.Empleada, sesion.Rol);
    }

    [Fact]
    public async Task Logout_invalida_sesion_anterior()
    {
        using var e = await Entorno.CrearAsync();
        var sesion = await e.Login.EjecutarAsync("duena", "clave-duena-000000");
        await e.Cerrar.EjecutarAsync(sesion.IdSesion);
        await Assert.ThrowsAsync<SesionInvalidaException>(() =>
            e.Autorizador.AutorizarAsync(sesion.IdSesion, PermisosAdministracion.UsuariosAdministrar));
    }

    [Fact]
    public async Task Duena_crea_dos_empleados()
    {
        using var e = await Entorno.CrearAsync();
        var ana = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        var lucia = await e.Alta.EjecutarAsync(new NuevoUsuario("lucia", RolesLocal.Empleada), "clave-lucia-000000", e.SesionDuena);
        Assert.NotEqual(ana, lucia);
        await using var s = await e.Fabrica.IniciarLecturaAsync();
        var repo = new RepositorioUsuarios(new GeneradorIds());
        var lista = await repo.ListarTodosAsync(s);
        Assert.Equal(3, lista.Count);
        Assert.Contains(lista, x => x.Nombre == "ana");
        Assert.Contains(lista, x => x.Nombre == "lucia");
    }

    [Fact]
    public async Task Permisos_distintos_por_empleado()
    {
        using var e = await Entorno.CrearAsync();
        var ana = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        var lucia = await e.Alta.EjecutarAsync(new NuevoUsuario("lucia", RolesLocal.Empleada), "clave-lucia-000000", e.SesionDuena);
        await e.Permisos.EjecutarAsync(ana, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(lucia, PermisosInventario.AjusteStock, true, e.SesionDuena);
        var sAna = await e.Login.EjecutarAsync("ana", "clave-ana-00000000");
        var sLucia = await e.Login.EjecutarAsync("lucia", "clave-lucia-000000");
        Assert.NotNull(await e.Autorizador.AutorizarAsync(sAna.IdSesion, PermisosCatalogo.ProductosGestionar));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Autorizador.AutorizarAsync(sAna.IdSesion, PermisosInventario.AjusteStock));
        Assert.NotNull(await e.Autorizador.AutorizarAsync(sLucia.IdSesion, PermisosInventario.AjusteStock));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Autorizador.AutorizarAsync(sLucia.IdSesion, PermisosCatalogo.ProductosGestionar));
    }

    [Fact]
    public async Task Otorgamiento_y_revocacion_reflejados()
    {
        using var e = await Entorno.CrearAsync();
        var ana = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        await e.Permisos.EjecutarAsync(ana, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        var sAna = await e.Login.EjecutarAsync("ana", "clave-ana-00000000");
        Assert.NotNull(await e.Autorizador.AutorizarAsync(sAna.IdSesion, PermisosCatalogo.ProductosGestionar));
        await e.Permisos.EjecutarAsync(ana, PermisosCatalogo.ProductosGestionar, false, e.SesionDuena);
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Autorizador.AutorizarAsync(sAna.IdSesion, PermisosCatalogo.ProductosGestionar));
        // reconceder
        await e.Permisos.EjecutarAsync(ana, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        Assert.NotNull(await e.Autorizador.AutorizarAsync(sAna.IdSesion, PermisosCatalogo.ProductosGestionar));
        // verificar estado explicito y efectivo via repos
        await using var s = await e.Fabrica.IniciarLecturaAsync();
        var explicito = await e.ReposPermisos.ObtenerAsync(s, ana, PermisosCatalogo.ProductosGestionar);
        Assert.True(explicito);
    }

    [Fact]
    public async Task Empleado_no_puede_administrar_aunque_invoque_casos_directos()
    {
        using var e = await Entorno.CrearAsync();
        var ana = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        var sAna = await e.Login.EjecutarAsync("ana", "clave-ana-00000000");
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            e.Alta.EjecutarAsync(new NuevoUsuario("otro", RolesLocal.Empleada), "clave-otro-0000000", sAna.IdSesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            e.Permisos.EjecutarAsync(ana, PermisosCatalogo.ProductosGestionar, true, sAna.IdSesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            e.Desactivar.EjecutarAsync(ana, sAna.IdSesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            e.Restablecer.EjecutarAsync(ana, "nueva-clave-000000", sAna.IdSesion, null));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            e.ConfigurarNoPermitido(sAna.IdSesion));
    }

    [Fact]
    public async Task Desactivacion_y_restablecimiento_empleado()
    {
        using var e = await Entorno.CrearAsync();
        var ana = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        await e.Desactivar.EjecutarAsync(ana, e.SesionDuena);
        await Assert.ThrowsAsync<CredencialesInvalidasException>(() => e.Login.EjecutarAsync("ana", "clave-ana-00000000"));
        await e.Restablecer.EjecutarAsync(ana, "nueva-clave-0000000", e.SesionDuena, null);
        // sigue desactivada: no puede loguear aun con nueva clave
        await Assert.ThrowsAsync<CredencialesInvalidasException>(() => e.Login.EjecutarAsync("ana", "nueva-clave-0000000"));
        // No hay reactivación automática; el test verifica que desactivar+restablecer no rompe integridad
        await using var s = await e.Fabrica.IniciarLecturaAsync();
        var u = await new RepositorioUsuarios(new GeneradorIds()).ObtenerPorIdAsync(s, ana);
        Assert.NotNull(u);
        Assert.False(u!.Activo);
    }

    [Fact]
    public async Task Proteccion_ultima_Duena()
    {
        using var e = await Entorno.CrearAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Desactivar.EjecutarAsync(e.DuenaId, e.SesionDuena));
    }

    [Fact]
    public void Selector_rechaza_ruta_provisional()
    {
        var provisional = RutaBaseDatosProvisional.ObtenerPredeterminada();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SelectorBaseDesarrollo.Resolver(new[] { "--dev-db=" + provisional }));
        Assert.Contains("provisional", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void Selector_rechaza_provisional_con_mayusculas_y_separadores()
    {
        var prov = RutaBaseDatosProvisional.ObtenerPredeterminada();
        var alt = prov.Replace('\\', '/').ToUpperInvariant();
        Assert.Throws<InvalidOperationException>(() => SelectorBaseDesarrollo.Resolver(new[] { "--dev-db=" + alt }));
        Assert.Throws<InvalidOperationException>(() => SelectorBaseDesarrollo.Resolver(new[] { "--dev-db", alt }));
    }

    [Fact]
    public void Selector_precedencia_arg_sobre_env()
    {
        var prov = RutaBaseDatosProvisional.ObtenerPredeterminada();
        var devOk = Path.Combine(Path.GetTempPath(), "dev-ok-" + Guid.NewGuid().ToString("N") + ".db");
        var prev = Environment.GetEnvironmentVariable(SelectorBaseDesarrollo.EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(SelectorBaseDesarrollo.EnvVar, prov);
            // arg debe prevalecer sobre env (que apunta a provisional)
            var res = SelectorBaseDesarrollo.Resolver(new[] { "--dev-db=" + devOk });
            Assert.Equal(Path.GetFullPath(devOk).Replace('/', Path.DirectorySeparatorChar), res);
            // si arg es provisional, debe rechazar aunque env sea ok
            Environment.SetEnvironmentVariable(SelectorBaseDesarrollo.EnvVar, devOk);
            Assert.Throws<InvalidOperationException>(() => SelectorBaseDesarrollo.Resolver(new[] { "--dev-db=" + prov }));
            // sin arg, usa env
            var resEnv = SelectorBaseDesarrollo.Resolver(Array.Empty<string>());
            Assert.Equal(Path.GetFullPath(devOk).Replace('/', Path.DirectorySeparatorChar), resEnv);
        }
        finally { Environment.SetEnvironmentVariable(SelectorBaseDesarrollo.EnvVar, prev); }
    }

    [Fact]
    public async Task Confirmacion_desactivacion_Cancelar_no_desactiva()
    {
        using var e = await Entorno.CrearAsync();
        var ana = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        var svc = new LocalInfantil.Wpf.Infra.ServiciosComerciales(e.Ruta);
        var vm = new LocalInfantil.Wpf.ViewModels.AdministracionUsuariosViewModel(svc, () => e.SesionDuena);
        // La VM usa su propio Gestor, por lo que no puede validar la sesión de Entorno;
        // verificamos solo el flujo visual de confirmación (Cancelar no ejecuta, Confirmar requiere estado).
        await vm.CargarAsync();
        // Seleccionar ana si existe en la VM (puede estar vacía por Gestor distinto, pero lista proviene de DB)
        if (vm.Usuarios.Any(x => x.Id == ana))
        {
            vm.Seleccionado = vm.Usuarios.First(x => x.Id == ana);
        }
        else
        {
            // Si la VM no ve usuarios por Gestor aislado, crear fila sintética para probar visibilidad
            vm.Seleccionado = new LocalInfantil.Wpf.ViewModels.UsuarioFilaViewModel(
                new UsuarioRegistrado(ana, "ana", LocalInfantil.Application.Features.Autenticacion.RolesLocal.Empleada, true),
                LocalInfantil.Wpf.Infra.ServiciosComerciales.PermisosComercialesConfigurables);
        }

        Assert.False(vm.ConfirmacionDesactivacionVisible);
        await vm.SolicitarDesactivacionAsyncPublic();
        Assert.True(vm.ConfirmacionDesactivacionVisible);
        Assert.Equal("ana", vm.NombrePendienteDesactivacion);
        await vm.CancelarDesactivacionAsyncPublic();
        Assert.False(vm.ConfirmacionDesactivacionVisible);
        Assert.Null(vm.NombrePendienteDesactivacion);
        await using var s = await e.Fabrica.IniciarLecturaAsync();
        var u = await new RepositorioUsuarios(new GeneradorIds()).ObtenerPorIdAsync(s, ana);
        Assert.True(u!.Activo);
        // Confirmar requiere Solicitar previo; sin Solicitar no ejecuta
        await vm.ConfirmarDesactivacionAsyncPublic();
        await using var s2 = await e.Fabrica.IniciarLecturaAsync();
        var u2 = await new RepositorioUsuarios(new GeneradorIds()).ObtenerPorIdAsync(s2, ana);
        Assert.True(u2!.Activo);
        // Solicitar y confirmar con sesión inválida (VM Gestor distinto) debe dejar error pero no desactivar vía VM
        await vm.SolicitarDesactivacionAsyncPublic();
        await vm.ConfirmarDesactivacionAsyncPublic();
        Assert.NotNull(vm.Error);
        await using var s3 = await e.Fabrica.IniciarLecturaAsync();
        var u3 = await new RepositorioUsuarios(new GeneradorIds()).ObtenerPorIdAsync(s3, ana);
        Assert.True(u3!.Activo);
        // La desactivación real sigue funcionando por caso de uso directo
        await e.Desactivar.EjecutarAsync(ana, e.SesionDuena);
        await using var s4 = await e.Fabrica.IniciarLecturaAsync();
        var u4 = await new RepositorioUsuarios(new GeneradorIds()).ObtenerPorIdAsync(s4, ana);
        Assert.False(u4!.Activo);
    }

    [Fact]
    public async Task Base_provisional_no_se_abre_ni_modifica()
    {
        var provisional = RutaBaseDatosProvisional.ObtenerPredeterminada();
        var existeAntes = File.Exists(provisional);
        long lenAntes = existeAntes ? new FileInfo(provisional).Length : -1;
        // crear entorno temporal y operar normalmente
        using var e = await Entorno.CrearAsync();
        var ana = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        Assert.NotEmpty(ana);
        var existeDespues = File.Exists(provisional);
        if (existeAntes)
        {
            Assert.True(existeDespues);
            Assert.Equal(lenAntes, new FileInfo(provisional).Length);
        }
        else
        {
            // si no existía, no debe haberse creado
            Assert.False(existeDespues);
        }
    }

    private static (CrearUsuario Alta, IniciarSesion Login, CerrarSesion Cerrar, ConfigurarPermiso Permisos, DesactivarUsuario Desactivar, RestablecerContrasena Restablecer, Autorizador Autorizador, RepositorioPermisos ReposPermisos) CrearEntorno(FabricaConexionesSqlite fabrica)
    {
        var ids = new GeneradorIds();
        var reloj = new RelojFijo(DateTimeOffset.Parse("2026-09-22T00:00:00Z"));
        var sesiones = new GestorSesiones();
        var usuarios = new RepositorioUsuarios(ids);
        var permisos = new RepositorioPermisos();
        var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
        var matriz = MatrizPermisos.ConValoresConfirmados();
        var autorizador = new Autorizador(sesiones, usuarios, permisos, fabrica, matriz);
        var hasher = new Pbkdf2HashContrasenas();
        return (
            new CrearUsuario(usuarios, hasher, autorizador, fabrica, auditoria, ids, reloj),
            new IniciarSesion(usuarios, hasher, sesiones, fabrica, auditoria, ids, reloj, new OpcionesAutenticacion(0)),
            new CerrarSesion(sesiones, fabrica, auditoria, ids),
            new ConfigurarPermiso(permisos, usuarios, autorizador, fabrica, auditoria, ids, reloj),
            new DesactivarUsuario(usuarios, autorizador, fabrica, auditoria, ids),
            new RestablecerContrasena(usuarios, hasher, autorizador, fabrica, auditoria, ids),
            autorizador,
            permisos
        );
    }

    private sealed class Entorno : IDisposable
    {
        public readonly FabricaConexionesSqlite Fabrica;
        public readonly CrearUsuario Alta;
        public readonly IniciarSesion Login;
        public readonly CerrarSesion Cerrar;
        public readonly ConfigurarPermiso Permisos;
        public readonly DesactivarUsuario Desactivar;
        public readonly RestablecerContrasena Restablecer;
        public readonly Autorizador Autorizador;
        public readonly RepositorioPermisos ReposPermisos;
        public readonly string SesionDuena;
        public readonly string DuenaId;
        public readonly string Ruta;
        private readonly string _dir;

        private Entorno(string dir, string ruta, FabricaConexionesSqlite fabrica, CrearUsuario alta, IniciarSesion login, CerrarSesion cerrar, ConfigurarPermiso permisos, DesactivarUsuario desactivar, RestablecerContrasena restablecer, Autorizador autorizador, RepositorioPermisos repos, string sesion, string duenaId)
        {
            _dir = dir; Ruta = ruta; Fabrica = fabrica; Alta = alta; Login = login; Cerrar = cerrar; Permisos = permisos; Desactivar = desactivar; Restablecer = restablecer; Autorizador = autorizador; ReposPermisos = repos; SesionDuena = sesion; DuenaId = duenaId;
        }

        public async Task ConfigurarNoPermitido(string sesionId)
        {
            await Permisos.EjecutarAsync(DuenaId, "usuarios.administrar", true, sesionId);
        }

        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "wpf-login.db");
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
            var cerrar = new CerrarSesion(sesiones, fabrica, auditoria, ids);
            var cfg = new ConfigurarPermiso(permisos, usuarios, autorizador, fabrica, auditoria, ids, reloj);
            var desactivar = new DesactivarUsuario(usuarios, autorizador, fabrica, auditoria, ids);
            var restablecer = new RestablecerContrasena(usuarios, hasher, autorizador, fabrica, auditoria, ids);
            var duenaId = await alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await login.EjecutarAsync("duena", "clave-duena-000000");
            return new Entorno(dir, ruta, fabrica, alta, login, cerrar, cfg, desactivar, restablecer, autorizador, permisos, sesion.IdSesion, duenaId);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
