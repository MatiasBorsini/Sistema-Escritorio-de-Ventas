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

namespace LocalInfantil.Tests.Catalogo;

/// <summary>
/// Prueba integral F02-T01+T02 sobre base temporal aislada, eliminada al
/// terminar. Cubre los 10 pasos del bloque con datos ficticios, sin tocar la
/// base provisional ni usar datos reales. Los permisos de catálogo se conceden
/// solo en matrices de prueba: la matriz productiva los deniega (probado).
/// </summary>
public sealed class CatalogoTests
{
    [Fact]
    public async Task Crear_remera_con_variantes_distintas()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var remeraId = await entorno.Casos.CrearProducto.EjecutarAsync(
            new NuevoProducto("Remera", "Marca Ficticia", "cat-rem"), entorno.Sesion);

        var v1 = await entorno.Casos.CrearVariante.EjecutarAsync(new NuevaVariante(remeraId, "6", "Rojo"), entorno.Sesion);
        var v2 = await entorno.Casos.CrearVariante.EjecutarAsync(new NuevaVariante(remeraId, "8", "Azul"), entorno.Sesion);
        var v3 = await entorno.Casos.CrearVariante.EjecutarAsync(new NuevaVariante(remeraId, "10", "Verde"), entorno.Sesion);

        Assert.NotEqual(v1, v2);
        Assert.NotEqual(v2, v3);
        Assert.Equal(3, (await entorno.Casos.Consultas.ListarPorProductoAsync(remeraId, entorno.Sesion)).Count);
    }

    [Fact]
    public async Task Articulos_sin_talle_sin_color_y_sin_ambos()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var productoId = await entorno.Casos.CrearProducto.EjecutarAsync(new NuevoProducto("Gorro", null, "cat-acc"), entorno.Sesion);

        var sinTalle = await entorno.Casos.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, null, "Rojo"), entorno.Sesion);
        var sinColor = await entorno.Casos.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "4", null), entorno.Sesion);
        var sinAmbos = await entorno.Casos.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, null, null), entorno.Sesion);

        var leida1 = await entorno.Casos.Consultas.ObtenerVarianteAsync(sinTalle, entorno.Sesion);
        var leida2 = await entorno.Casos.Consultas.ObtenerVarianteAsync(sinColor, entorno.Sesion);
        var leida3 = await entorno.Casos.Consultas.ObtenerVarianteAsync(sinAmbos, entorno.Sesion);
        Assert.NotNull(leida1);
        Assert.Null(leida1.Talle);
        Assert.Equal("Rojo", leida1.Color);
        Assert.NotNull(leida2);
        Assert.Equal("4", leida2.Talle);
        Assert.Null(leida2.Color);
        Assert.NotNull(leida3);
        Assert.Null(leida3.Talle);
        Assert.Null(leida3.Color);
    }

    [Fact]
    public async Task Consultar_y_editar_conserva_ids()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var productoId = await entorno.Casos.CrearProducto.EjecutarAsync(new NuevoProducto("Pantalón", null, "cat-pan"), entorno.Sesion);
        var varianteId = await entorno.Casos.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "4", null), entorno.Sesion);

        await entorno.Casos.EditarProducto.EjecutarAsync(productoId, new EdicionProducto("Pantalón Largo", "Marca", "cat-rem"), entorno.Sesion);
        await entorno.Casos.EditarVariante.EjecutarAsync(varianteId, new EdicionVariante("6", "Azul"), entorno.Sesion);

        var producto = await entorno.Casos.Consultas.ObtenerProductoAsync(productoId, entorno.Sesion);
        var variante = await entorno.Casos.Consultas.ObtenerVarianteAsync(varianteId, entorno.Sesion);
        Assert.NotNull(producto);
        Assert.Equal(productoId, producto.Id);
        Assert.Equal("Pantalón Largo", producto.Nombre);
        Assert.NotNull(variante);
        Assert.Equal(varianteId, variante.Id);
        Assert.Equal("6", variante.Talle);
        Assert.Equal("Azul", variante.Color);
        Assert.NotNull(producto);
        Assert.Equal($"{producto.CodigoInterno}-01", variante.CodigoInterno);
        Assert.Equal(productoId, variante.ProductoId);
    }

    [Fact]
    public async Task Buscar_por_codigo_interno_exacto()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var productoId = await entorno.Casos.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.Sesion);
        var varianteId = await entorno.Casos.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "6", null), entorno.Sesion);
        var creada = await entorno.Casos.Consultas.ObtenerVarianteAsync(varianteId, entorno.Sesion);
        Assert.NotNull(creada);

        // Código exacto válido resuelve sin obligar a elegir sugerencia.
        var hallada = await entorno.Casos.Consultas.BuscarPorCodigoInternoAsync(creada.CodigoInterno, entorno.Sesion);
        Assert.NotNull(hallada);
        Assert.Equal(creada.CodigoInterno, hallada.CodigoInterno);
        Assert.Null(await entorno.Casos.Consultas.BuscarPorCodigoInternoAsync("REM-999-99", entorno.Sesion));
    }

    [Fact]
    public async Task Codigos_internos_automaticos_unicos_y_secuenciales()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var productoId = await entorno.Casos.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.Sesion);
        var producto = await entorno.Casos.Consultas.ObtenerProductoAsync(productoId, entorno.Sesion);
        Assert.NotNull(producto);

        var v1 = await entorno.Casos.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "6", null), entorno.Sesion);
        var v2 = await entorno.Casos.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "8", null), entorno.Sesion);

        var l1 = await entorno.Casos.Consultas.ObtenerVarianteAsync(v1, entorno.Sesion);
        var l2 = await entorno.Casos.Consultas.ObtenerVarianteAsync(v2, entorno.Sesion);
        Assert.NotNull(l1);
        Assert.NotNull(l2);
        Assert.NotEqual(l1.CodigoInterno, l2.CodigoInterno);
        Assert.Equal($"{producto.CodigoInterno}-01", l1.CodigoInterno);
        Assert.Equal($"{producto.CodigoInterno}-02", l2.CodigoInterno);
        Assert.Equal(2, (await entorno.Casos.Consultas.ListarPorProductoAsync(productoId, entorno.Sesion)).Count);
    }

    [Fact]
    public async Task Variante_de_producto_inexistente_se_rechaza()
    {
        using var entorno = await EntornoPrueba.CrearAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            entorno.Casos.CrearVariante.EjecutarAsync(new NuevaVariante("producto-inexistente", null, null), entorno.Sesion));
    }

    [Fact]
    public async Task Desactivar_conserva_historial()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var productoId = await entorno.Casos.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.Sesion);
        var varianteId = await entorno.Casos.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "6", null), entorno.Sesion);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            entorno.Casos.DesactivarProducto.EjecutarAsync(productoId, entorno.Sesion));

        await entorno.Casos.DesactivarVariante.EjecutarAsync(varianteId, entorno.Sesion);
        await entorno.Casos.DesactivarProducto.EjecutarAsync(productoId, entorno.Sesion);

        var producto = await entorno.Casos.Consultas.ObtenerProductoAsync(productoId, entorno.Sesion);
        var variante = await entorno.Casos.Consultas.ObtenerVarianteAsync(varianteId, entorno.Sesion);
        Assert.NotNull(producto);
        Assert.False(producto.Activo);
        Assert.NotNull(variante);
        Assert.False(variante.Activa);
        Assert.Empty(await entorno.Casos.Consultas.ListarProductosAsync(entorno.Sesion, soloActivos: true));
        Assert.NotEmpty(await entorno.AuditoriaDeAsync(productoId));
    }

    [Fact]
    public async Task Rollback_compuesto_con_auditoria()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var fabrica = entorno.Fabrica;
        var ids = new GeneradorIds();
        var productos = new RepositorioProductos(ids);
        var variantes = new RepositorioVariantes(ids);
        var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), new RelojFijo(MomentoFijo));

        string productoId;
        await using (var sesion = await fabrica.IniciarAsync())
        {
            productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Campera", null, null), Fecha);
            var varianteId = await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "6", null), Fecha);
            await auditoria.RegistrarAsync(sesion, Evento("op-compuesta", "catalogo.producto_alta", "producto", productoId));
            // El fallo compuesto se provoca con una FK inexistente y todo revierte.
            await Assert.ThrowsAsync<SqliteException>(() =>
                variantes.InsertarAsync(sesion, new NuevaVariante("producto-inexistente", null, null), Fecha));
            _ = varianteId;
        }

        await using var verificacion = await fabrica.IniciarLecturaAsync();
        Assert.Null(await productos.ObtenerPorIdAsync(verificacion, productoId));
        Assert.Empty(await auditoria.ObtenerPorOperacionAsync(verificacion, "op-compuesta"));
    }

    [Fact]
    public async Task Escrituras_denegadas_sin_sesion_o_permiso()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var productoId = await entorno.Casos.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.Sesion);

        await Assert.ThrowsAsync<SesionInvalidaException>(() =>
            entorno.Casos.CrearProducto.EjecutarAsync(new NuevoProducto("Otra", null, null), null));
        await Assert.ThrowsAsync<SesionInvalidaException>(() =>
            entorno.Casos.Consultas.ObtenerProductoAsync(productoId, null));

        using var denegado = await EntornoPrueba.CrearAsyncMatrizVacia();
        var sesionSinPermiso = await denegado.SesionValidaAsync();
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            denegado.Casos.CrearProducto.EjecutarAsync(new NuevoProducto("Otra", null, null), sesionSinPermiso));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            denegado.Casos.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, null, null), sesionSinPermiso));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            denegado.Casos.EditarProducto.EjecutarAsync(productoId, new EdicionProducto("X", null, null), sesionSinPermiso));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            denegado.Casos.EditarVariante.EjecutarAsync(productoId, new EdicionVariante(null, null), sesionSinPermiso));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            denegado.Casos.DesactivarProducto.EjecutarAsync(productoId, sesionSinPermiso));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            denegado.Casos.DesactivarVariante.EjecutarAsync(productoId, sesionSinPermiso));
    }

    [Fact]
    public async Task Matriz_productiva_deniega_catalogo_a_todos_los_roles()
    {
        using var entorno = await EntornoPrueba.CrearAsync();
        var matriz = MatrizPermisos.ConValoresConfirmados();

        Assert.False(matriz.Permite(RolesLocal.Administrador, PermisosCatalogo.ProductosGestionar));
        Assert.False(matriz.Permite(RolesLocal.Administrador, PermisosCatalogo.VariantesGestionar));
        Assert.False(matriz.Permite(RolesLocal.Duena, PermisosCatalogo.ProductosGestionar));
        Assert.False(matriz.Permite(RolesLocal.Duena, PermisosCatalogo.VariantesGestionar));
        Assert.False(matriz.Permite(RolesLocal.Empleada, PermisosCatalogo.ProductosGestionar));
        Assert.False(matriz.Permite(RolesLocal.Empleada, PermisosCatalogo.VariantesGestionar));
        _ = entorno;
    }

    private static readonly DateTimeOffset MomentoFijo = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private const string Fecha = "2026-09-21T12:00:00Z";

    private static NuevoEventoAuditoria Evento(string operacion, string tipo, string refTipo, string refId)
    {
        return new NuevoEventoAuditoria(operacion, tipo, ActorAuditoria.Sistema, null, refTipo, refId, null);
    }

    private sealed class JuegoCasos
    {
        public required CrearProducto CrearProducto { get; init; }
        public required EditarProducto EditarProducto { get; init; }
        public required DesactivarProducto DesactivarProducto { get; init; }
        public required CrearVariante CrearVariante { get; init; }
        public required EditarVariante EditarVariante { get; init; }
        public required DesactivarVariante DesactivarVariante { get; init; }
        public required ConsultasCatalogo Consultas { get; init; }
    }

    private sealed class EntornoPrueba : IDisposable
    {
        private readonly string _directorio;
        private readonly FabricaConexionesSqlite _fabrica;
        private readonly RepositorioProductos _productos;
        private readonly RepositorioVariantes _variantes;
        private readonly RepositorioAuditoria _auditoria;
        private readonly GeneradorIds _ids;
        private readonly RelojFijo _reloj;
        private readonly GestorSesiones _sesiones;

        public readonly JuegoCasos Casos;
        public FabricaConexionesSqlite Fabrica => _fabrica;

        private EntornoPrueba(string directorio, FabricaConexionesSqlite fabrica, MatrizPermisos matriz)
        {
            _directorio = directorio;
            _fabrica = fabrica;
            _ids = new GeneradorIds();
            var reloj = new RelojFijo(MomentoFijo);
            _reloj = reloj;
            _productos = new RepositorioProductos(_ids);
            _variantes = new RepositorioVariantes(_ids);
            _auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
            _sesiones = new GestorSesiones();
            var autorizador = new Autorizador(_sesiones, new RepositorioUsuarios(_ids), new RepositorioPermisos(), _fabrica, matriz);
            Casos = new JuegoCasos
            {
                CrearProducto = new CrearProducto(_productos, new RepositorioCategorias(_ids), autorizador, _fabrica, _auditoria, _ids, reloj),
                EditarProducto = new EditarProducto(_productos, new RepositorioCategorias(_ids), autorizador, _fabrica, _auditoria, _ids),
                DesactivarProducto = new DesactivarProducto(_productos, _variantes, autorizador, _fabrica, _auditoria, _ids),
                CrearVariante = new CrearVariante(_variantes, _productos, autorizador, _fabrica, _auditoria, _ids, reloj),
                EditarVariante = new EditarVariante(_variantes, autorizador, _fabrica, _auditoria, _ids),
                DesactivarVariante = new DesactivarVariante(_variantes, autorizador, _fabrica, _auditoria, _ids),
                Consultas = new ConsultasCatalogo(_productos, _variantes, _sesiones, _fabrica),
            };
        }

        public static async Task<EntornoPrueba> CrearAsync()
        {
            var matriz = MatrizPermisos.ConValoresConfirmados();
            matriz.Permitir(RolesLocal.Empleada, PermisosCatalogo.ProductosGestionar);
            matriz.Permitir(RolesLocal.Empleada, PermisosCatalogo.VariantesGestionar);
            return await CrearConMatrizAsync(matriz);
        }

        public static async Task<EntornoPrueba> CrearAsyncMatrizVacia()
        {
            return await CrearConMatrizAsync(MatrizPermisos.Vacia());
        }

        public string Sesion => _sesionId ?? throw new InvalidOperationException("Sin sesión registrada.");

        private string? _sesionId;

        public async Task<string> SesionValidaAsync()
        {
            if (_sesionId is not null)
            {
                return _sesionId;
            }

            await using var sesion = await _fabrica.IniciarAsync();
            var usuarios = new RepositorioUsuarios(_ids);
            var hash = new Pbkdf2HashContrasenas();
            await usuarios.CrearAsync(sesion, new NuevoUsuario("lector", RolesLocal.Empleada), hash.CrearHuella("clave-lectora-00"), Fecha);
            await sesion.ConfirmarAsync();
            var valida = new SesionUsuario("sesion-lectora", await IdDeAsync("lector"), "lector", RolesLocal.Empleada, Fecha);
            _sesiones.Registrar(valida);
            _sesionId = valida.IdSesion;
            return valida.IdSesion;
        }

        public async Task<string> IdDeAsync(string nombre)
        {
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            var usuarios = new RepositorioUsuarios(_ids);
            var usuario = await usuarios.ObtenerPorNombreAsync(sesion, nombre);
            return usuario is null ? throw new InvalidOperationException("El usuario ficticio no existe.") : usuario.Id;
        }

        public async Task<IReadOnlyList<EventoAuditado>> AuditoriaDeAsync(string referenciaId)
        {
            await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_directorio, "catalogo-f01.db"),
                Pooling = false,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            await conexion.OpenAsync();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT id, operacion_id, tipo_evento, actor_tipo, actor_id, referencia_tipo, referencia_id, detalle, creado_en FROM auditoria WHERE referencia_id = $ref ORDER BY creado_en, id;";
            comando.Parameters.AddWithValue("$ref", referenciaId);
            await using var lector = await comando.ExecuteReaderAsync();
            var resultado = new List<EventoAuditado>();
            while (await lector.ReadAsync())
            {
                resultado.Add(new EventoAuditado(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5),
                    lector.IsDBNull(6) ? null : lector.GetString(6),
                    lector.IsDBNull(7) ? null : lector.GetString(7),
                    lector.GetString(8)));
            }

            return resultado;
        }

        private static async Task<EntornoPrueba> CrearConMatrizAsync(MatrizPermisos matriz)
        {
            var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(directorio, "catalogo-f01.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var entorno = new EntornoPrueba(directorio, new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta)), matriz);
            await entorno.SesionValidaAsync();
            return entorno;
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
