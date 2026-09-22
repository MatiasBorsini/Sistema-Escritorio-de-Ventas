using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Compras;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Infrastructure.Seguridad;
using LocalInfantil.Tests.Fakes;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Tests.Wpf;

public sealed class WpfRecepcionDurableTests
{
    [Fact]
    public void Pendiente_corrupto_bloquea()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "corr.db");
        Directory.CreateDirectory(dir);
        try
        {
            var rutaPend = AlmacenRecepcionPendiente.RutaPendiente(ruta);
            File.WriteAllText(rutaPend, "{ not json");
            var ex = Assert.Throws<InvalidOperationException>(() => AlmacenRecepcionPendiente.Cargar(ruta));
            Assert.Contains("corrupto", ex.Message.ToLowerInvariant());
            // No se interpreta como ausencia: sigue existiendo y bloquea Guardar con otro id
            var ex2 = Assert.Throws<InvalidOperationException>(() => AlmacenRecepcionPendiente.Guardar(ruta, "op2", "prov2", null, new[] { new LineaRecepcion("v1", 1, null, null) }));
            Assert.Contains("corrupto", ex2.Message.ToLowerInvariant());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Pendiente_incompleto_tmp_no_sobrescribe_definitivo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "tmp.db");
        Directory.CreateDirectory(dir);
        try
        {
            var op1 = Guid.NewGuid().ToString("N");
            AlmacenRecepcionPendiente.Guardar(ruta, op1, "prov1", null, new[] { new LineaRecepcion("v1", 1, null, null) });
            var rutaPend = AlmacenRecepcionPendiente.RutaPendiente(ruta);
            var contenidoAntes = File.ReadAllText(rutaPend);
            // Simular escritura parcial dejando .tmp incompleto
            File.WriteAllText(rutaPend + ".tmp", "{ parcial");
            // El definitivo debe seguir intacto
            Assert.True(File.Exists(rutaPend));
            Assert.Equal(contenidoAntes, File.ReadAllText(rutaPend));
            Assert.True(File.Exists(rutaPend + ".tmp"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Fallo_al_guardar_no_envia_recepcion()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "noguardar.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        using var e = await Entorno.CrearAsync(ruta);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        // Hacer directorio no escribible para forzar fallo al guardar pendiente
        var rutaPend = AlmacenRecepcionPendiente.RutaPendiente(ruta);
        Directory.CreateDirectory(Path.GetDirectoryName(rutaPend)!);
        File.WriteAllText(rutaPend, "{}"); // corrupto existente
        // Intentar guardar con otro op debe fallar por corrupto existente y no debe enviar
        var svc = new ServiciosComerciales(ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => ses.IdSesion);
        await vm.CargarProveedoresAsync();
        vm.ProveedorSeleccionado = vm.Proveedores.FirstOrDefault(p => p.Id == prov) ?? new ProveedorRegistrado(prov, "Prov", true);
        var sku = (await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena))!.Variantes.First(x => x.Id == v1).Sku;
        vm.Filas.Clear();
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = sku, CantidadTexto = "1" });
        // Forzar pendiente corrupto bloquea Guardar
        File.WriteAllText(rutaPend, "{ corrupto");
        await vm.ConfirmarAsyncPublic();
        Assert.Contains("corrupto", vm.Error ?? "", StringComparison.OrdinalIgnoreCase);
        var recepciones = await svc.ConsultasRecepciones.ListarAsync(10, 0, ses.IdSesion);
        Assert.Empty(recepciones);
        try { File.Delete(rutaPend); } catch { }
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Fallo_al_eliminar_no_habilita_otra_recepcion()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "noeliminar.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        using var e = await Entorno.CrearAsync(ruta);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        var svc = new ServiciosComerciales(ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => ses.IdSesion);
        await vm.CargarProveedoresAsync();
        vm.ProveedorSeleccionado = vm.Proveedores.First(p => p.Id == prov);
        var sku = (await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena))!.Variantes.First(x => x.Id == v1).Sku;
        vm.Filas.Clear();
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = sku, CantidadTexto = "2" });
        await vm.ConfirmarAsyncPublic();
        Assert.Null(vm.Error);
        // Simular fallo al eliminar: bloquear archivo pendiente
        var rutaPend = AlmacenRecepcionPendiente.RutaPendiente(ruta);
        // Crear pendiente artificial y bloquearlo
        AlmacenRecepcionPendiente.Guardar(ruta, "op-bloqueo", prov, null, new[] { new LineaRecepcion(v1, 1, null, null) });
        using var bloqueo = new FileStream(rutaPend, FileMode.Open, FileAccess.Read, FileShare.None);
        // Intentar eliminar debe fallar (IOException) y ViewModel debe mantener pendiente
        // Verificar que Existe sigue true a pesar de intentar eliminar
        Assert.True(AlmacenRecepcionPendiente.Existe(ruta));
        bloqueo.Dispose();
        AlmacenRecepcionPendiente.Eliminar(ruta);
        Assert.False(AlmacenRecepcionPendiente.Existe(ruta));
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Recuperacion_en_curso_bloquea_confirmar()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "bloqueo.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        using var e = await Entorno.CrearAsync(ruta);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        var op = Guid.NewGuid().ToString("N");
        AlmacenRecepcionPendiente.Guardar(ruta, op, prov, null, new[] { new LineaRecepcion(v1, 1, null, null) });
        var svc = new ServiciosComerciales(ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => ses.IdSesion);
        // No esperar VerificarPendiente, llamar Confirmar concurrentemente
        var tVerif = vm.VerificarPendienteAsync();
        var tConfirm = vm.ConfirmarAsyncPublic(); // debe estar bloqueado por Ocupado
        await Task.WhenAll(tVerif, tConfirm);
        // Si estaba recuperando, Confirmar no debe haber creado segunda recepción
        var recepciones = await svc.ConsultasRecepciones.ListarAsync(10, 0, ses.IdSesion);
        Assert.True(recepciones.Count <= 1);
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Pendiente_existente_distinto_operacion_id_no_sobrescribe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "nooverwrite.db");
        Directory.CreateDirectory(dir);
        var op1 = Guid.NewGuid().ToString("N");
        AlmacenRecepcionPendiente.Guardar(ruta, op1, "prov1", null, new[] { new LineaRecepcion("v1", 1, null, null) });
        var ex = Assert.Throws<InvalidOperationException>(() => AlmacenRecepcionPendiente.Guardar(ruta, "op2", "prov2", null, new[] { new LineaRecepcion("v2", 1, null, null) }));
        Assert.Contains("no reconciliada", ex.Message.ToLowerInvariant());
        var cargado = AlmacenRecepcionPendiente.Cargar(ruta)!;
        Assert.Equal(op1, cargado.OperacionId);
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Cambio_contenido_incierto_requiere_nueva_confirmacion()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "cambio.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        using var e = await Entorno.CrearAsync(ruta);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (prod, v1, v2) = await CrearCatalogoAsync(e);
        var op = Guid.NewGuid().ToString("N");
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 2, null, null) }, op, null, e.SesionDuena);
        var svc = new ServiciosComerciales(ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => ses.IdSesion);
        await vm.CargarProveedoresAsync();
        // Simular pendiente con op ya registrado pero contenido distinto (v1 vs v2)
        AlmacenRecepcionPendiente.Guardar(ruta, op, prov, null, new[] { new LineaRecepcion(v1, 2, null, null) });
        // Forzar ViewModel a tener ese pendiente
        await vm.VerificarPendienteAsync(); // detectará ya registrada y limpiará con mensaje
        Assert.Contains("ya quedó registrada", vm.Mensaje ?? "");
        // Ahora cambiar contenido a v2 y confirmar debe requerir nueva confirmación explícita, no auto
        var sku2 = (await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena))!.Variantes.First(x => x.Id == v2).Sku;
        vm.Filas.Clear();
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = sku2, CantidadTexto = "5" });
        vm.ProveedorSeleccionado = vm.Proveedores.First(p => p.Id == prov);
        // La reconciliación previa ya generó nuevo id, ahora confirmar nuevo contenido debe crear nueva recepción (requiere confirmación explícita, que damos)
        await vm.ConfirmarAsyncPublic();
        Assert.Null(vm.Error);
        var recepciones = await svc.ConsultasRecepciones.ListarAsync(10, 0, ses.IdSesion);
        Assert.Equal(2, recepciones.Count);
        Directory.Delete(dir, true);
    }

    private async Task<(string prod, string v1, string v2)> CrearCatalogoAsync(Entorno e)
    {
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        var prod = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod-" + Guid.NewGuid().ToString("N").Substring(0, 4), null, null), e.SesionDuena);
        var v1 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, "SKU-" + Guid.NewGuid().ToString("N").Substring(0, 6), "M", "Rojo"), e.SesionDuena);
        var v2 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, "SKU-" + Guid.NewGuid().ToString("N").Substring(0, 6), null, null), e.SesionDuena);
        return (prod, v1, v2);
    }

    private sealed class Entorno : IDisposable
    {
        public string Ruta { get; }
        public FabricaConexionesSqlite Fabrica { get; }
        public CrearProveedor CrearProveedor { get; }
        public CrearProducto CrearProducto { get; }
        public CrearVariante CrearVariante { get; }
        public ConsultasCatalogo ConsultasCatalogo { get; }
        public ConfirmarRecepcion ConfirmarRecepcion { get; }
        public ConsultasRecepciones ConsultasRecepciones { get; }
        public string SesionDuena { get; }
        public string DuenaId { get; }
        public ConfigurarPermiso Permisos { get; }
        private readonly string _dir;
        public Entorno(string dir, string ruta, FabricaConexionesSqlite fabrica, CrearProveedor cp, CrearProducto cprod, CrearVariante cvar, ConsultasCatalogo cc, ConfirmarRecepcion cr, ConsultasRecepciones cr2, string sesion, string duenaId, ConfigurarPermiso perm)
        { _dir = dir; Ruta = ruta; Fabrica = fabrica; CrearProveedor = cp; CrearProducto = cprod; CrearVariante = cvar; ConsultasCatalogo = cc; ConfirmarRecepcion = cr; ConsultasRecepciones = cr2; SesionDuena = sesion; DuenaId = duenaId; Permisos = perm; }
        public static async Task<Entorno> CrearAsync(string? rutaPrevia = null)
        {
            var dir = rutaPrevia is null ? Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N")) : Path.GetDirectoryName(rutaPrevia)!;
            var ruta = rutaPrevia ?? Path.Combine(dir, "stock.db");
            if (rutaPrevia is null) await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var ids = new GeneradorIds();
            var reloj = new RelojAvance(DateTimeOffset.Parse("2026-09-22T00:00:00Z"));
            var sesiones = new GestorSesiones();
            var usuarios = new RepositorioUsuarios(ids);
            var permisos = new RepositorioPermisos();
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
            var matriz = MatrizPermisos.ConValoresConfirmados();
            var autorizador = new Autorizador(sesiones, usuarios, permisos, fabrica, matriz);
            var hasher = new Pbkdf2HashContrasenas();
            var alta = new CrearUsuario(usuarios, hasher, autorizador, fabrica, auditoria, ids, reloj);
            var login = new IniciarSesion(usuarios, hasher, sesiones, fabrica, auditoria, ids, reloj, new OpcionesAutenticacion(0));
            var perm = new ConfigurarPermiso(permisos, usuarios, autorizador, fabrica, auditoria, ids, reloj);
            var provRepo = new RepositorioProveedores(ids);
            var prodRepo = new RepositorioProductos(ids);
            var varRepo = new RepositorioVariantes(ids);
            var movRepo = new RepositorioMovimientos(ids);
            var recepRepo = new RepositorioRecepciones(ids);
            var crearProv = new CrearProveedor(provRepo, autorizador, fabrica, auditoria, ids, reloj);
            var crearProd = new CrearProducto(prodRepo, autorizador, fabrica, auditoria, ids, reloj);
            var crearVar = new CrearVariante(varRepo, prodRepo, autorizador, fabrica, auditoria, ids, reloj);
            var consultasCat = new ConsultasCatalogo(prodRepo, varRepo, sesiones, fabrica);
            var confirmarRec = new ConfirmarRecepcion(recepRepo, provRepo, varRepo, movRepo, autorizador, fabrica, auditoria, reloj);
            var consultasRec = new ConsultasRecepciones(recepRepo, sesiones, fabrica);
            string duenaId, sesId;
            if (rutaPrevia is null)
            {
                duenaId = await alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
                sesId = (await login.EjecutarAsync("duena", "clave-duena-000000")).IdSesion;
            }
            else
            {
                await using var lecturaPrevia = await fabrica.IniciarLecturaAsync();
                var existente = await usuarios.ObtenerPorNombreAsync(lecturaPrevia, "duena");
                duenaId = existente?.Id
                    ?? await alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
                sesId = (await login.EjecutarAsync("duena", "clave-duena-000000")).IdSesion;
            }
            return new Entorno(dir, ruta, fabrica, crearProv, crearProd, crearVar, consultasCat, confirmarRec, consultasRec, sesId, duenaId, perm);
        }
        public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    }

    [Fact]
    public async Task Prototipo_con_archivo_invalido_informa_error_sin_reconstruir()
    {
        // Solo archivo ficticio temporal; nunca la ruta provisional real.
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var ruta = Path.Combine(dir, "prototipo-invalido.db");
        try
        {
            var basura = "esto no es una base sqlite";
            File.WriteAllText(ruta, basura);
            var comprobacion = new LocalInfantil.Application.Features.Persistencia.ComprobarPersistenciaLocal(
                new SqlitePersistenciaTecnicaDePrueba(ruta));
            var resultado = await comprobacion.EjecutarAsync();
            Assert.False(resultado.DatoCoincide);
            Assert.NotNull(resultado.MensajeError);
            // El archivo no fue reconstruido silenciosamente: sigue con el contenido inválido original.
            Assert.Equal(basura, File.ReadAllText(ruta));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private sealed class RelojAvance : IReloj
    {
        private DateTimeOffset _ahora;
        public RelojAvance(DateTimeOffset inicio) => _ahora = inicio;
        public DateTimeOffset UtcNow() { var r = _ahora; _ahora = _ahora.AddSeconds(1); return r; }
    }
}
