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
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Autenticacion;

/// <summary>
/// Permisos individuales por empleado y conversión V5→V6. Bases temporales
/// aisladas, eliminadas al terminar. Sin base provisional ni datos reales.
/// </summary>
public sealed class PermisosIndividualesTests
{
    [Fact]
    public async Task Duena_crea_dos_empleados_con_permisos_diferentes()
    {
        using var entorno = await EntornoDuena.CrearAsync();
        var anaId = await entorno.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", entorno.SesionDuena);
        var luciaId = await entorno.Alta.EjecutarAsync(new NuevoUsuario("lucia", RolesLocal.Empleada), "clave-lucia-000000", entorno.SesionDuena);
        Assert.NotEqual(anaId, luciaId);

        await entorno.Permisos.EjecutarAsync(anaId, PermisosCatalogo.ProductosGestionar, true, entorno.SesionDuena);
        await entorno.Permisos.EjecutarAsync(luciaId, PermisosInventario.AjusteStock, true, entorno.SesionDuena);

        var ana = await entorno.Login.EjecutarAsync("ana", "clave-ana-00000000");
        var lucia = await entorno.Login.EjecutarAsync("lucia", "clave-lucia-000000");
        Assert.NotNull(await entorno.Autorizador.AutorizarAsync(ana.IdSesion, PermisosCatalogo.ProductosGestionar));
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Autorizador.AutorizarAsync(ana.IdSesion, PermisosInventario.AjusteStock));
        Assert.NotNull(await entorno.Autorizador.AutorizarAsync(lucia.IdSesion, PermisosInventario.AjusteStock));
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Autorizador.AutorizarAsync(lucia.IdSesion, PermisosCatalogo.ProductosGestionar));
    }

    [Fact]
    public async Task Revocar_bloquea_sesion_ya_abierta()
    {
        using var entorno = await EntornoDuena.CrearAsync();
        var anaId = await entorno.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", entorno.SesionDuena);
        await entorno.Permisos.EjecutarAsync(anaId, PermisosCatalogo.ProductosGestionar, true, entorno.SesionDuena);
        var ana = await entorno.Login.EjecutarAsync("ana", "clave-ana-00000000");

        Assert.NotNull(await entorno.Autorizador.AutorizarAsync(ana.IdSesion, PermisosCatalogo.ProductosGestionar));
        await entorno.Permisos.EjecutarAsync(anaId, PermisosCatalogo.ProductosGestionar, false, entorno.SesionDuena);
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Autorizador.AutorizarAsync(ana.IdSesion, PermisosCatalogo.ProductosGestionar));
    }

    [Fact]
    public async Task Revocacion_prevalece_sobre_concesion_de_matriz()
    {
        using var entorno = await EntornoDuena.CrearAsync();
        var anaId = await entorno.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", entorno.SesionDuena);
        var ana = await entorno.Login.EjecutarAsync("ana", "clave-ana-00000000");

        Assert.NotNull(await entorno.Autorizador.AutorizarAsync(ana.IdSesion, PermisosComerciales.ReintegroAutorizar));
        await entorno.Permisos.EjecutarAsync(anaId, PermisosComerciales.ReintegroAutorizar, false, entorno.SesionDuena);
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Autorizador.AutorizarAsync(ana.IdSesion, PermisosComerciales.ReintegroAutorizar));

        await entorno.Permisos.EjecutarAsync(anaId, PermisosComerciales.ReintegroAutorizar, true, entorno.SesionDuena);
        Assert.NotNull(await entorno.Autorizador.AutorizarAsync(ana.IdSesion, PermisosComerciales.ReintegroAutorizar));
    }

    [Fact]
    public async Task Empleado_no_se_auto_otorga_ni_crea_usuarios()
    {
        using var entorno = await EntornoDuena.CrearAsync();
        await entorno.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", entorno.SesionDuena);
        var ana = await entorno.Login.EjecutarAsync("ana", "clave-ana-00000000");
        var anaId = ana.UsuarioId;

        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Permisos.EjecutarAsync(anaId, PermisosCatalogo.ProductosGestionar, true, ana.IdSesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Alta.EjecutarAsync(new NuevoUsuario("otra", RolesLocal.Empleada), "clave-otra-000000", ana.IdSesion));
    }

    [Fact]
    public async Task Desactivar_preserva_auditoria_e_historial()
    {
        using var entorno = await EntornoDuena.CrearAsync();
        var anaId = await entorno.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", entorno.SesionDuena);
        await entorno.Login.EjecutarAsync("ana", "clave-ana-00000000");
        await entorno.Desactivar.EjecutarAsync(anaId, entorno.SesionDuena);

        Assert.True(await entorno.HayMencionAuditoriaDeAsync(anaId));
        Assert.NotNull(await entorno.UsuarioDeAsync(anaId));
    }

    [Fact]
    public async Task Fallo_intermedio_en_permiso_revierte_todo()
    {
        using var entorno = await EntornoDuena.CrearAsync();
        var anaId = await entorno.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", entorno.SesionDuena);

        await using (var sesion = await entorno.Fabrica.IniciarAsync())
        {
            await entorno.ReposPermisos.DefinirAsync(sesion, anaId, PermisosCatalogo.ProductosGestionar, true, Fecha, entorno.DuenaId);
            await Assert.ThrowsAsync<SqliteException>(() => entorno.ReposAuditoria.RegistrarAsync(
                sesion,
                new NuevoEventoAuditoria("op-permiso-falla", "autorizacion.permiso_otorgado", ActorAuditoria.Usuario, null, "usuario", anaId, null)));
        }

        Assert.Empty(await entorno.PermisosDeAsync(anaId));
        Assert.Equal(0, await entorno.AuditoriaDeAsync("op-permiso-falla"));
    }

    [Fact]
    public async Task V6_convierte_unico_administrador_activo()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            var catalogoV5 = MigracionesF01.Todas.Where(m => m.Version <= 5).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV5, null));
            await EjecutarAsync(ruta,
                "INSERT INTO usuario (id, nombre, rol, huella_contrasena, creado_en) VALUES ('u-admin', 'Vieja Admin', 'administrador', 'huella-ficticia-abc', '2026-09-21T12:00:00Z');",
                "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u-ana', 'Ana', 'empleada', '2026-09-21T12:00:00Z');",
                "INSERT INTO auditoria (id, operacion_id, tipo_evento, actor_tipo, actor_id, creado_en) VALUES ('ev-1', 'op-vieja', 'prueba.evento', 'sistema', NULL, '2026-09-21T12:00:00Z');");

            Assert.Equal(new[] { 6, 7, 8 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, Path.Combine(directorio, "respaldos")));

            Assert.Equal("duena", await LeerTextoAsync(ruta, "SELECT rol FROM usuario WHERE id = 'u-admin';"));
            Assert.Equal("Vieja Admin", await LeerTextoAsync(ruta, "SELECT nombre FROM usuario WHERE id = 'u-admin';"));
            Assert.Equal("huella-ficticia-abc", await LeerTextoAsync(ruta, "SELECT huella_contrasena FROM usuario WHERE id = 'u-admin';"));
            Assert.Equal("empleada", await LeerTextoAsync(ruta, "SELECT rol FROM usuario WHERE id = 'u-ana';"));
            Assert.Equal("op-vieja", await LeerTextoAsync(ruta, "SELECT operacion_id FROM auditoria WHERE id = 'ev-1';"));
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, await VersionesAsync(ruta));
            Assert.Empty(await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, Path.Combine(directorio, "respaldos")));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task V6_rechaza_dos_administradores()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            var catalogoV5 = MigracionesF01.Todas.Where(m => m.Version <= 5).ToList();
            await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV5, null);
            await EjecutarAsync(ruta,
                "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u-a1', 'A1', 'administrador', '2026-09-21T12:00:00Z');",
                "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u-a2', 'A2', 'administrador', '2026-09-21T12:00:00Z');");

            await Assert.ThrowsAsync<SqliteException>(
                () => new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, Path.Combine(directorio, "respaldos")));

            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, await VersionesAsync(ruta));
            Assert.Equal("administrador", await LeerTextoAsync(ruta, "SELECT rol FROM usuario WHERE id = 'u-a1';"));
            Assert.Equal("administrador", await LeerTextoAsync(ruta, "SELECT rol FROM usuario WHERE id = 'u-a2';"));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task V6_rechaza_admin_inactivo_y_sin_duena()
    {
        foreach (var filas in new[]
        {
            new[] { "INSERT INTO usuario (id, nombre, rol, activo, creado_en) VALUES ('u-a1', 'A1', 'administrador', 0, '2026-09-21T12:00:00Z');" },
            new[] { "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u-ana', 'Ana', 'empleada', '2026-09-21T12:00:00Z');" },
        })
        {
            var (directorio, ruta) = CrearBaseTemporal();
            try
            {
                var catalogoV5 = MigracionesF01.Todas.Where(m => m.Version <= 5).ToList();
                await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV5, null);
                await EjecutarAsync(ruta, filas);

                await Assert.ThrowsAsync<SqliteException>(
                    () => new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, Path.Combine(directorio, "respaldos")));

                Assert.Equal(new[] { 1, 2, 3, 4, 5 }, await VersionesAsync(ruta));
            }
            finally
            {
                Limpiar(directorio);
            }
        }
    }

    private static readonly DateTimeOffset MomentoFijo = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private const string Fecha = "2026-09-21T12:00:00Z";

    private static (string Directorio, string Ruta) CrearBaseTemporal()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        return (directorio, Path.Combine(directorio, "permisos-f01.db"));
    }

    private static void Limpiar(string directorio)
    {
        if (Directory.Exists(directorio))
        {
            Directory.Delete(directorio, recursive: true);
        }
    }

    private static async Task EjecutarAsync(string ruta, params string[] sentencias)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
        }.ToString());
        await conexion.OpenAsync();
        foreach (var sql in sentencias)
        {
            using var comando = conexion.CreateCommand();
            comando.CommandText = sql;
            await comando.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string?> LeerTextoAsync(string ruta, string sql)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        return await comando.ExecuteScalarAsync() as string;
    }

    private static async Task<List<int>> VersionesAsync(string ruta)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT version FROM schema_migrations ORDER BY version;";
        await using var lector = await comando.ExecuteReaderAsync();
        var versiones = new List<int>();
        while (await lector.ReadAsync())
        {
            versiones.Add(lector.GetInt32(0));
        }

        return versiones;
    }

    private sealed class EntornoDuena : IDisposable
    {
        private readonly string _directorio;
        private readonly FabricaConexionesSqlite _fabrica;
        private readonly GeneradorIds _ids;
        private readonly RelojFijo _reloj;
        private readonly GestorSesiones _sesiones;

        public readonly CrearUsuario Alta;
        public readonly ConfigurarPermiso Permisos;
        public readonly DesactivarUsuario Desactivar;
        public readonly Autorizador Autorizador;
        public readonly RepositorioPermisos ReposPermisos;
        public readonly RepositorioAuditoria ReposAuditoria;
        public FabricaConexionesSqlite Fabrica => _fabrica;

        private string? _sesionId;
        private string? _duenaId;

        public string SesionDuena => _sesionId ?? throw new InvalidOperationException("Sin sesión registrada.");
        public string DuenaId => _duenaId ?? throw new InvalidOperationException("Sin dueña registrada.");

        private EntornoDuena(string directorio, FabricaConexionesSqlite fabrica, MatrizPermisos matriz)
        {
            _directorio = directorio;
            _fabrica = fabrica;
            _ids = new GeneradorIds();
            var reloj = new RelojFijo(MomentoFijo);
            _reloj = reloj;
            _sesiones = new GestorSesiones();
            var usuarios = new RepositorioUsuarios(_ids);
            var permisos = new RepositorioPermisos();
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
            var autorizador = new Autorizador(_sesiones, usuarios, permisos, _fabrica, matriz);
            Autorizador = autorizador;
            ReposPermisos = permisos;
            ReposAuditoria = auditoria;
            Alta = new CrearUsuario(usuarios, new Pbkdf2HashContrasenas(), autorizador, _fabrica, auditoria, _ids, reloj);
            Permisos = new ConfigurarPermiso(permisos, usuarios, autorizador, _fabrica, auditoria, _ids, reloj);
            Desactivar = new DesactivarUsuario(usuarios, autorizador, _fabrica, auditoria, _ids);
            Login = new IniciarSesion(usuarios, new Pbkdf2HashContrasenas(), _sesiones, _fabrica, auditoria, _ids, reloj, new OpcionesAutenticacion(0));
        }

        public readonly IniciarSesion Login;

        public static async Task<EntornoDuena> CrearAsync()
        {
            var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(directorio, "permisos-f01.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var entorno = new EntornoDuena(directorio, new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta)), MatrizPermisos.ConValoresConfirmados());
            var id = await entorno.Alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Empleada), "clave-duena-000000", null);
            var sesion = await entorno.Login.EjecutarAsync("duena", "clave-duena-000000");
            entorno._sesionId = sesion.IdSesion;
            entorno._duenaId = id;
            return entorno;
        }

        public async Task<long> AuditoriaDeAsync(string operacionId)
        {
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            return (await ReposAuditoria.ObtenerPorOperacionAsync(sesion, operacionId)).Count;
        }

        public async Task<bool> HayMencionAuditoriaDeAsync(string usuarioId)
        {
            await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_directorio, "permisos-f01.db"),
                Pooling = false,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            await conexion.OpenAsync();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT COUNT(*) FROM auditoria WHERE actor_id = $id OR referencia_id = $id;";
            comando.Parameters.AddWithValue("$id", usuarioId);
            return Convert.ToInt64(await comando.ExecuteScalarAsync()) > 0;
        }

        public async Task<UsuarioRegistrado?> UsuarioDeAsync(string id)
        {
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            var usuarios = new RepositorioUsuarios(_ids);
            return await usuarios.ObtenerPorIdAsync(sesion, id);
        }

        public async Task<List<string>> PermisosDeAsync(string id)
        {
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            return (await ReposPermisos.ListarConcedidosAsync(sesion, id)).ToList();
        }

        public void Dispose()
        {
            if (Directory.Exists(_directorio))
            {
                Directory.Delete(_directorio, recursive: true);
            }
        }
    }
}
