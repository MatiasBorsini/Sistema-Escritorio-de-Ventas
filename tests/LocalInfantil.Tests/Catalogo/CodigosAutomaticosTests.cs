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

    [Fact]
    public async Task Matriz_12_combinaciones_es_atomica_secuencial_y_no_cambia_stock()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera matriz", null, "cat-rem"), e.Sesion);
        var combinaciones = (from talle in new[] { "4", "6", "8", "10" } from color in new[] { "Azul", "Rojo", "Negro" } select new CombinacionVariante(talle, color)).ToArray();
        var preview = await e.Servicios.ConsultasMatrizVariantes.PrevisualizarAsync(prod, combinaciones, e.Sesion);
        Assert.Equal(12, preview.Count);
        Assert.Equal("REM-001-01", preview[0].CodigoInterno);
        Assert.All(preview, p => Assert.False(p.YaExiste));

        var resultado = await e.Servicios.CasoCrearVariantesBatch.EjecutarAsync(prod, combinaciones, e.Sesion);
        Assert.Equal(12, resultado.CantidadCreadas);
        var variantes = await e.Servicios.ConsultasCatalogo.ListarPorProductoAsync(prod, e.Sesion);
        Assert.Equal(Enumerable.Range(1, 12).Select(i => $"REM-001-{i:D2}"), variantes.Select(v => v.CodigoInterno));
        foreach (var variante in variantes)
            Assert.Equal(0, (await e.Servicios.ConsultasInventario.ObtenerStockVendibleAsync(variante.Id, e.Sesion))!.Disponible);
        Assert.Equal(0, (await e.Servicios.CasoCrearVariantesBatch.EjecutarAsync(prod, combinaciones, e.Sesion)).CantidadCreadas);
    }

    [Fact]
    public async Task Matriz_admite_solo_talle_solo_color_y_sin_atributos()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Accesorio matriz", null, "cat-acc"), e.Sesion);
        var combinaciones = new[] { new CombinacionVariante("Único", null), new CombinacionVariante(null, "Dorado"), new CombinacionVariante(null, null) };
        var creado = await e.Servicios.CasoCrearVariantesBatch.EjecutarAsync(prod, combinaciones, e.Sesion);
        Assert.Equal(3, creado.CantidadCreadas);
        var variantes = await e.Servicios.ConsultasCatalogo.ListarPorProductoAsync(prod, e.Sesion);
        Assert.Contains(variantes, v => v.Talle == "Único" && v.Color is null);
        Assert.Contains(variantes, v => v.Talle is null && v.Color == "Dorado");
        Assert.Contains(variantes, v => v.Talle is null && v.Color is null);
    }

    [Fact]
    public async Task Matriz_omite_duplicados_normalizados_y_es_idempotente()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), e.Sesion);
        await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "4", "Azul"), e.Sesion);
        var seleccion = new[] { new CombinacionVariante(" 4 ", " azul "), new CombinacionVariante("6", "Rojo") };
        var preview = await e.Servicios.ConsultasMatrizVariantes.PrevisualizarAsync(prod, seleccion, e.Sesion);
        Assert.True(preview[0].YaExiste);
        Assert.False(preview[1].YaExiste);
        Assert.Equal("REM-001-02", preview[1].CodigoInterno);

        Assert.Equal(1, (await e.Servicios.CasoCrearVariantesBatch.EjecutarAsync(prod, seleccion, e.Sesion)).CantidadCreadas);
        Assert.Equal(0, (await e.Servicios.CasoCrearVariantesBatch.EjecutarAsync(prod, seleccion, e.Sesion)).CantidadCreadas);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoCrearVariantesBatch.EjecutarAsync(prod,
            [new("M", "Rojo"), new(" m ", " rojo ")], e.Sesion));
    }

    [Fact]
    public async Task Fallo_en_el_medio_del_lote_revierte_variantes_auditoria_y_secuencia()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), e.Sesion);
        await using (var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = e.Ruta, Pooling = false }.ToString()))
        {
            await conexion.OpenAsync();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "CREATE TRIGGER test_f11_falla BEFORE INSERT ON variante WHEN NEW.talle = 'FALLA' BEGIN SELECT RAISE(ABORT, 'fallo de prueba'); END;";
            await comando.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAnyAsync<Exception>(() => e.Servicios.CasoCrearVariantesBatch.EjecutarAsync(prod,
            [new("4", "Azul"), new("FALLA", "Rojo")], e.Sesion));
        Assert.Empty(await e.Servicios.ConsultasCatalogo.ListarPorProductoAsync(prod, e.Sesion));
        await using (var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = e.Ruta, Pooling = false }.ToString()))
        {
            await conexion.OpenAsync();
            using var comando = conexion.CreateCommand(); comando.CommandText = "SELECT COUNT(*) FROM auditoria WHERE tipo_evento = 'catalogo.variante_alta';";
            Assert.Equal(0L, Convert.ToInt64(await comando.ExecuteScalarAsync()));
        }
        await using (var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = e.Ruta, Pooling = false }.ToString()))
        {
            await conexion.OpenAsync(); using var comando = conexion.CreateCommand(); comando.CommandText = "DROP TRIGGER test_f11_falla;"; await comando.ExecuteNonQueryAsync();
        }
        var siguiente = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "4", "Azul"), e.Sesion);
        Assert.Equal("REM-001-01", (await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(siguiente, e.Sesion))!.CodigoInterno);
    }

    [Fact]
    public async Task Lotes_concurrentes_no_duplican_combinaciones_ni_secuencia()
    {
        using var e = await Entorno.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), e.Sesion);
        var combinaciones = new[] { new CombinacionVariante("4", "Azul"), new CombinacionVariante("6", "Rojo") };
        var resultados = await Task.WhenAll(
            e.Servicios.CasoCrearVariantesBatch.EjecutarAsync(prod, combinaciones, e.Sesion),
            e.Servicios.CasoCrearVariantesBatch.EjecutarAsync(prod, combinaciones, e.Sesion));
        Assert.Equal(2, resultados.Sum(r => r.CantidadCreadas));
        var codigos = (await e.Servicios.ConsultasCatalogo.ListarPorProductoAsync(prod, e.Sesion)).Select(v => v.CodigoInterno).ToArray();
        Assert.Equal(new[] { "REM-001-01", "REM-001-02" }, codigos);
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
