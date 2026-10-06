using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Reportes;
using LocalInfantil.Application.Features.Ventas;

namespace LocalInfantil.Tests.Reportes;

/// <summary>
/// F09-T03: stock vivo, ranking neto por evento y sin-ventas. Sin snapshots,
/// sin temporada inventada, sin stock mínimo comercial.
/// </summary>
public sealed class ReportesInventarioTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static (string Desde, string Hasta) Rango(DateOnly desde, DateOnly hasta)
    {
        var r = RangoFechas.PorDias(desde, hasta, Utc);
        return (r.DesdeUtc, r.HastaUtc);
    }

    [Fact]
    public async Task Stock_actual_muestra_disponible_apartado_fisico()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        await e.Apartar.EjecutarAsync(v1, 3, "Vidriera", "op-apart-stock", e.Sesion);

        var filas = await e.Reportes.StockActualAsync(string.Empty, null, false, null, 100, 0, e.Sesion);
        Assert.Equal(2, filas.Count);
        var f1 = filas.First(f => f.VarianteId == v1);
        Assert.Equal(7, f1.Disponible);
        Assert.Equal(3, f1.Apartado);
        Assert.Equal(10, f1.Fisico);
        Assert.Equal("Remera F09", f1.ProductoNombre);
        var f2 = filas.First(f => f.VarianteId == v2);
        Assert.Equal(7, f2.Disponible);

        // Filtros: texto, cero y hasta N.
        Assert.Empty(await e.Reportes.StockActualAsync("Buzo", null, false, null, 100, 0, e.Sesion));
        Assert.Empty(await e.Reportes.StockActualAsync(string.Empty, null, true, null, 100, 0, e.Sesion));
        Assert.Equal(2, (await e.Reportes.StockActualAsync(string.Empty, null, false, 10, 100, 0, e.Sesion)).Count);
        Assert.Equal(0, (await e.Reportes.StockActualAsync(string.Empty, null, false, 5, 100, 0, e.Sesion)).Count);
    }

    [Fact]
    public async Task Venta_y_devoluciones_mueven_stock_vivo()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 2, 10000)], [new(MediosPago.Efectivo, 20000, 20000)], "op-stock-v", e.Sesion));
        var trasVenta = (await e.Reportes.StockActualAsync(string.Empty, null, false, null, 100, 0, e.Sesion))
            .First(f => f.VarianteId == v1);
        Assert.Equal(8, trasVenta.Disponible);

        var ficha = (await e.Ventas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        // Devolución apta: vuelve a Disponible.
        await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok.VentaId, [new(ficha.Lineas[0].Id, 1, true, null)], [], [],
            MediosPago.Efectivo, "Apta", "op-stock-dev", e.Sesion), default);
        var trasDev = (await e.Reportes.StockActualAsync(string.Empty, null, false, null, 100, 0, e.Sesion))
            .First(f => f.VarianteId == v1);
        Assert.Equal(9, trasDev.Disponible);
        Assert.Equal(0, trasDev.Apartado);
    }

    [Fact]
    public async Task Devolucion_no_apta_sube_fisico_y_apartado_sin_disponible()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-stock-na", e.Sesion));
        var ficha = (await e.Ventas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok.VentaId, [new(ficha.Lineas[0].Id, 1, false, "Rota")], [], [],
            MediosPago.Efectivo, "No apta", "op-stock-devna", e.Sesion), default);

        var fila = (await e.Reportes.StockActualAsync(string.Empty, null, false, null, 100, 0, e.Sesion))
            .First(f => f.VarianteId == v1);
        Assert.Equal(9, fila.Disponible);
        Assert.Equal(1, fila.Apartado);
        Assert.Equal(10, fila.Fisico);
    }

    [Fact]
    public async Task Ranking_bruto_neto_y_devolucion_posterior()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 3, 10000), new(v2, 1, 12000)], [new(MediosPago.Efectivo, 42000, 42000)], "op-rank-v", e.Sesion));
        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var ranking = await e.Reportes.RankingVentasAsync(d, h, 100, 0, e.Sesion);
        Assert.Equal(2, ranking.Count);
        var r1 = ranking.First(r => r.VarianteId == v1);
        Assert.Equal(3, r1.BrutasUnidades);
        Assert.Equal(0, r1.DevueltasUnidades);
        Assert.Equal(3, r1.NetasUnidades);
        Assert.Equal(v1, ranking[0].VarianteId);

        // Devolución posterior resta en su período.
        e.FijarHora(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        var ficha = (await e.Ventas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok.VentaId, [new(ficha.Lineas[0].Id, 2, true, null)], [], [],
            MediosPago.Efectivo, "Devuelve 2", "op-rank-dev", e.Sesion), default);

        var (oct, octH) = Rango(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var rankingOct = await e.Reportes.RankingVentasAsync(oct, octH, 100, 0, e.Sesion);
        var r1oct = Assert.Single(rankingOct);
        Assert.Equal(0, r1oct.BrutasUnidades);
        Assert.Equal(2, r1oct.DevueltasUnidades);
        Assert.Equal(-2, r1oct.NetasUnidades);

        // Septiembre no se reescribe.
        var rankingSep = await e.Reportes.RankingVentasAsync(d, h, 100, 0, e.Sesion);
        Assert.Equal(3, rankingSep.First(r => r.VarianteId == v1).NetasUnidades);

        // Agrupación por producto.
        var porProd = await e.Reportes.RankingPorProductoAsync(d, h, 100, 0, e.Sesion);
        var prod = Assert.Single(porProd);
        Assert.Equal(4, prod.BrutasUnidades);
        Assert.Equal(4, prod.NetasUnidades);
    }

    [Fact]
    public async Task Sin_ventas_separa_nunca_vendidos_y_muestra_ultimas_fechas()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov SV"), e.Sesion);
        await e.Recepcion.EjecutarAsync(prov, [new(v2, 2, null, null)], "op-sv-rec", null, e.Sesion);
        await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-sv-v", e.Sesion);

        // Corte: 40 días después. v1 vendida hace 40 días, v2 nunca.
        var corte = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero).ToString("o");
        var filas = await e.Reportes.SinVentasAsync(corte, 100, 0, e.Sesion);
        Assert.Equal(2, filas.Count);
        var nunca = filas.First(f => f.VarianteId == v2);
        Assert.True(nunca.NuncaVendido);
        Assert.Null(nunca.UltimaVentaEn);
        Assert.NotNull(nunca.UltimaRecepcionEn);
        var vieja = filas.First(f => f.VarianteId == v1);
        Assert.False(vieja.NuncaVendido);
        Assert.NotNull(vieja.UltimaVentaEn);

        // Corte anterior a la venta: solo la nunca vendida califica.
        var cortePrevio = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero).ToString("o");
        var soloNunca = await e.Reportes.SinVentasAsync(cortePrevio, 100, 0, e.Sesion);
        var unica = Assert.Single(soloNunca);
        Assert.Equal(v2, unica.VarianteId);
        Assert.True(unica.NuncaVendido);
    }
}
