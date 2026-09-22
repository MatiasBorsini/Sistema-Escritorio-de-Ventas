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

namespace LocalInfantil.Tests.Inventario;

/// <summary>
/// Prueba integral F03-T03+T04 sobre base temporal aislada, eliminada al
/// terminar. Sin base provisional ni datos reales. Los permisos nuevos se
/// conceden solo en matrices de prueba.
/// </summary>
public sealed class AjustesHistorialTests
{
    [Fact]
    public async Task Flujo_ajustes_existencias_e_historial()
    {
        using var entorno = await EntornoInventario.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-A-004", "4", null), entorno.Sesion);
        var v2 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-A-006", "6", null), entorno.Sesion);

        await entorno.Carga.EjecutarAsync(
            new List<FilaCargaInicial> { new(v1, 5), new(v2, 8) },
            "op-inicial-001",
            entorno.Sesion);

        entorno.AvanzarReloj(TimeSpan.FromMinutes(1));
        await entorno.Ajuste.EjecutarAsync(v1, 3, "Sobran teñidas", "op-aj-001", entorno.Sesion);
        entorno.AvanzarReloj(TimeSpan.FromMinutes(1));
        await entorno.Ajuste.EjecutarAsync(v1, -2, "Muestra vidriera", "op-aj-002", entorno.Sesion);

        var e1 = await entorno.Consultas.ObtenerExistenciaAsync("REM-A-004", entorno.Sesion);
        var e2 = await entorno.Consultas.ObtenerExistenciaAsync("REM-A-006", entorno.Sesion);
        Assert.NotNull(e1);
        Assert.NotNull(e2);
        Assert.Equal(6, e1.Existencias);
        Assert.Equal(v1, e1.VarianteId);
        Assert.Equal(8, e2.Existencias);
        Assert.Null(await entorno.Consultas.ObtenerExistenciaAsync("REM-X-000", entorno.Sesion));

        var pagina1 = await entorno.Consultas.HistorialPorSkuAsync("REM-A-004", 2, 0, entorno.Sesion);
        var pagina2 = await entorno.Consultas.HistorialPorSkuAsync("REM-A-004", 2, 2, entorno.Sesion);
        Assert.Equal(["inicial", "ajuste"], pagina1.Select(m => m.Tipo));
        Assert.Equal(["ajuste"], pagina2.Select(m => m.Tipo));
        Assert.All(pagina1.Concat(pagina2), m => Assert.False(string.IsNullOrWhiteSpace(m.OperacionId)));
        Assert.Equal("op-aj-001", pagina1[1].OperacionId);
        Assert.All(pagina1.Concat(pagina2), m => Assert.False(string.IsNullOrWhiteSpace(m.CreadoEn)));
    }

    [Fact]
    public async Task Repetir_operacion_no_duplica_y_rechaza_invalidos()
    {
        using var entorno = await EntornoInventario.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-B-001", null, null), entorno.Sesion);
        await entorno.Carga.EjecutarAsync(new List<FilaCargaInicial> { new(v1, 5) }, "op-ini-001", entorno.Sesion);
        await entorno.Ajuste.EjecutarAsync(v1, 2, "Suma", "op-aj-010", entorno.Sesion);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            entorno.Ajuste.EjecutarAsync(v1, 2, "Suma", "op-aj-010", entorno.Sesion));
        Assert.Equal(7, (await entorno.Consultas.ObtenerExistenciaAsync("REM-B-001", entorno.Sesion))!.Existencias);

        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Ajuste.EjecutarAsync(v1, 0, "Cero", "op-aj-011", entorno.Sesion));
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Ajuste.EjecutarAsync(v1, 1, "  ", "op-aj-012", entorno.Sesion));
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Ajuste.EjecutarAsync(v1, -100, "Exceso", "op-aj-013", entorno.Sesion));
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Ajuste.EjecutarAsync("variante-inexistente", 1, "X", "op-aj-014", entorno.Sesion));
        Assert.Equal(7, (await entorno.Consultas.ObtenerExistenciaAsync("REM-B-001", entorno.Sesion))!.Existencias);
    }

    [Fact]
    public async Task Repetir_operacion_tras_reapertura_no_duplica()
    {
        using var entorno = await EntornoInventario.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-E-001", null, null), entorno.Sesion);
        await entorno.Carga.EjecutarAsync(new List<FilaCargaInicial> { new(v1, 5) }, "op-ini-r1", entorno.Sesion);
        await entorno.Ajuste.EjecutarAsync(v1, 2, "Suma", "op-reap-001", entorno.Sesion);

        var reabierto = entorno.AjusteReabierto();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reabierto.EjecutarAsync(v1, 2, "Suma", "op-reap-001", entorno.Sesion));
        Assert.Equal(7, (await entorno.Consultas.ObtenerExistenciaAsync("REM-E-001", entorno.Sesion))!.Existencias);
    }

    [Fact]
    public async Task Fallo_intermedio_no_persiste_movimiento_ni_auditoria()
    {
        using var entorno = await EntornoInventario.CrearAsync();
        var ids = new GeneradorIds();
        var movimientos = new RepositorioMovimientos(ids);
        var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), new RelojFijo(MomentoFijo));
        var fabrica = entorno.Fabrica;

        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var varianteId = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-F-001", null, null), entorno.Sesion);

        await using (var sesion = await fabrica.IniciarAsync())
        {
            await movimientos.RegistrarAsync(
                sesion,
                new NuevoMovimiento(varianteId, "ajuste", 1, "Previo", "op-falla-001", null),
                Fecha);
            await Assert.ThrowsAsync<SqliteException>(() => auditoria.RegistrarAsync(
                sesion,
                new NuevoEventoAuditoria("op-falla-001", "inventario.ajuste", ActorAuditoria.Usuario, null, "variante", varianteId, null)));
        }

        Assert.Equal(0, await entorno.MovimientosDeAsync("op-falla-001"));
        Assert.Equal(0, await entorno.AuditoriaDeAsync("op-falla-001"));
        Assert.Equal(0, await entorno.ExistenciasDeAsync(varianteId));
    }

    [Fact]
    public async Task Escrituras_denegadas_sin_sesion_o_permiso()
    {
        using var entorno = await EntornoInventario.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-C-001", null, null), entorno.Sesion);

        await Assert.ThrowsAsync<SesionInvalidaException>(() =>
            entorno.Ajuste.EjecutarAsync(v1, 1, "X", "op-s1", null));
        await Assert.ThrowsAsync<SesionInvalidaException>(() =>
            entorno.Consultas.ObtenerExistenciaAsync("REM-C-001", null));

        using var denegado = await EntornoInventario.CrearAsyncMatrizVacia();
        var sesion = await denegado.SesionValidaAsync();
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            denegado.Ajuste.EjecutarAsync(v1, 1, "X", "op-s2", sesion));
    }

    [Fact]
    public async Task Desactivar_conserva_historial_y_compensatorio_funciona()
    {
        using var entorno = await EntornoInventario.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-D-001", null, null), entorno.Sesion);
        await entorno.Carga.EjecutarAsync(new List<FilaCargaInicial> { new(v1, 6) }, "op-ini-d", entorno.Sesion);
        await entorno.Ajuste.EjecutarAsync(v1, -6, "Rotura", "op-aj-d1", entorno.Sesion);
        await entorno.Ajuste.EjecutarAsync(v1, 6, "Compensa op-aj-d1: reclasificada apta", "op-aj-d2", entorno.Sesion);
        Assert.Equal(6, (await entorno.Consultas.ObtenerExistenciaAsync("REM-D-001", entorno.Sesion))!.Existencias);

        await entorno.DesactivarVariante.EjecutarAsync(v1, entorno.Sesion);
        var historial = await entorno.Consultas.HistorialPorSkuAsync("REM-D-001", 10, 0, entorno.Sesion);
        Assert.Equal(3, historial.Count);
        Assert.Contains(historial, m => m.Motivo != null && m.Motivo.Contains("op-aj-d1"));
    }

    [Fact]
    public async Task Matriz_productiva_deniega_ajuste_a_todos_los_roles()
    {
        using var entorno = await EntornoInventario.CrearAsync();
        var matriz = MatrizPermisos.ConValoresConfirmados();

        Assert.False(matriz.Permite(RolesLocal.Administrador, PermisosInventario.AjusteStock));
        Assert.False(matriz.Permite(RolesLocal.Duena, PermisosInventario.AjusteStock));
        Assert.False(matriz.Permite(RolesLocal.Empleada, PermisosInventario.AjusteStock));
        _ = entorno;
    }

    private static readonly DateTimeOffset MomentoFijo = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private const string Fecha = "2026-09-21T12:00:00Z";

    private sealed class EntornoInventario : IDisposable
    {
        private readonly string _directorio;
        private readonly FabricaConexionesSqlite _fabrica;
        private readonly GeneradorIds _ids;
        private readonly RelojFijo _reloj;
        private readonly GestorSesiones _sesiones;
        private readonly MatrizPermisos _matriz;

        public readonly CrearProducto CrearProducto;
        public readonly CrearVariante CrearVariante;
        public readonly DesactivarVariante DesactivarVariante;
        public readonly CargarExistenciaInicial Carga;
        public readonly AjustarInventario Ajuste;
        public readonly ConsultasInventario Consultas;
        public FabricaConexionesSqlite Fabrica => _fabrica;

        private string? _sesionId;

        public string Sesion => _sesionId ?? throw new InvalidOperationException("Sin sesión registrada.");

        private EntornoInventario(string directorio, FabricaConexionesSqlite fabrica, MatrizPermisos matriz)
        {
            _directorio = directorio;
            _fabrica = fabrica;
            _ids = new GeneradorIds();
            var reloj = new RelojFijo(MomentoFijo);
            _reloj = reloj;
            _sesiones = new GestorSesiones();
            _matriz = matriz;
            var productos = new RepositorioProductos(_ids);
            var variantes = new RepositorioVariantes(_ids);
            var movimientos = new RepositorioMovimientos(_ids);
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
            var autorizador = new Autorizador(_sesiones, new RepositorioUsuarios(_ids), new RepositorioPermisos(), _fabrica, matriz);
            CrearProducto = new CrearProducto(productos, autorizador, _fabrica, auditoria, _ids, reloj);
            CrearVariante = new CrearVariante(variantes, productos, autorizador, _fabrica, auditoria, _ids, reloj);
            DesactivarVariante = new DesactivarVariante(variantes, autorizador, _fabrica, auditoria, _ids);
            Carga = new CargarExistenciaInicial(movimientos, variantes, autorizador, _fabrica, auditoria, reloj);
            Ajuste = new AjustarInventario(movimientos, variantes, autorizador, _fabrica, auditoria, reloj);
            Consultas = new ConsultasInventario(movimientos, variantes, new RepositorioConteos(new GeneradorIds()), _sesiones, _fabrica);
        }

        public static async Task<EntornoInventario> CrearAsync()
        {
            var matriz = MatrizPermisos.ConValoresConfirmados();
            matriz.Permitir(RolesLocal.Empleada, PermisosCatalogo.ProductosGestionar);
            matriz.Permitir(RolesLocal.Empleada, PermisosCatalogo.VariantesGestionar);
            matriz.Permitir(RolesLocal.Empleada, PermisosInventario.CargaInicialEstablecer);
            matriz.Permitir(RolesLocal.Empleada, PermisosInventario.AjusteStock);
            return await CrearConMatrizAsync(matriz);
        }

        public static async Task<EntornoInventario> CrearAsyncMatrizVacia()
        {
            return await CrearConMatrizAsync(MatrizPermisos.Vacia());
        }

        public AjustarInventario AjusteReabierto()
        {
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(Path.Combine(_directorio, "inventario-f01.db")));
            var movimientos = new RepositorioMovimientos(_ids);
            var variantes = new RepositorioVariantes(_ids);
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), _reloj);
            var autorizador = new Autorizador(_sesiones, new RepositorioUsuarios(_ids), new RepositorioPermisos(), fabrica, _matriz);
            return new AjustarInventario(movimientos, variantes, autorizador, fabrica, auditoria, _reloj);
        }

        public async Task<string> SesionValidaAsync()
        {
            if (_sesionId is not null)
            {
                return _sesionId;
            }

            await using var sesion = await _fabrica.IniciarAsync();
            var usuarios = new RepositorioUsuarios(_ids);
            var hash = new Pbkdf2HashContrasenas();
            var id = await usuarios.CrearAsync(sesion, new NuevoUsuario("lector", RolesLocal.Empleada), hash.CrearHuella("clave-lectora-00"), Fecha);
            await sesion.ConfirmarAsync();
            var valida = new SesionUsuario("sesion-lectora", id, "lector", RolesLocal.Empleada, Fecha);
            _sesiones.Registrar(valida);
            _sesionId = valida.IdSesion;
            return valida.IdSesion;
        }

        public void AvanzarReloj(TimeSpan paso)
        {
            _reloj.Avanzar(paso);
        }

        public async Task<long> ExistenciasDeAsync(string varianteId)
        {
            var movimientos = new RepositorioMovimientos(_ids);
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            return await movimientos.SumarPorVarianteAsync(sesion, varianteId);
        }

        public async Task<long> MovimientosDeAsync(string operacionId)
        {
            await using var conexion = await AbrirCruda();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT COUNT(*) FROM movimiento_inventario WHERE operacion_id = $op;";
            comando.Parameters.AddWithValue("$op", operacionId);
            return Convert.ToInt64(await comando.ExecuteScalarAsync());
        }

        public async Task<long> AuditoriaDeAsync(string operacionId)
        {
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), _reloj);
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            return (await auditoria.ObtenerPorOperacionAsync(sesion, operacionId)).Count;
        }

        private static async Task<EntornoInventario> CrearConMatrizAsync(MatrizPermisos matriz)
        {
            var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(directorio, "inventario-f01.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var entorno = new EntornoInventario(directorio, new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta)), matriz);
            await entorno.SesionValidaAsync();
            return entorno;
        }

        private async Task<SqliteConnection> AbrirCruda()
        {
            var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_directorio, "inventario-f01.db"),
                Pooling = false,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            await conexion.OpenAsync();
            return conexion;
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
