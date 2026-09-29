using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Catalogo;

/// <summary>
/// Códigos internos automáticos (autoridad para altas nuevas), categorías
/// controladas obligatorias y regla sin duplicar Talle+Color:
/// formato jerárquico {PREFIJO}-{NNN} / {CODIGO_PRODUCTO}-{NN}
/// (ej REM-001 / REM-001-01), secuencias por categoría y por producto,
/// typeahead por código o nombre, y resolución exacta sin sugerencia.
/// </summary>
public sealed class CodigosAutomaticosTests
{
    [Fact]
    public async Task Categorias_seed_definitivas_con_prefijos()
    {
        using var e = await Entorno.CrearAsync();
        var cats = await e.Servicios.ConsultasCatalogo.ListarCategoriasAsync(e.Sesion, true);
        Assert.Equal(12, cats.Count);
        Assert.Equal(
            new[] { "REM", "BUZ", "ABR", "PAN", "SHO", "VES", "CON", "BOD", "PIJ", "INT", "ACC", "OTR" },
            cats.Select(c => c.Prefijo));
        Assert.Equal(
            new[] { "Remeras y tops", "Buzos y sweaters", "Camperas y abrigos", "Pantalones y calzas", "Shorts y bermudas", "Vestidos y polleras", "Conjuntos", "Bodies y enteritos", "Pijamas", "Ropa interior y medias", "Accesorios", "Otros" },
            cats.Select(c => c.Nombre));
    }

    [Fact]
    public async Task Producto_sin_categoria_falla_con_mensaje_humano()
    {
        using var e = await Entorno.CrearAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), e.Sesion));
        Assert.Equal("Seleccioná una categoría.", error.Message);

        var errorVacia = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "   "), e.Sesion));
        Assert.Equal("Seleccioná una categoría.", errorVacia.Message);

        var errorInexistente = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-no-existe"), e.Sesion));
        Assert.Equal("Seleccioná una categoría.", errorInexistente.Message);
    }

    [Fact]
    public async Task Otros_funciona_cuando_se_selecciona_explicitamente()
    {
        using var e = await Entorno.CrearAsync();
        var id = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Varios", null, "cat-otr"), e.Sesion);
        var prod = await e.Servicios.ConsultasCatalogo.ObtenerProductoAsync(id, e.Sesion);
        Assert.NotNull(prod);
        Assert.Equal("OTR-001", prod.CodigoInterno);
        Assert.Equal("Otros", prod.Categoria);
    }

    [Fact]
    public async Task Sin_dependencia_del_campo_categoria_texto_heredado()
    {
        using var e = await Entorno.CrearAsync();
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = e.Ruta,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM pragma_table_info('producto') WHERE name = 'categoria';";
        Assert.Equal(0L, Convert.ToInt64(await comando.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Producto_genera_codigo_por_categoria_y_secuencia()
    {
        using var e = await Entorno.CrearAsync();
        var r1 = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera básica", null, "cat-rem"), e.Sesion);
        var r2 = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera segunda", null, "cat-rem"), e.Sesion);
        var p1 = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Pantalón uno", null, "cat-pan"), e.Sesion);

        Assert.Equal("REM-001", (await e.Servicios.ConsultasCatalogo.ObtenerProductoAsync(r1, e.Sesion))!.CodigoInterno);
        Assert.Equal("REM-002", (await e.Servicios.ConsultasCatalogo.ObtenerProductoAsync(r2, e.Sesion))!.CodigoInterno);
        Assert.Equal("PAN-001", (await e.Servicios.ConsultasCatalogo.ObtenerProductoAsync(p1, e.Sesion))!.CodigoInterno);
    }

    [Fact]
    public async Task Variante_genera_codigo_jerarquico_secuencial()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera básica", null, "cat-rem"), e.Sesion);
        var codProd = (await e.Servicios.ConsultasCatalogo.ObtenerProductoAsync(prod, e.Sesion))!.CodigoInterno;

        var a = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", "Negro"), e.Sesion);
        var b = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "S", "Blanco"), e.Sesion);

        Assert.Equal($"{codProd}-01", (await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(a, e.Sesion))!.CodigoInterno);
        Assert.Equal($"{codProd}-02", (await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(b, e.Sesion))!.CodigoInterno);
    }

    [Fact]
    public async Task Codigo_estable_ante_renombre_y_reclasificacion()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), e.Sesion);
        var vid = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", null), e.Sesion);
        var codProd = (await e.Servicios.ConsultasCatalogo.ObtenerProductoAsync(prod, e.Sesion))!.CodigoInterno;
        var codVar = (await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(vid, e.Sesion))!.CodigoInterno;

        await e.Servicios.CasoEditarProducto.EjecutarAsync(prod, new EdicionProducto("Remera Renombrada", "Marca", "cat-buz"), e.Sesion);
        await e.Servicios.CasoEditarVariante.EjecutarAsync(vid, new EdicionVariante("L", "Azul"), e.Sesion);

        Assert.Equal(codProd, (await e.Servicios.ConsultasCatalogo.ObtenerProductoAsync(prod, e.Sesion))!.CodigoInterno);
        Assert.Equal(codVar, (await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(vid, e.Sesion))!.CodigoInterno);
    }

    [Fact]
    public async Task Duplicado_exacttalle_color_se_rechaza_con_mensaje_humano()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), e.Sesion);
        await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", "Negro"), e.Sesion);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", "Negro"), e.Sesion));
        Assert.Equal("Ya existe una variante con ese talle y color.", error.Message);
        Assert.Single(await e.Servicios.ConsultasCatalogo.ListarPorProductoAsync(prod, e.Sesion));
    }

    [Fact]
    public async Task Duplicado_case_insensitive_y_con_espacios_se_rechaza()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), e.Sesion);
        await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", "Rojo"), e.Sesion);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, " m ", "rOJO"), e.Sesion));
        Assert.Equal("Ya existe una variante con ese talle y color.", error.Message);
    }

    [Fact]
    public async Task Null_y_vacio_son_el_mismo_sin_valor()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Gorro", null, "cat-acc"), e.Sesion);
        await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, null, null), e.Sesion);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "  ", ""), e.Sesion));
        Assert.Equal("Ya existe una variante con ese talle y color.", error.Message);
    }

    [Fact]
    public async Task Dos_productos_distintos_pueden_repetir_talle_color()
    {
        using var e = await Entorno.CrearAsync();
        var p1 = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera A", null, "cat-rem"), e.Sesion);
        var p2 = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera B", null, "cat-rem"), e.Sesion);
        var a = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(p1, "M", "Rojo"), e.Sesion);
        var b = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(p2, "M", "Rojo"), e.Sesion);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Editar_a_combinacion_existente_se_rechaza()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), e.Sesion);
        await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", "Rojo"), e.Sesion);
        var v2 = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "L", null), e.Sesion);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoEditarVariante.EjecutarAsync(v2, new EdicionVariante("m", "ROJO"), e.Sesion));
        Assert.Equal("Ya existe una variante con ese talle y color.", error.Message);
    }

    [Fact]
    public async Task Typeahead_encuentra_por_codigo_y_por_nombre()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera básica", "Nube", "cat-rem"), e.Sesion);
        var vid = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", "Negro"), e.Sesion);
        var codigo = (await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(vid, e.Sesion))!.CodigoInterno;

        var porCodigo = await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(codigo, 10, e.Sesion);
        Assert.Contains(porCodigo, s => s.CodigoInterno == codigo);

        var porNombre = await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("Remera básica", 10, e.Sesion);
        Assert.Contains(porNombre, s => s.CodigoInterno == codigo);

        // Ejemplo visual del contrato: código + producto + talle/color.
        var sug = porCodigo.First(s => s.CodigoInterno == codigo);
        Assert.Equal("Remera básica", sug.NombreProducto);
        Assert.Equal("Nube · Remeras y tops", sug.ContextoProducto);
        Assert.Equal("M · Negro", sug.DetalleTalleColor);
    }

    [Fact]
    public async Task Codigo_exacto_resuelve_sin_sugerencia()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera básica", null, "cat-rem"), e.Sesion);
        var vid = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", "Negro"), e.Sesion);
        var codigo = (await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(vid, e.Sesion))!.CodigoInterno;

        var hallada = await e.Servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(codigo, e.Sesion);
        Assert.NotNull(hallada);
        Assert.Equal(vid, hallada.Id);
    }

    private sealed class Entorno : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        public string Ruta { get; }
        private readonly string _dir;

        private Entorno(string dir, ServiciosComerciales servicios, string sesion)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
            Ruta = Path.Combine(dir, "codigos.db");
        }

        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "codigos.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.ProductosGestionar, true, sesion.IdSesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.VariantesGestionar, true, sesion.IdSesion);
            return new Entorno(dir, servicios, sesion.IdSesion);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
