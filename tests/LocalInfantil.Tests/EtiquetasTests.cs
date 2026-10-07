using System.Windows.Documents;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Printing;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Wpf.Printing;
using ZXing;
using ZXing.Common;

namespace LocalInfantil.Tests;

public sealed class EtiquetasTests
{
    [Theory]
    [InlineData("REM-001-03")]
    [InlineData("ACC-001-01")]
    public void Code128_conserva_contenido_y_quiet_zone(string codigo)
    {
        var matrix = new GeneradorCodigoBarrasCode128().GenerarCodigoBarras(codigo);
        Assert.True(matrix.Ancho > 30);
        Assert.Equal(1, matrix.Alto);
        Assert.All(matrix.Modulos, bit => Assert.InRange(bit, (byte)0, (byte)1));
        Assert.All(Enumerable.Range(0, 10), x => Assert.False(matrix.EstaNegro(x, 0)));
        Assert.All(Enumerable.Range(matrix.Ancho - 10, 10), x => Assert.False(matrix.EstaNegro(x, 0)));

        const int escala = 4, alto = 16;
        var pixeles = new byte[matrix.Ancho * escala * alto];
        Array.Fill(pixeles, (byte)255);
        for (var x = 0; x < matrix.Ancho; x++)
            if (matrix.EstaNegro(x, 0))
                for (var dx = 0; dx < escala; dx++)
                    for (var y = 2; y < alto - 2; y++) pixeles[y * matrix.Ancho * escala + x * escala + dx] = 0;
        var luminancia = new RGBLuminanceSource(pixeles, matrix.Ancho * escala, alto, RGBLuminanceSource.BitmapFormat.Gray8);
        var decodificado = new MultiFormatReader().decode(new BinaryBitmap(new HybridBinarizer(luminancia)));
        Assert.Equal(codigo, decodificado.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("REM 001 03")]
    [InlineData("REM-001-03$")]
    public void Codigo_vacio_o_con_caracteres_no_admitidos_se_rechaza(string codigo)
        => Assert.ThrowsAny<ArgumentException>(() => new GeneradorCodigoBarrasCode128().GenerarCodigoBarras(codigo));

    [Fact]
    public void Distribucion_calcula_columnas_filas_y_paginas_sin_cortar_etiquetas()
    {
        var opciones = new EtiquetasOpciones(50, 30, 4, 4, 2, 2, true);
        var una = DistribucionEtiquetas.Calcular(27, 210, 297, opciones);
        Assert.Equal((3, 9, 27, 1), (una.Columnas, una.FilasPorPagina, una.EtiquetasPorPagina, una.Paginas));
        var varias = DistribucionEtiquetas.Calcular(60, 210, 297, opciones);
        Assert.Equal(3, varias.Paginas);
        Assert.Equal(60, varias.TotalEtiquetas);
        Assert.Throws<InvalidOperationException>(() => DistribucionEtiquetas.Calcular(1, 210, 297, new EtiquetasOpciones(220, 400, 0, 0, 0, 0, false)));
    }

    [Fact]
    public void Renderer_genera_documento_multipagina_con_codigo_y_opcion_de_precio()
    {
        var datos = new VarianteParaEtiqueta("id-interno-test", "REM-001-03", "Remera Dino larga", "8", "Azul", 4, 1850000, "ARS");
        var selecciones = new[] { new SeleccionEtiqueta(datos, 31) };
        FixedDocument? documento = null;
        Exception? fallo = null;
        var hilo = new Thread(() =>
        {
            try { documento = new DocumentoEtiquetasRenderer(new GeneradorCodigoBarrasCode128()).Renderizar(selecciones, new EtiquetasOpciones(50, 30, 4, 4, 2, 2, true)); }
            catch (Exception ex) { fallo = ex; }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start(); hilo.Join();
        Assert.Null(fallo);
        Assert.NotNull(documento);
        Assert.Equal(2, documento!.Pages.Count);
    }

    [Fact]
    public async Task Consulta_etiquetas_es_una_lectura_con_precio_y_stock_disponible_agregado()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "etiquetas.db");
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            foreach (var permiso in new[] { PermisosCatalogo.ProductosGestionar, PermisosCatalogo.VariantesGestionar, PermisosCatalogo.PreciosGestionar, PermisosInventario.CargaInicialEstablecer, PermisosInventario.NoVendibleGestionar })
                await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, permiso, true, sesion.IdSesion);
            var producto = await servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera Dino", null, "cat-rem"), sesion.IdSesion);
            var variante = await servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(producto, "8", "Azul"), sesion.IdSesion);
            await servicios.CasoFijarPrecio.EjecutarAsync(variante, 1850000, sesion.IdSesion);
            await servicios.CasoCargarExistenciaInicial.EjecutarAsync([new FilaCargaInicial(variante, 5)], Guid.NewGuid().ToString("N"), sesion.IdSesion);
            await servicios.CasoApartarStock.EjecutarAsync(variante, 2, "muestra dañada", Guid.NewGuid().ToString("N"), sesion.IdSesion);

            var fila = Assert.Single(await servicios.ConsultasEtiquetas.ListarPorProductoAsync(producto, sesion.IdSesion));
            Assert.Equal("REM-001-01", fila.CodigoInterno);
            Assert.Equal("Remera Dino", fila.NombreProducto);
            Assert.Equal(3, fila.Disponible);
            Assert.Equal(1850000, fila.PrecioMinor);
            Assert.Equal(fila, Assert.Single(await servicios.ConsultasEtiquetas.ListarPorIdsAsync([variante], sesion.IdSesion)));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
