using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Compras;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Infrastructure.Seguridad;
using LocalInfantil.Tests.Fakes;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Tests.Wpf;

public sealed class WpfStocktakeRecoveryTests
{
    [Fact]
    public async Task Recepcion_durable_tras_reinicio_no_duplica()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var ruta = e.Ruta;
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        var op = Guid.NewGuid().ToString("N");
        var lineas = new[] { new LineaRecepcion(v1, 3, null, null) };
        AlmacenRecepcionPendiente.Guardar(ruta, op, prov, null, lineas);
        await e.ConfirmarRecepcion.EjecutarAsync(prov, lineas, op, null, e.SesionDuena);
        Assert.True(AlmacenRecepcionPendiente.Existe(ruta));
        var svc2 = new ServiciosComerciales(ruta);
        var ses2 = await svc2.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc2, () => ses2.IdSesion);
        await vm.CargarProveedoresAsync();
        Assert.Contains("ya quedó registrada", vm.Mensaje ?? "");
        Assert.False(AlmacenRecepcionPendiente.Existe(ruta));
        var recepciones = await svc2.ConsultasRecepciones.ListarAsync(10, 0, ses2.IdSesion);
        Assert.Single(recepciones);
    }

    [Fact]
    public async Task Recepcion_pendiente_no_registrada_permite_reintento_mismo_id()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionRevertir, true, e.SesionDuena);
        var ruta = e.Ruta;
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        var op = Guid.NewGuid().ToString("N");
        var lineas = new[] { new LineaRecepcion(v1, 2, null, null) };
        AlmacenRecepcionPendiente.Guardar(ruta, op, prov, null, lineas);
        var svc2 = new ServiciosComerciales(ruta);
        var ses2 = await svc2.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc2, () => ses2.IdSesion);
        await vm.CargarProveedoresAsync();
        await vm.VerificarPendienteAsync();
        Assert.Contains("Pendiente incierto", vm.Mensaje ?? "");
        Assert.Equal(op, vm.OperacionId);
        var sku = (await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena))!.Variantes.First(x => x.Id == v1).Sku;
        vm.ProveedorSeleccionado = vm.Proveedores.First(p => p.Id == prov);
        vm.Filas.Clear();
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = sku, CantidadTexto = "2" });
        await vm.ConfirmarAsyncPublic();
        Assert.Null(vm.Error);
        var recepciones = await svc2.ConsultasRecepciones.ListarAsync(10, 0, ses2.IdSesion);
        Assert.Single(recepciones);
        Assert.False(AlmacenRecepcionPendiente.Existe(ruta));
    }

    [Fact]
    public void Parser_ARS_admite_1_234_50_y_rechaza_ambiguos()
    {
        Assert.True(TryParseArsLocal("1.234,50", out var v1) && v1 == 123450);
        Assert.True(TryParseArsLocal("1234,50", out var v2) && v2 == 123450);
        Assert.True(TryParseArsLocal("1234.50", out var v3) && v3 == 123450);
        Assert.True(TryParseArsLocal("0", out var v4) && v4 == 0);
        Assert.False(TryParseArsLocal("-5", out _));
        Assert.False(TryParseArsLocal("1,234.50", out _));
        Assert.False(TryParseArsLocal("1.234,567", out _));
        Assert.False(TryParseArsLocal("9999999999999999999,99", out _));
    }

    private static bool TryParseArsLocal(string t, out long minor)
    {
        var m = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetMethod("TryParseArs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var args = new object[] { t, 0L };
        var ok = (bool)m.Invoke(null, args)!;
        minor = (long)args[1];
        return ok;
    }

    [Fact]
    public async Task Conteo_parcial_no_afecta_omitidas_y_borrador_recuperable()
    {
        using var e = await EntornoConteo.CrearAsync();
        var prod = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod", null, null), e.SesionDuena);
        var v1 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, "SKU-1", "M", null), e.SesionDuena);
        var v2 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, "SKU-2", null, null), e.SesionDuena);
        await e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(v1, 10), new FilaCargaInicial(v2, 5) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        var conteoId = await e.CrearConteo.EjecutarAsync(new[] { new FilaConteo(v1, 7) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        var informe = await e.ConsultasConteos.InformeAsync(conteoId, e.SesionDuena);
        Assert.Single(informe);
        var ruta = e.Ruta;
        using var e2 = await EntornoConteo.CrearDesdeRutaAsync(ruta);
        var lista = await e2.ConsultasConteos.ListarAsync(10, 0, e2.SesionDuena);
        Assert.Contains(lista, c => c.Id == conteoId);
    }

    [Fact]
    public async Task Conteo_correccion_y_aprobacion_diferencias()
    {
        using var e = await EntornoConteo.CrearAsync();
        var prod = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod", null, null), e.SesionDuena);
        var v1 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, "SKU-1", null, null), e.SesionDuena);
        var v2 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, "SKU-2", null, null), e.SesionDuena);
        await e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(v1, 10), new FilaCargaInicial(v2, 10) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        var cid = await e.CrearConteo.EjecutarAsync(new[] { new FilaConteo(v1, 12), new FilaConteo(v2, 8) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        await e.CorregirConteo.EjecutarAsync(cid, new[] { new FilaConteo(v1, 15), new FilaConteo(v2, 8) }, e.SesionDuena);
        var informe = await e.ConsultasConteos.InformeAsync(cid, e.SesionDuena);
        Assert.Contains(informe, x => x.VarianteId == v1 && x.Contado == 15 && x.Diferencia == 5);
        await e.AprobarConteo.EjecutarAsync(cid, Guid.NewGuid().ToString("N"), e.SesionDuena);
        var ficha = await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.Equal(15, ficha!.Variantes.First(x => x.Id == v1).Existencias);
        var cid2 = await e.CrearConteo.EjecutarAsync(new[] { new FilaConteo(v1, 15) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        await e.AprobarConteo.EjecutarAsync(cid2, Guid.NewGuid().ToString("N"), e.SesionDuena);
        var ficha2 = await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.Equal(15, ficha2!.Variantes.First(x => x.Id == v1).Existencias);
    }

    [Fact]
    public async Task Conteo_foto_vieja_y_doble_aprobacion()
    {
        using var e = await EntornoConteo.CrearAsync();
        var prod = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod", null, null), e.SesionDuena);
        var v1 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, "SKU-1", null, null), e.SesionDuena);
        await e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(v1, 10) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        var cid = await e.CrearConteo.EjecutarAsync(new[] { new FilaConteo(v1, 12) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        await e.AjustarInventario.EjecutarAsync(v1, 2, "ajuste", Guid.NewGuid().ToString("N"), e.SesionDuena);
        await e.AjustarInventario.EjecutarAsync(v1, -2, "ajuste", Guid.NewGuid().ToString("N"), e.SesionDuena);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.AprobarConteo.EjecutarAsync(cid, Guid.NewGuid().ToString("N"), e.SesionDuena));
        var cid2 = await e.CrearConteo.EjecutarAsync(new[] { new FilaConteo(v1, 10) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        await e.AprobarConteo.EjecutarAsync(cid2, Guid.NewGuid().ToString("N"), e.SesionDuena);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.AprobarConteo.EjecutarAsync(cid2, Guid.NewGuid().ToString("N"), e.SesionDuena));
        var ruta = e.Ruta;
        using var e2 = await EntornoConteo.CrearDesdeRutaAsync(ruta);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e2.AprobarConteo.EjecutarAsync(cid2, Guid.NewGuid().ToString("N"), e2.SesionDuena));
    }



    [Fact]
    public async Task ViewModel_conteo_comandos_reales()
    {
        using var e = await EntornoConteo.CrearAsync();
        var prod = await e.CrearProducto.EjecutarAsync(new NuevoProducto("ProdVM", null, null), e.SesionDuena);
        var v1 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, "SKU-VM", null, null), e.SesionDuena);
        await e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(v1, 10) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        var svc = new ServiciosComerciales(e.Ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.ConteoViewModel(svc, () => ses.IdSesion);
        vm.FilasSeleccion.First().Sku = "SKU-VM";
        vm.FilasSeleccion.First().CantidadTexto = "12";
        await vm.CrearAsyncPublic();
        Assert.Null(vm.Error);
        Assert.Single(vm.Conteos);
        vm.Seleccionado = vm.Conteos.First();
        Assert.NotNull(vm.Informe);
        vm.FilasSeleccion.First().CantidadTexto = "15";
        await vm.CorregirAsyncPublic();
        Assert.Null(vm.Error);
        await vm.SolicitarAprobarAsyncPublic();
        Assert.True(vm.ConfirmacionAprobarVisible);
        await vm.AprobarAsyncPublic();
        Assert.Null(vm.Error);
        // Logout debe impedir
        await svc.CasoCerrarSesion.EjecutarAsync(ses.IdSesion);
        vm.FilasSeleccion.First().Sku = "SKU-VM";
        vm.FilasSeleccion.First().CantidadTexto = "10";
        await vm.CrearAsyncPublic();
        Assert.Contains("Sesión inválida", vm.Error ?? "");
    }

    [Fact]
    public async Task Conteo_paginacion_25_recorre_y_recupera_borrador()
    {
        using var e = await EntornoConteo.CrearAsync();
        var prod = await e.CrearProducto.EjecutarAsync(new NuevoProducto("ProdPag", null, null), e.SesionDuena);
        var v1 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, "SKU-PAG", null, null), e.SesionDuena);
        await e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(v1, 50) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        for (var i = 0; i < 25; i++)
            await e.CrearConteo.EjecutarAsync(new[] { new FilaConteo(v1, i + 1) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        var svc = new ServiciosComerciales(e.Ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.ConteoViewModel(svc, () => ses.IdSesion);
        await vm.CargarConteosAsync();
        Assert.Equal(20, vm.Conteos.Count);
        await vm.SiguienteAsyncPublic();
        Assert.Equal(5, vm.Conteos.Count);
        await vm.AnteriorAsyncPublic();
        Assert.Equal(20, vm.Conteos.Count);
        // Recuperar un borrador de la segunda página (orden estable del repositorio).
        await vm.SiguienteAsyncPublic();
        var borrador = vm.Conteos.First();
        vm.Seleccionado = borrador;
        for (var t = 0; t < 50 && vm.Informe.Count == 0; t++) await Task.Delay(100);
        Assert.Single(vm.Informe);
        Assert.Equal("SKU-PAG", vm.Informe[0].Sku);
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
        public ConsultasProveedores ConsultasProv { get; }
        public CrearProducto CrearProducto { get; }
        public CrearVariante CrearVariante { get; }
        public ConsultasCatalogo ConsultasCatalogo { get; }
        public ConfirmarRecepcion ConfirmarRecepcion { get; }
        public ConsultasRecepciones ConsultasRecepciones { get; }
        public string SesionDuena { get; }
        public string DuenaId { get; }
        public ConfigurarPermiso Permisos { get; }
        private readonly string _dir;
        public Entorno(string dir, string ruta, FabricaConexionesSqlite fabrica, CrearProveedor cp, ConsultasProveedores cprov, CrearProducto cprod, CrearVariante cvar, ConsultasCatalogo cc, ConfirmarRecepcion cr, ConsultasRecepciones cr2, string sesion, string duenaId, ConfigurarPermiso perm)
        { _dir = dir; Ruta = ruta; Fabrica = fabrica; CrearProveedor = cp; ConsultasProv = cprov; CrearProducto = cprod; CrearVariante = cvar; ConsultasCatalogo = cc; ConfirmarRecepcion = cr; ConsultasRecepciones = cr2; SesionDuena = sesion; DuenaId = duenaId; Permisos = perm; }
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
            var cprov = new ConsultasProveedores(provRepo, sesiones, fabrica);
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
                duenaId = (await usuarios.ObtenerPorNombreAsync(lecturaPrevia, "duena"))!.Id;
                sesId = (await login.EjecutarAsync("duena", "clave-duena-000000")).IdSesion;
            }
            return new Entorno(dir, ruta, fabrica, crearProv, cprov, crearProd, crearVar, consultasCat, confirmarRec, consultasRec, sesId, duenaId, perm);
        }
        public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    }

    private sealed class EntornoConteo : IDisposable
    {
        public string Ruta { get; }
        public FabricaConexionesSqlite Fabrica { get; }
        public CrearProducto CrearProducto { get; }
        public CrearVariante CrearVariante { get; }
        public CrearConteo CrearConteo { get; }
        public CorregirConteo CorregirConteo { get; }
        public AprobarConteo AprobarConteo { get; }
        public ConsultasConteos ConsultasConteos { get; }
        public ConsultasCatalogo ConsultasCatalogo { get; }
        public CargarExistenciaInicial Cargar { get; }
        public AjustarInventario AjustarInventario { get; }
        public ConfigurarPermiso Permisos { get; }
        public string SesionDuena { get; }
        private readonly string _dir;
        public EntornoConteo(string dir, string ruta, FabricaConexionesSqlite fabrica, CrearProducto cp, CrearVariante cv, CrearConteo cc, CorregirConteo cor, AprobarConteo apr, ConsultasConteos cons, ConsultasCatalogo cat, CargarExistenciaInicial car, AjustarInventario aj, ConfigurarPermiso perm, string sesion)
        { _dir = dir; Ruta = ruta; Fabrica = fabrica; CrearProducto = cp; CrearVariante = cv; CrearConteo = cc; CorregirConteo = cor; AprobarConteo = apr; ConsultasConteos = cons; ConsultasCatalogo = cat; Cargar = car; AjustarInventario = aj; Permisos = perm; SesionDuena = sesion; }
        public static async Task<EntornoConteo> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "conteo.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            return await CrearDesdeRutaAsync(ruta);
        }
        public static async Task<EntornoConteo> CrearDesdeRutaAsync(string ruta)
        {
            var dir = Path.GetDirectoryName(ruta)!;
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var ids = new GeneradorIds();
            var reloj = new RelojAvance(DateTimeOffset.Parse("2026-09-22T00:00:00Z"));
            var sesiones = new GestorSesiones();
            var usuarios = new RepositorioUsuarios(ids);
            var permisos = new RepositorioPermisos();
            var auditoria = new RepositorioAuditoria(new GeneradorIds(), reloj);
            var matriz = MatrizPermisos.ConValoresConfirmados();
            var autorizador = new Autorizador(sesiones, usuarios, permisos, fabrica, matriz);
            var hasher = new Pbkdf2HashContrasenas();
            var alta = new CrearUsuario(usuarios, hasher, autorizador, fabrica, auditoria, ids, reloj);
            var login = new IniciarSesion(usuarios, hasher, sesiones, fabrica, auditoria, ids, reloj, new OpcionesAutenticacion(0));
            var perm = new ConfigurarPermiso(permisos, usuarios, autorizador, fabrica, auditoria, ids, reloj);
            var prodRepo = new RepositorioProductos(ids);
            var varRepo = new RepositorioVariantes(ids);
            var movRepo = new RepositorioMovimientos(ids);
            var conteoRepo = new RepositorioConteos(ids);
            var crearProd = new CrearProducto(prodRepo, autorizador, fabrica, auditoria, ids, reloj);
            var crearVar = new CrearVariante(varRepo, prodRepo, autorizador, fabrica, auditoria, ids, reloj);
            var apartRepo = new RepositorioApartados(ids);
            var crearConteo = new CrearConteo(conteoRepo, varRepo, movRepo, apartRepo, autorizador, fabrica, auditoria, ids, reloj);
            var corregir = new CorregirConteo(conteoRepo, autorizador, fabrica, auditoria, ids);
            var aprobar = new AprobarConteo(conteoRepo, movRepo, apartRepo, autorizador, fabrica, auditoria, ids, reloj);
            var consultasConteos = new ConsultasConteos(conteoRepo, sesiones, fabrica);
            var consultasCat = new ConsultasCatalogo(prodRepo, varRepo, sesiones, fabrica);
            var cargar = new CargarExistenciaInicial(movRepo, varRepo, autorizador, fabrica, auditoria, reloj);
            var ajustar = new AjustarInventario(movRepo, varRepo, apartRepo, autorizador, fabrica, auditoria, reloj);
            string sesId;
            try
            {
                var duenaId = await alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
                sesId = (await login.EjecutarAsync("duena", "clave-duena-000000")).IdSesion;
                await perm.EjecutarAsync(duenaId, PermisosInventario.ConteoRegistrar, true, sesId);
                await perm.EjecutarAsync(duenaId, PermisosInventario.ConteoAprobar, true, sesId);
                await perm.EjecutarAsync(duenaId, PermisosCatalogo.ProductosGestionar, true, sesId);
                await perm.EjecutarAsync(duenaId, PermisosCatalogo.VariantesGestionar, true, sesId);
                await perm.EjecutarAsync(duenaId, PermisosInventario.CargaInicialEstablecer, true, sesId);
                await perm.EjecutarAsync(duenaId, PermisosInventario.AjusteStock, true, sesId);
            }
            catch
            {
                sesId = (await login.EjecutarAsync("duena", "clave-duena-000000")).IdSesion;
            }
            return new EntornoConteo(dir, ruta, fabrica, crearProd, crearVar, crearConteo, corregir, aprobar, consultasConteos, consultasCat, cargar, ajustar, perm, sesId);
        }
        public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    }

    private sealed class RelojAvance : IReloj
    {
        private DateTimeOffset _ahora;
        public RelojAvance(DateTimeOffset inicio) => _ahora = inicio;
        public DateTimeOffset UtcNow() { var r = _ahora; _ahora = _ahora.AddSeconds(1); return r; }
    }
}
