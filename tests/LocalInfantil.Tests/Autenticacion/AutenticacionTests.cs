using System.Diagnostics;
using System.Security.Cryptography;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Infrastructure.Seguridad;
using LocalInfantil.Tests.Fakes;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Autenticacion;

/// <summary>
/// Pruebas de F01-T04 sobre bases temporales aisladas que se eliminan al
/// terminar. Nunca tocan la base provisional ni usan datos reales.
/// </summary>
public sealed class AutenticacionTests
{
    [Fact]
    public async Task Login_correcto_e_incorrecto()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var duenaId = await entorno.CrearUsuarioAsync("duena", RolesLocal.Empleada, "clave-duena-01");

        var sesion = await entorno.Login.EjecutarAsync("duena", "clave-duena-01");
        Assert.Equal(duenaId, sesion.UsuarioId);
        Assert.Equal(RolesLocal.Duena, sesion.Rol);

        var errorClave = await Assert.ThrowsAsync<CredencialesInvalidasException>(
            () => entorno.Login.EjecutarAsync("duena", "otra-clave-00"));
        var errorNombre = await Assert.ThrowsAsync<CredencialesInvalidasException>(
            () => entorno.Login.EjecutarAsync("nadie", "clave-duena-01"));
        Assert.Equal(errorClave.Message, errorNombre.Message);
    }

    [Fact]
    public async Task Misma_contrasena_genera_huellas_distintas()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        await entorno.CrearUsuarioAsync("duena", RolesLocal.Duena, "misma-clave-00");
        await entorno.InsertarUsuarioDirectoAsync("empleada", RolesLocal.Empleada, "misma-clave-00");

        var huellaDuena = await entorno.HuellaDeAsync("duena");
        var huellaEmpleada = await entorno.HuellaDeAsync("empleada");

        Assert.NotNull(huellaDuena);
        Assert.NotNull(huellaEmpleada);
        Assert.NotEqual(huellaDuena, huellaEmpleada);
        Assert.True(entorno.Hash.Verificar("misma-clave-00", huellaDuena));
        Assert.True(entorno.Hash.Verificar("misma-clave-00", huellaEmpleada));
        Assert.False(entorno.Hash.Verificar("otra-clave-00", huellaDuena));
    }

    [Fact]
    public async Task Usuario_desactivado_no_autentica()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var id = await entorno.CrearUsuarioAsync("empleada", RolesLocal.Empleada, "clave-local-01");
        var sesion = await entorno.Login.EjecutarAsync("empleada", "clave-local-01");

        await entorno.DesactivarAsync(id);

        await Assert.ThrowsAsync<CredencialesInvalidasException>(
            () => entorno.Login.EjecutarAsync("empleada", "clave-local-01"));
        await Assert.ThrowsAsync<SesionInvalidaException>(
            () => entorno.Autorizador.AutorizarAsync(sesion.IdSesion, PermisosComerciales.ReintegroAutorizar));
    }

    [Fact]
    public async Task Sesion_invalida_y_cierre_deniegan_acceso()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        await entorno.CrearUsuarioAsync("duena", RolesLocal.Duena, "clave-local-01");
        var sesion = await entorno.Login.EjecutarAsync("duena", "clave-local-01");

        await Assert.ThrowsAsync<SesionInvalidaException>(
            () => entorno.Autorizador.AutorizarAsync("sesion-inexistente", PermisosComerciales.ReintegroAutorizar));

        Assert.True(await entorno.Cierre.EjecutarAsync(sesion.IdSesion));
        Assert.False(await entorno.Cierre.EjecutarAsync(sesion.IdSesion));
        await Assert.ThrowsAsync<SesionInvalidaException>(
            () => entorno.Autorizador.AutorizarAsync(sesion.IdSesion, PermisosComerciales.ReintegroAutorizar));
    }

    [Fact]
    public async Task Permisos_no_configurados_se_deniegan()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        await entorno.CrearUsuarioAsync("duena", RolesLocal.Duena, "clave-local-01");
        var sesion = await entorno.Login.EjecutarAsync("duena", "clave-local-01");

        foreach (var permiso in new[] { "descuento.aplicar", "fiado.vender", "anular.venta", "reporte.ver", "costo.ver", "reintegro.inventado" })
        {
            await Assert.ThrowsAsync<AccesoDenegadoException>(
                () => entorno.Autorizador.AutorizarAsync(sesion.IdSesion, permiso));
        }
    }

    [Fact]
    public async Task Reintegros_solo_con_permisos_confirmados()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        await entorno.CrearUsuarioAsync("admin", RolesLocal.Administrador, "clave-admin-00");
        await entorno.InsertarUsuarioDirectoAsync("duena", RolesLocal.Duena, "clave-duena-01");
        await entorno.InsertarUsuarioDirectoAsync("empleada", RolesLocal.Empleada, "clave-local-02");
        var sesionDuena = await entorno.Login.EjecutarAsync("duena", "clave-duena-01");
        var sesionEmpleada = await entorno.Login.EjecutarAsync("empleada", "clave-local-02");

        Assert.NotNull(await entorno.Autorizador.AutorizarAsync(sesionDuena.IdSesion, PermisosComerciales.ReintegroAutorizar));
        Assert.NotNull(await entorno.Autorizador.AutorizarAsync(sesionDuena.IdSesion, PermisosComerciales.ReintegroMedioExcepcional));
        Assert.NotNull(await entorno.Autorizador.AutorizarAsync(sesionEmpleada.IdSesion, PermisosComerciales.ReintegroAutorizar));
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Autorizador.AutorizarAsync(sesionEmpleada.IdSesion, PermisosComerciales.ReintegroMedioExcepcional));
    }

    [Fact]
    public void Autorizacion_vive_en_Application_sin_WPF()
    {
        Assert.Equal("LocalInfantil.Application", typeof(IniciarSesion).Assembly.GetName().Name);
        Assert.Equal("LocalInfantil.Application", typeof(Autorizador).Assembly.GetName().Name);
        Assert.Equal("LocalInfantil.Application", typeof(MatrizPermisos).Assembly.GetName().Name);
        Assert.Equal("LocalInfantil.Application", typeof(CrearUsuario).Assembly.GetName().Name);
        Assert.Equal("LocalInfantil.Application", typeof(RestablecerContrasena).Assembly.GetName().Name);
        Assert.Equal("LocalInfantil.Application", typeof(GestorSesiones).Assembly.GetName().Name);
    }

    [Fact]
    public async Task Auditoria_con_actor_correcto_y_sin_secretos()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        const string clave = "clave-secreta-xyz";
        var id = await entorno.CrearUsuarioAsync("duena", RolesLocal.Duena, clave);
        var sesion = await entorno.Login.EjecutarAsync("duena", clave);
        await entorno.LoginFallidoAsync("duena", "clave-otra-00");
        await entorno.Cierre.EjecutarAsync(sesion.IdSesion);

        var textos = await entorno.TextosAuditoriaAsync();
        Assert.NotEmpty(textos);
        Assert.DoesNotContain(textos, t => t.Contains(clave));
        Assert.Contains(textos, t => t.Contains("autenticacion.inicio"));
        Assert.Contains(textos, t => t.Contains("autenticacion.fallo"));
        Assert.Contains(textos, t => t.Contains("autenticacion.cierre"));
        Assert.Contains(textos, t => t.Contains(id));
    }

    [Fact]
    public async Task Restablecer_denegado_sin_permiso()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        await entorno.CrearUsuarioAsync("admin", RolesLocal.Administrador, "clave-admin-000");
        await entorno.InsertarUsuarioDirectoAsync("empleada", RolesLocal.Empleada, "clave-vieja-00");
        var id = await entorno.IdDeAsync("empleada");
        var sesion = await entorno.Login.EjecutarAsync("empleada", "clave-vieja-00");

        await Assert.ThrowsAsync<SesionInvalidaException>(
            () => entorno.Restablecer.EjecutarAsync(id, "clave-nueva-00", null, null));
        await Assert.ThrowsAsync<SesionInvalidaException>(
            () => entorno.Restablecer.EjecutarAsync(id, "clave-nueva-00", "sesion-forjada", null));
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Restablecer.EjecutarAsync(id, "clave-nueva-00", sesion.IdSesion, null));

        var vigente = await entorno.Login.EjecutarAsync("empleada", "clave-vieja-00");
        Assert.Equal(id, vigente.UsuarioId);
    }

    [Fact]
    public async Task Cambiar_huella_a_nivel_repositorio_sigue_operativo()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var id = await entorno.CrearUsuarioAsync("empleada", RolesLocal.Empleada, "clave-vieja-00");

        await entorno.CambiarHuellaDirectoAsync(id, "clave-nueva-00");

        await Assert.ThrowsAsync<CredencialesInvalidasException>(
            () => entorno.Login.EjecutarAsync("empleada", "clave-vieja-00"));
        var sesion = await entorno.Login.EjecutarAsync("empleada", "clave-nueva-00");
        Assert.Equal(id, sesion.UsuarioId);
    }

    [Fact]
    public void Ids_sesion_impredecibles_y_con_formato()
    {
        var generador = new GeneradorIds();
        var vistos = new HashSet<string>();
        for (var i = 0; i < 1000; i++)
        {
            var id = generador.NuevoId();
            Assert.Equal(32, id.Length);
            Assert.Matches("^[0-9a-f]{32}$", id);
            Assert.True(vistos.Add(id));
        }
    }

    [Fact]
    public void Politica_longitud_admite_frases_sin_clases_arbitrarias()
    {
        var hash = new Pbkdf2HashContrasenas();

        Assert.Throws<ArgumentException>(() => hash.CrearHuella(new string('a', 11)));
        Assert.Throws<ArgumentException>(() => hash.CrearHuella(new string('a', 257)));
        Assert.Throws<ArgumentException>(() => hash.CrearHuella(string.Empty));

        var frase = "mate amargo bajo la parra 1978";
        var huella = hash.CrearHuella(frase);
        Assert.True(hash.Verificar(frase, huella));
        Assert.Contains("$220000$", huella);
    }

    [Fact]
    public void Huella_anterior_verifica_y_pide_actualizacion()
    {
        var hash = new Pbkdf2HashContrasenas();
        const string clave = "clave-anterior-00";
        var sal = Convert.FromBase64String("AAAAAAAAAAAAAAAAAAAAAA==");
        var derivada = Rfc2898DeriveBytes.Pbkdf2(clave, sal, 210_000, HashAlgorithmName.SHA512, 32);
        var huellaVieja = $"v1$210000$AAAAAAAAAAAAAAAAAAAAAA==${Convert.ToBase64String(derivada)}";

        Assert.True(hash.Verificar(clave, huellaVieja));
        Assert.True(hash.NecesitaActualizacion(huellaVieja));
        Assert.False(hash.Verificar("otra-clave-00", huellaVieja));
    }

    [Fact]
    public void Iteraciones_manipuladas_se_rechazan_rapido()
    {
        var hash = new Pbkdf2HashContrasenas();
        var real = hash.CrearHuella("clave-manipulada-00");
        var partes = real.Split('$');
        var manipulada = $"v1$999999999${partes[2]}${partes[3]}";

        var cronometro = Stopwatch.StartNew();
        Assert.False(hash.Verificar("clave-manipulada-00", manipulada));
        cronometro.Stop();

        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(10), $"Tardó {cronometro.Elapsed}.");
        Assert.True(hash.NecesitaActualizacion(manipulada));
    }

    [Fact]
    public async Task Crear_sin_sesion_con_usuarios_existentes_se_rechaza()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        await entorno.CrearUsuarioAsync("duena", RolesLocal.Duena, "clave-duena-01");

        await Assert.ThrowsAsync<SesionInvalidaException>(
            () => entorno.Alta.EjecutarAsync(new NuevoUsuario("intrusa", RolesLocal.Duena), "clave-intrusa-00", null));
        await Assert.ThrowsAsync<SesionInvalidaException>(
            () => entorno.Alta.EjecutarAsync(new NuevoUsuario("intrusa", RolesLocal.Duena), "clave-intrusa-00", "sesion-forjada"));
    }

    [Fact]
    public async Task Crear_con_desactivados_no_reabre_bootstrap()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var id = await entorno.CrearUsuarioAsync("duena", RolesLocal.Duena, "clave-duena-01");
        await entorno.DesactivarAsync(id);

        await Assert.ThrowsAsync<SesionInvalidaException>(
            () => entorno.Alta.EjecutarAsync(new NuevoUsuario("intrusa", RolesLocal.Duena), "clave-intrusa-00", null));
    }

    [Fact]
    public async Task Crear_sesion_valida_no_concede_por_si_sola()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        await entorno.CrearUsuarioAsync("duena", RolesLocal.Empleada, "clave-duena-0000");
        await entorno.InsertarUsuarioDirectoAsync("ana", RolesLocal.Empleada, "clave-ana-000000");
        var sesion = await entorno.Login.EjecutarAsync("ana", "clave-ana-000000");

        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Alta.EjecutarAsync(new NuevoUsuario("empleada", RolesLocal.Empleada), "clave-empleada-00", sesion.IdSesion));
    }

    [Fact]
    public async Task Bootstrap_crea_exclusivamente_duena()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var id = await entorno.CrearUsuarioAsync("primera", RolesLocal.Empleada, "clave-primera-00");

        var rol = await entorno.RolDeAsync("primera");
        Assert.Equal(RolesLocal.Duena, rol);
        var sesion = await entorno.Login.EjecutarAsync("primera", "clave-primera-00");
        Assert.Equal(id, sesion.UsuarioId);
    }

    [Fact]
    public async Task Duena_gestiona_cuentas()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        await entorno.CrearUsuarioAsync("duena", RolesLocal.Empleada, "clave-duena-0000");
        var sesionDuena = await entorno.Login.EjecutarAsync("duena", "clave-duena-0000");

        var empleadaId = await entorno.Alta.EjecutarAsync(new NuevoUsuario("empleada", RolesLocal.Empleada), "clave-empleada-00", sesionDuena.IdSesion);
        await entorno.Desactivar.EjecutarAsync(empleadaId, sesionDuena.IdSesion);
        await Assert.ThrowsAsync<CredencialesInvalidasException>(
            () => entorno.Login.EjecutarAsync("empleada", "clave-empleada-00"));
    }

    [Fact]
    public async Task Duena_restablece_ajenas_y_la_propia_con_actual()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var duenaId = await entorno.CrearUsuarioAsync("duena", RolesLocal.Empleada, "clave-duena-0000");
        await entorno.InsertarUsuarioDirectoAsync("ana", RolesLocal.Empleada, "clave-ana-000000");
        var sesionDuena = await entorno.Login.EjecutarAsync("duena", "clave-duena-0000");

        var anaId = await entorno.IdDeAsync("ana");
        await entorno.Restablecer.EjecutarAsync(anaId, "clave-ana-nueva00", sesionDuena.IdSesion, null);
        var sesionAna = await entorno.Login.EjecutarAsync("ana", "clave-ana-nueva00");
        Assert.Equal(anaId, sesionAna.UsuarioId);

        await entorno.Restablecer.EjecutarAsync(duenaId, "clave-duena-nueva", sesionDuena.IdSesion, "clave-duena-0000");
        await Assert.ThrowsAsync<CredencialesInvalidasException>(
            () => entorno.Restablecer.EjecutarAsync(duenaId, "clave-otra-0000", sesionDuena.IdSesion, "clave-equivocada"));
        await Assert.ThrowsAsync<CredencialesInvalidasException>(
            () => entorno.Restablecer.EjecutarAsync(duenaId, "clave-otra-0000", sesionDuena.IdSesion, null));
        var renovada = await entorno.Login.EjecutarAsync("duena", "clave-duena-nueva");
        Assert.Equal(duenaId, renovada.UsuarioId);
    }

    [Fact]
    public async Task Empleada_no_gestiona_ni_cambia_claves()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        await entorno.CrearUsuarioAsync("duena", RolesLocal.Empleada, "clave-duena-0000");
        await entorno.InsertarUsuarioDirectoAsync("ana", RolesLocal.Empleada, "clave-ana-000000");
        var sesionAna = await entorno.Login.EjecutarAsync("ana", "clave-ana-000000");
        var anaId = sesionAna.UsuarioId;

        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Alta.EjecutarAsync(new NuevoUsuario("otra", RolesLocal.Empleada), "clave-otra-0000", sesionAna.IdSesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Desactivar.EjecutarAsync(anaId, sesionAna.IdSesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Restablecer.EjecutarAsync(anaId, "clave-propia-nueva", sesionAna.IdSesion, "clave-ana-000000"));
    }

    [Fact]
    public async Task No_asignar_administrador_por_via_generica()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        await entorno.CrearUsuarioAsync("duena", RolesLocal.Empleada, "clave-duena-0000");
        var sesionDuena = await entorno.Login.EjecutarAsync("duena", "clave-duena-0000");

        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Alta.EjecutarAsync(new NuevoUsuario("segundo", RolesLocal.Administrador), "clave-segunda-00", sesionDuena.IdSesion));
    }

    [Fact]
    public async Task Ultima_duena_no_se_desactiva()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var duenaId = await entorno.CrearUsuarioAsync("duena", RolesLocal.Empleada, "clave-duena-0000");
        var sesionDuena = await entorno.Login.EjecutarAsync("duena", "clave-duena-0000");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => entorno.Desactivar.EjecutarAsync(duenaId, sesionDuena.IdSesion));
        Assert.Contains("última", error.Message);
        var vigente = await entorno.Login.EjecutarAsync("duena", "clave-duena-0000");
        Assert.Equal(duenaId, vigente.UsuarioId);

        var segundoId = await entorno.Alta.EjecutarAsync(new NuevoUsuario("operadora", RolesLocal.Empleada), "clave-operadora-0", sesionDuena.IdSesion);
        await entorno.Desactivar.EjecutarAsync(segundoId, sesionDuena.IdSesion);
        await Assert.ThrowsAsync<CredencialesInvalidasException>(
            () => entorno.Login.EjecutarAsync("operadora", "clave-operadora-0"));
    }

    [Fact]
    public async Task Desactivar_forjada_y_cerrada_se_rechaza()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var duenaId = await entorno.CrearUsuarioAsync("duena", RolesLocal.Empleada, "clave-duena-0000");
        var sesionDuena = await entorno.Login.EjecutarAsync("duena", "clave-duena-0000");

        await Assert.ThrowsAsync<SesionInvalidaException>(
            () => entorno.Desactivar.EjecutarAsync(duenaId, "sesion-forjada"));
        await entorno.Cierre.EjecutarAsync(sesionDuena.IdSesion);
        await Assert.ThrowsAsync<SesionInvalidaException>(
            () => entorno.Desactivar.EjecutarAsync(duenaId, sesionDuena.IdSesion));
    }

    [Fact]
    public async Task Duena_gestiona_pero_sin_comerciales_extra()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        await entorno.CrearUsuarioAsync("duena", RolesLocal.Empleada, "clave-duena-0000");
        var sesionDuena = await entorno.Login.EjecutarAsync("duena", "clave-duena-0000");

        Assert.NotNull(await entorno.Autorizador.AutorizarAsync(sesionDuena.IdSesion, PermisosAdministracion.UsuariosAdministrar));
        Assert.NotNull(await entorno.Autorizador.AutorizarAsync(sesionDuena.IdSesion, PermisosComerciales.ReintegroAutorizar));
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Autorizador.AutorizarAsync(sesionDuena.IdSesion, PermisosCatalogo.ProductosGestionar));
    }

    [Fact]
    public async Task Cambio_de_permisos_aplica_de_inmediato()
    {
        using var entorno = await EntornoPrueba.CrearAsyncMatrizVacia();
        await entorno.CrearUsuarioAsync("duena", RolesLocal.Duena, "clave-duena-01");
        var sesion = await entorno.Login.EjecutarAsync("duena", "clave-duena-01");

        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Autorizador.AutorizarAsync(sesion.IdSesion, PermisosComerciales.ReintegroAutorizar));
        entorno.Matriz.Permitir(RolesLocal.Duena, PermisosComerciales.ReintegroAutorizar);
        Assert.NotNull(await entorno.Autorizador.AutorizarAsync(sesion.IdSesion, PermisosComerciales.ReintegroAutorizar));
        entorno.Matriz.Denegar(RolesLocal.Duena, PermisosComerciales.ReintegroAutorizar);
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => entorno.Autorizador.AutorizarAsync(sesion.IdSesion, PermisosComerciales.ReintegroAutorizar));
    }

    [Fact]
    public void Huella_malformada_no_verifica_y_pide_actualizacion()
    {
        var hash = new Pbkdf2HashContrasenas();
        var huella = hash.CrearHuella("clave-valida-00");

        Assert.False(hash.Verificar("clave-valida-00", null));
        Assert.False(hash.Verificar("clave-valida-00", "formato-roto"));
        Assert.False(hash.Verificar(string.Empty, huella));
        Assert.False(hash.NecesitaActualizacion(huella));
        Assert.True(hash.NecesitaActualizacion(huella.Replace("$220000$", "$1000$")));
        Assert.True(hash.NecesitaActualizacion(null));
    }

    [Fact]
    public async Task Migracion_V2_V6_con_datos_respaldo_y_sin_duplicados()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(directorio, "auth-f01.db");
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            var catalogoV2 = MigracionesF01.Todas.Where(m => m.Version <= 2).ToList();
            Assert.Equal(new[] { 1, 2 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV2, null));

            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var hash = new Pbkdf2HashContrasenas();
            var ids = new GeneradorIds();
            var usuarios = new RepositorioUsuarios(ids);

            // Filas de época V2: el único administrador activo se convierte en dueña.
            await using (var cruda = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = ruta,
                Pooling = false,
            }.ToString()))
            {
                await cruda.OpenAsync();
                using var insercion = cruda.CreateCommand();
                insercion.CommandText = "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u-ficticio', 'ficticio', 'empleada', '2026-09-21T12:00:00Z');";
                await insercion.ExecuteNonQueryAsync();
                using var admin = cruda.CreateCommand();
                admin.CommandText = "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u-admin', 'vieja-admin', 'administrador', '2026-09-21T12:00:00Z');";
                await admin.ExecuteNonQueryAsync();
            }

            Assert.Equal(new[] { 3, 4, 5, 6, 7, 8 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));

            Assert.Single(Directory.GetFiles(respaldos, "*.db"));
            await using var lectura = await fabrica.IniciarLecturaAsync();
            var conservado = await usuarios.ObtenerPorNombreAsync(lectura, "ficticio");
            Assert.NotNull(conservado);
            Assert.Null(await usuarios.ObtenerHuellaAsync(lectura, conservado.Id));
            var convertida = await usuarios.ObtenerPorIdAsync(lectura, "u-admin");
            Assert.NotNull(convertida);
            Assert.Equal(RolesLocal.Duena, convertida.Rol);
            Assert.Equal("vieja-admin", convertida.Nombre);

            await using (var sesion = await fabrica.IniciarAsync())
            {
                var nuevoId = await usuarios.CrearAsync(sesion, new NuevoUsuario("ficticio2", RolesLocal.Duena), hash.CrearHuella("clave-ficticia"), "2026-09-21T12:00:00Z");
                await sesion.ConfirmarAsync();
                await using var releer = await fabrica.IniciarLecturaAsync();
                var huella = await usuarios.ObtenerHuellaAsync(releer, nuevoId);
                Assert.NotNull(huella);
                Assert.True(hash.Verificar("clave-ficticia", huella));
            }

            Assert.Empty(await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
        }
        finally
        {
            if (Directory.Exists(directorio))
            {
                Directory.Delete(directorio, recursive: true);
            }
        }
    }

    private static readonly DateTimeOffset MomentoFijo = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed class EntornoPrueba : IDisposable
    {
        private readonly string _directorio;
        private readonly FabricaConexionesSqlite _fabrica;
        private readonly RepositorioUsuarios _usuarios;
        private readonly RepositorioAuditoria _auditoria;
        private readonly GeneradorIds _ids;
        private readonly RelojFijo _reloj;
        private readonly GestorSesiones _sesiones;

        public readonly Pbkdf2HashContrasenas Hash = new();
        public readonly IniciarSesion Login;
        public readonly CerrarSesion Cierre;
        public readonly CrearUsuario Alta;
        public readonly RestablecerContrasena Restablecer;
        public readonly DesactivarUsuario Desactivar;
        public readonly Autorizador Autorizador;
        public readonly MatrizPermisos Matriz;

        private EntornoPrueba(string directorio, FabricaConexionesSqlite fabrica, MatrizPermisos matriz)
        {
            _directorio = directorio;
            _fabrica = fabrica;
            Matriz = matriz;
            _ids = new GeneradorIds();
            var reloj = new RelojFijo(MomentoFijo);
            _reloj = reloj;
            _usuarios = new RepositorioUsuarios(_ids);
            _auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
            _sesiones = new GestorSesiones();
            var opciones = new OpcionesAutenticacion(0);
            Login = new IniciarSesion(_usuarios, Hash, _sesiones, _fabrica, _auditoria, _ids, reloj, opciones);
            Cierre = new CerrarSesion(_sesiones, _fabrica, _auditoria, _ids);
            Autorizador = new Autorizador(_sesiones, _usuarios, new RepositorioPermisos(), _fabrica, Matriz);
            Alta = new CrearUsuario(_usuarios, Hash, Autorizador, _fabrica, _auditoria, _ids, reloj);
            Restablecer = new RestablecerContrasena(_usuarios, Hash, Autorizador, _fabrica, _auditoria, _ids);
            Desactivar = new DesactivarUsuario(_usuarios, Autorizador, _fabrica, _auditoria, _ids);
        }

        public static async Task<EntornoPrueba> CrearAsync()
        {
            return await CrearConMatrizAsync(MatrizPermisos.ConValoresConfirmados());
        }

        public static async Task<EntornoPrueba> CrearAsyncMatrizVacia()
        {
            return await CrearConMatrizAsync(MatrizPermisos.Vacia());
        }

        private static async Task<EntornoPrueba> CrearConMatrizAsync(MatrizPermisos matriz)
        {
            var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(directorio, "auth-f01.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            return new EntornoPrueba(directorio, new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta)), matriz);
        }

        public Task<string> CrearUsuarioAsync(string nombre, string rol, string contrasena)
        {
            return Alta.EjecutarAsync(new NuevoUsuario(nombre, rol), contrasena, null);
        }

        public async Task LoginFallidoAsync(string nombre, string contrasena)
        {
            await Assert.ThrowsAsync<CredencialesInvalidasException>(() => Login.EjecutarAsync(nombre, contrasena));
        }

        public async Task<string> InsertarUsuarioDirectoAsync(string nombre, string rol, string contrasena)
        {
            await using var sesion = await _fabrica.IniciarAsync();
            var id = await _usuarios.CrearAsync(
                sesion,
                new NuevoUsuario(nombre, rol),
                Hash.CrearHuella(contrasena),
                "2026-09-21T12:00:00Z");
            await sesion.ConfirmarAsync();
            return id;
        }

        public async Task DesactivarAsync(string id)
        {
            await using var sesion = await _fabrica.IniciarAsync();
            await _usuarios.DesactivarAsync(sesion, id);
            await sesion.ConfirmarAsync();
        }

        public async Task CambiarHuellaDirectoAsync(string id, string nuevaContrasena)
        {
            await using var sesion = await _fabrica.IniciarAsync();
            await _usuarios.CambiarHuellaAsync(sesion, id, Hash.CrearHuella(nuevaContrasena));
            await sesion.ConfirmarAsync();
        }

        public async Task<string?> HuellaDeAsync(string nombre)
        {
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            var usuario = await _usuarios.ObtenerPorNombreAsync(sesion, nombre);
            return usuario is null ? null : await _usuarios.ObtenerHuellaAsync(sesion, usuario.Id);
        }

        public async Task<string> IdDeAsync(string nombre)
        {
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            var usuario = await _usuarios.ObtenerPorNombreAsync(sesion, nombre);
            return usuario is null ? throw new InvalidOperationException("El usuario ficticio no existe.") : usuario.Id;
        }

        public async Task<string?> RolDeAsync(string nombre)
        {
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            var usuario = await _usuarios.ObtenerPorNombreAsync(sesion, nombre);
            return usuario?.Rol;
        }

        public async Task<List<string>> TextosAuditoriaAsync()
        {
            await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_directorio, "auth-f01.db"),
                Pooling = false,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            await conexion.OpenAsync();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT operacion_id, tipo_evento, actor_tipo, actor_id, referencia_tipo, referencia_id, detalle FROM auditoria;";
            await using var lector = await comando.ExecuteReaderAsync();
            var textos = new List<string>();
            while (await lector.ReadAsync())
            {
                for (var i = 0; i < lector.FieldCount; i++)
                {
                    textos.Add(lector.IsDBNull(i) ? string.Empty : lector.GetString(i));
                }
            }

            return textos;
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
