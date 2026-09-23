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
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Compras;

/// <summary>
/// Prueba integral F03-T01+T02 sobre base temporal aislada, eliminada al
/// terminar. Sin base provisional ni datos reales. Los permisos nuevos se
/// conceden solo en matrices de prueba.
/// </summary>
public sealed class ComprasTests
{
    [Fact]
    public async Task Recepcion_suma_existencias_con_costo_e_historial()
    {
        using var entorno = await EntornoCompras.CrearAsync();
        var proveedorId = await entorno.Proveedores.CrearProveedor.EjecutarAsync(new NuevoProveedor("Mayorista Ficticio"), entorno.Sesion);
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-C-004", "4", null), entorno.Sesion);
        var v2 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-C-006", "6", null), entorno.Sesion);

        var recepcionId = await entorno.Recepcion.EjecutarAsync(
            proveedorId,
            new List<LineaRecepcion> { new(v1, 10, 8000, "ARS"), new(v2, 6, null, null) },
            "op-rec-001",
            "FC-0001",
            entorno.Sesion);

        Assert.Equal(10, await entorno.ExistenciasDeAsync(v1));
        Assert.Equal(6, await entorno.ExistenciasDeAsync(v2));
        Assert.Equal("FC-0001", await entorno.ComprobanteDeAsync(recepcionId));
        Assert.Equal(proveedorId, await entorno.ProveedorDeAsync(recepcionId));
        Assert.Equal(entorno.LectorId, await entorno.ResponsableDeAsync(recepcionId));
        Assert.Equal(8000, await entorno.CostoDeAsync(recepcionId, v1));
        Assert.Null(await entorno.CostoDeAsync(recepcionId, v2));
        Assert.Equal(2, await entorno.MovimientosDeAsync("op-rec-001"));
        Assert.Equal(1, await entorno.AuditoriaDeAsync("op-rec-001"));
        var eventos = await entorno.EventosDeAsync("op-rec-001");
        Assert.Single(eventos);
        Assert.Equal("compra.recepcion_confirmada", eventos[0].TipoEvento);
        Assert.Equal(entorno.LectorId, eventos[0].ActorId);
    }

    [Fact]
    public async Task Costo_cero_se_distingue_de_desconocido()
    {
        using var entorno = await EntornoCompras.CrearAsync();
        var proveedorId = await entorno.Proveedores.CrearProveedor.EjecutarAsync(new NuevoProveedor("Mayorista"), entorno.Sesion);
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-0-001", null, null), entorno.Sesion);
        var v2 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-0-002", null, null), entorno.Sesion);

        var recepcionId = await entorno.Recepcion.EjecutarAsync(
            proveedorId,
            new List<LineaRecepcion> { new(v1, 2, 0, "ARS"), new(v2, 2, null, null) },
            "op-costo-001",
            null,
            entorno.Sesion);

        Assert.Equal(0, await entorno.CostoDeAsync(recepcionId, v1));
        Assert.Null(await entorno.CostoDeAsync(recepcionId, v2));
    }

    [Fact]
    public async Task Repetir_operacion_tras_reapertura_no_duplica()
    {
        using var entorno = await EntornoCompras.CrearAsync();
        var proveedorId = await entorno.Proveedores.CrearProveedor.EjecutarAsync(new NuevoProveedor("Mayorista"), entorno.Sesion);
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-R-001", null, null), entorno.Sesion);
        await entorno.Recepcion.EjecutarAsync(
            proveedorId, new List<LineaRecepcion> { new(v1, 4, null, null) }, "op-rec-dup", null, entorno.Sesion);

        var reabierta = entorno.RecepcionReabierta();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reabierta.EjecutarAsync(
            proveedorId, new List<LineaRecepcion> { new(v1, 4, null, null) }, "op-rec-dup", null, entorno.Sesion));
        Assert.Equal(4, await entorno.ExistenciasDeAsync(v1));
        Assert.Equal(1, await entorno.MovimientosDeAsync("op-rec-dup"));
    }

    [Fact]
    public async Task Fallo_en_linea_revierte_todo()
    {
        using var entorno = await EntornoCompras.CrearAsync();
        var proveedorId = await entorno.Proveedores.CrearProveedor.EjecutarAsync(new NuevoProveedor("Mayorista"), entorno.Sesion);
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-F-001", null, null), entorno.Sesion);

        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Recepcion.EjecutarAsync(
            proveedorId,
            new List<LineaRecepcion> { new(v1, 3, null, null), new("variante-inexistente", 2, null, null) },
            "op-rec-falla",
            null,
            entorno.Sesion));

        Assert.Equal(0, await entorno.MovimientosDeAsync("op-rec-falla"));
        Assert.Equal(0, await entorno.AuditoriaDeAsync("op-rec-falla"));
        Assert.Equal(0, await entorno.RecepcionesDeAsync("op-rec-falla"));
        Assert.Equal(0, await entorno.ExistenciasDeAsync(v1));
    }

    [Fact]
    public async Task Filas_invalidas_se_rechazan()
    {
        using var entorno = await EntornoCompras.CrearAsync();
        var proveedorId = await entorno.Proveedores.CrearProveedor.EjecutarAsync(new NuevoProveedor("Mayorista"), entorno.Sesion);
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-I-001", null, null), entorno.Sesion);

        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Recepcion.EjecutarAsync(
            proveedorId, new List<LineaRecepcion> { new(v1, 0, null, null) }, "op-i-001", null, entorno.Sesion));
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Recepcion.EjecutarAsync(
            proveedorId, new List<LineaRecepcion> { new(v1, -2, null, null) }, "op-i-002", null, entorno.Sesion));
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Recepcion.EjecutarAsync(
            proveedorId, new List<LineaRecepcion> { new(v1, 1, 5000, null) }, "op-i-003", null, entorno.Sesion));
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Recepcion.EjecutarAsync(
            proveedorId, new List<LineaRecepcion> { new(v1, 1, null, "ARS") }, "op-i-004", null, entorno.Sesion));
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Recepcion.EjecutarAsync(
            proveedorId, new List<LineaRecepcion> { new(v1, 1, null, null), new(v1, 2, null, null) }, "op-i-005", null, entorno.Sesion));
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Recepcion.EjecutarAsync(
            proveedorId, new List<LineaRecepcion>(), "op-i-006", null, entorno.Sesion));
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Recepcion.EjecutarAsync(
            "proveedor-inexistente", new List<LineaRecepcion> { new(v1, 1, null, null) }, "op-i-007", null, entorno.Sesion));
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Recepcion.EjecutarAsync(
            proveedorId, new List<LineaRecepcion> { new("variante-inexistente", 1, null, null) }, "op-i-008", null, entorno.Sesion));
        Assert.Equal(0, await entorno.ExistenciasDeAsync(v1));
    }

    [Fact]
    public async Task Desactivar_proveedor_conserva_historial()
    {
        using var entorno = await EntornoCompras.CrearAsync();
        var proveedorId = await entorno.Proveedores.CrearProveedor.EjecutarAsync(new NuevoProveedor("Mayorista"), entorno.Sesion);
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-H-001", null, null), entorno.Sesion);
        await entorno.Recepcion.EjecutarAsync(
            proveedorId, new List<LineaRecepcion> { new(v1, 5, 7000, "ARS") }, "op-rec-h", null, entorno.Sesion);

        await entorno.Proveedores.Desactivar.EjecutarAsync(proveedorId, entorno.Sesion);

        var proveedor = await entorno.Proveedores.Consultas.ObtenerAsync(proveedorId, entorno.Sesion);
        Assert.NotNull(proveedor);
        Assert.False(proveedor.Activo);
        await entorno.Proveedores.Editar.EjecutarAsync(proveedorId, new EdicionProveedor("Mayorista Editado"), entorno.Sesion);
        Assert.Equal("Mayorista Editado", (await entorno.Proveedores.Consultas.ObtenerAsync(proveedorId, entorno.Sesion))!.Nombre);
        Assert.Equal(1, await entorno.RecepcionesDeAsync("op-rec-h"));
        Assert.Equal(5, await entorno.ExistenciasDeAsync(v1));
        Assert.Empty(await entorno.Proveedores.Consultas.ListarAsync(entorno.Sesion, soloActivos: true));
        Assert.Single(await entorno.Proveedores.Consultas.ListarAsync(entorno.Sesion, soloActivos: false));
    }

    [Fact]
    public async Task Escrituras_denegadas_sin_sesion_o_permiso()
    {
        using var entorno = await EntornoCompras.CrearAsync();

        await Assert.ThrowsAsync<SesionInvalidaException>(() =>
            entorno.Proveedores.CrearProveedor.EjecutarAsync(new NuevoProveedor("X"), null));
        await Assert.ThrowsAsync<SesionInvalidaException>(() =>
            entorno.Recepcion.EjecutarAsync("p", new List<LineaRecepcion>(), "op", null, null));

        using var denegado = await EntornoCompras.CrearAsyncMatrizVacia();
        var sesion = await denegado.SesionValidaAsync();
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            denegado.Proveedores.CrearProveedor.EjecutarAsync(new NuevoProveedor("X"), sesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            denegado.Recepcion.EjecutarAsync("p", new List<LineaRecepcion>(), "op", null, sesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            denegado.Proveedores.Editar.EjecutarAsync("p", new EdicionProveedor("Y"), sesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            denegado.Proveedores.Desactivar.EjecutarAsync("p", sesion));
    }

    [Fact]
    public async Task Matriz_productiva_deniega_compras_a_todos_los_roles()
    {
        using var entorno = await EntornoCompras.CrearAsync();
        var matriz = MatrizPermisos.ConValoresConfirmados();

        Assert.False(matriz.Permite(RolesLocal.Administrador, PermisosCompras.ProveedoresGestionar));
        Assert.False(matriz.Permite(RolesLocal.Administrador, PermisosCompras.RecepcionConfirmar));
        Assert.False(matriz.Permite(RolesLocal.Duena, PermisosCompras.ProveedoresGestionar));
        Assert.False(matriz.Permite(RolesLocal.Duena, PermisosCompras.RecepcionConfirmar));
        Assert.False(matriz.Permite(RolesLocal.Empleada, PermisosCompras.ProveedoresGestionar));
        Assert.False(matriz.Permite(RolesLocal.Empleada, PermisosCompras.RecepcionConfirmar));
        _ = entorno;
    }

    [Fact]
    public async Task Carga_inicial_y_ajustes_siguen_funcionando()
    {
        using var entorno = await EntornoCompras.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-S-001", null, null), entorno.Sesion);

        await entorno.Carga.EjecutarAsync(new List<FilaCargaInicial> { new(v1, 10) }, "op-carga-s", entorno.Sesion);
        await entorno.Ajuste.EjecutarAsync(v1, -3, "Merma", "op-ajuste-s", entorno.Sesion);
        Assert.Equal(7, await entorno.ExistenciasDeAsync(v1));
    }

    private static readonly DateTimeOffset MomentoFijo = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private const string Fecha = "2026-09-21T12:00:00Z";

    private sealed class JuegoProveedores
    {
        public required CrearProveedor CrearProveedor { get; init; }
        public required EditarProveedor Editar { get; init; }
        public required DesactivarProveedor Desactivar { get; init; }
        public required ConsultasProveedores Consultas { get; init; }
    }

    private sealed class EntornoCompras : IDisposable
    {
        private readonly string _directorio;
        private readonly FabricaConexionesSqlite _fabrica;
        private readonly GeneradorIds _ids;
        private readonly RelojFijo _reloj;
        private readonly GestorSesiones _sesiones;

        public readonly CrearProducto CrearProducto;
        public readonly CrearVariante CrearVariante;
        public readonly CargarExistenciaInicial Carga;
        public readonly AjustarInventario Ajuste;
        public readonly ConfirmarRecepcion Recepcion;
        public readonly ConsultasInventario ConsultasInv;
        public readonly JuegoProveedores Proveedores;
        public readonly Autorizador Autorizador;

        private string? _sesionId;

        public string Sesion => _sesionId ?? throw new InvalidOperationException("Sin sesión registrada.");
        public string LectorId => _lectorId ?? throw new InvalidOperationException("Sin lector registrado.");

        private string? _lectorId;

        private EntornoCompras(string directorio, FabricaConexionesSqlite fabrica, MatrizPermisos matriz)
        {
            _directorio = directorio;
            _fabrica = fabrica;
            _ids = new GeneradorIds();
            var reloj = new RelojFijo(MomentoFijo);
            _reloj = reloj;
            _sesiones = new GestorSesiones();
            var productos = new RepositorioProductos(_ids);
            var variantes = new RepositorioVariantes(_ids);
            var movimientos = new RepositorioMovimientos(_ids);
            var apartados = new RepositorioApartados(_ids);
            var proveedores = new RepositorioProveedores(_ids);
            var recepciones = new RepositorioRecepciones(_ids);
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
            var autorizador = new Autorizador(_sesiones, new RepositorioUsuarios(_ids), new RepositorioPermisos(), _fabrica, matriz);
            Autorizador = autorizador;
            CrearProducto = new CrearProducto(productos, autorizador, _fabrica, auditoria, _ids, reloj);
            CrearVariante = new CrearVariante(variantes, productos, autorizador, _fabrica, auditoria, _ids, reloj);
            Carga = new CargarExistenciaInicial(movimientos, variantes, autorizador, _fabrica, auditoria, reloj);
            Ajuste = new AjustarInventario(movimientos, variantes, apartados, autorizador, _fabrica, auditoria, reloj);
            Recepcion = new ConfirmarRecepcion(recepciones, proveedores, variantes, movimientos, autorizador, _fabrica, auditoria, reloj);
            ConsultasInv = new ConsultasInventario(movimientos, variantes, new RepositorioConteos(new GeneradorIds()), apartados, _sesiones, _fabrica);
            Proveedores = new JuegoProveedores
            {
                CrearProveedor = new CrearProveedor(proveedores, autorizador, _fabrica, auditoria, _ids, reloj),
                Editar = new EditarProveedor(proveedores, autorizador, _fabrica, auditoria, _ids),
                Desactivar = new DesactivarProveedor(proveedores, autorizador, _fabrica, auditoria, _ids),
                Consultas = new ConsultasProveedores(proveedores, _sesiones, _fabrica),
            };
        }

        public static async Task<EntornoCompras> CrearAsync()
        {
            var matriz = MatrizPermisos.ConValoresConfirmados();
            matriz.Permitir(RolesLocal.Empleada, PermisosCatalogo.ProductosGestionar);
            matriz.Permitir(RolesLocal.Empleada, PermisosCatalogo.VariantesGestionar);
            matriz.Permitir(RolesLocal.Empleada, PermisosInventario.CargaInicialEstablecer);
            matriz.Permitir(RolesLocal.Empleada, PermisosInventario.AjusteStock);
            matriz.Permitir(RolesLocal.Empleada, PermisosCompras.ProveedoresGestionar);
            matriz.Permitir(RolesLocal.Empleada, PermisosCompras.RecepcionConfirmar);
            return await CrearConMatrizAsync(matriz);
        }

        public static async Task<EntornoCompras> CrearAsyncMatrizVacia()
        {
            return await CrearConMatrizAsync(MatrizPermisos.Vacia());
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
            _lectorId = id;
            return valida.IdSesion;
        }

        public ConfirmarRecepcion RecepcionReabierta()
        {
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(Path.Combine(_directorio, "compras-f03.db")));
            var autorizador = new Autorizador(_sesiones, new RepositorioUsuarios(_ids), new RepositorioPermisos(), fabrica, MatrizDePrueba());
            return new ConfirmarRecepcion(
                new RepositorioRecepciones(_ids),
                new RepositorioProveedores(_ids),
                new RepositorioVariantes(_ids),
                new RepositorioMovimientos(_ids),
                autorizador,
                fabrica,
                new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), _reloj),
                _reloj);
        }

        private static MatrizPermisos MatrizDePrueba()
        {
            var matriz = MatrizPermisos.ConValoresConfirmados();
            matriz.Permitir(RolesLocal.Empleada, PermisosCompras.RecepcionConfirmar);
            return matriz;
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

        public async Task<long> RecepcionesDeAsync(string operacionId)
        {
            await using var conexion = await AbrirCruda();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT COUNT(*) FROM recepcion WHERE operacion_id = $op;";
            comando.Parameters.AddWithValue("$op", operacionId);
            return Convert.ToInt64(await comando.ExecuteScalarAsync());
        }

        public async Task<string?> ComprobanteDeAsync(string recepcionId)
        {
            await using var conexion = await AbrirCruda();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT comprobante_ref FROM recepcion WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", recepcionId);
            return await comando.ExecuteScalarAsync() as string;
        }

        public async Task<string?> ProveedorDeAsync(string recepcionId)
        {
            await using var conexion = await AbrirCruda();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT proveedor_id FROM recepcion WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", recepcionId);
            return await comando.ExecuteScalarAsync() as string;
        }

        public async Task<string?> ResponsableDeAsync(string recepcionId)
        {
            await using var conexion = await AbrirCruda();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT creado_por_usuario_id FROM recepcion WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", recepcionId);
            return await comando.ExecuteScalarAsync() as string;
        }

        public async Task<long?> CostoDeAsync(string recepcionId, string varianteId)
        {
            await using var conexion = await AbrirCruda();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT costo_unitario_minor FROM recepcion_linea WHERE recepcion_id = $r AND variante_id = $v;";
            comando.Parameters.AddWithValue("$r", recepcionId);
            comando.Parameters.AddWithValue("$v", varianteId);
            var valor = await comando.ExecuteScalarAsync();
            return valor is null || valor is DBNull ? null : Convert.ToInt64(valor);
        }

        public async Task<IReadOnlyList<EventoAuditado>> EventosDeAsync(string operacionId)
        {
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), _reloj);
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            return await auditoria.ObtenerPorOperacionAsync(sesion, operacionId);
        }

        private static async Task<EntornoCompras> CrearConMatrizAsync(MatrizPermisos matriz)
        {
            var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(directorio, "compras-f03.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var entorno = new EntornoCompras(directorio, new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta)), matriz);
            await entorno.SesionValidaAsync();
            return entorno;
        }

        private async Task<SqliteConnection> AbrirCruda()
        {
            var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_directorio, "compras-f03.db"),
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
