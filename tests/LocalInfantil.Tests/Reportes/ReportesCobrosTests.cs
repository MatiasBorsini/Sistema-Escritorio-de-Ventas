using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Reportes;
using LocalInfantil.Application.Features.Ventas;

namespace LocalInfantil.Tests.Reportes;

/// <summary>
/// F09-T01/T02 (cobros): dinero recibido por fecha real, por medio vigente,
/// con vuelto excluido y reintegros separados. Nunca suma recibido.
/// </summary>
public sealed class ReportesCobrosTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static (string Desde, string Hasta) Rango(DateOnly desde, DateOnly hasta)
    {
        var r = RangoFechas.PorDias(desde, hasta, Utc);
        return (r.DesdeUtc, r.HastaUtc);
    }

    [Fact]
    public async Task Cada_medio_suma_su_importe()
    {
        foreach (var medio in new[] { MediosPago.Transferencia, MediosPago.Debito, MediosPago.Credito, MediosPago.Billetera })
        {
            using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
            await e.AbrirCajaAsync();
            var (_, v1, _) = await e.CatalogoConStockAsync();
            await e.ConfirmarVenta.EjecutarAsync(
                [new(v1, 1, 10000)], [new(medio, 10000, null)], $"op-cobro-{medio}", e.Sesion);
            var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
            var medios = await e.Reportes.DesgloseMediosAsync(d, h, e.Sesion);
            var fila = medios.First(m => m.Medio == medio);
            Assert.Equal(10000, fila.VentasMinor);
            Assert.Equal(10000, fila.NetoMinor);
            Assert.False(fila.ConCorreccion);
            Assert.All(medios.Where(m => m.Medio != medio), m => Assert.Equal(0, m.NetoMinor));
        }
    }

    [Fact]
    public async Task Combinado_suma_cada_medio_y_vuelto_no_existe()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 2, 10000)],
            [new(MediosPago.Efectivo, 15000, 20000), new(MediosPago.Debito, 5000, null)],
            "op-cobro-comb",
            e.Sesion);
        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var medios = await e.Reportes.DesgloseMediosAsync(d, h, e.Sesion);
        // Recibido 20000 jamás se suma: solo el imputado 15000.
        Assert.Equal(15000, medios.First(m => m.Medio == MediosPago.Efectivo).VentasMinor);
        Assert.Equal(5000, medios.First(m => m.Medio == MediosPago.Debito).VentasMinor);
        Assert.Equal(20000, (await e.Reportes.ResumenVentasAsync(d, h, e.Sesion)).CobradoMinor);
    }

    [Fact]
    public async Task Abono_posterior_suma_cobro_en_su_fecha_sin_venta()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var cliente = await e.ClienteHabilitadoAsync(100000);
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await e.ConfirmarVenta.EjecutarAsync([new(v1, 1, 10000)], [], "op-cobro-fiado", e.Sesion, cliente, null, true);

        e.FijarHora(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        var pagos = new[] { new NuevoPago(MediosPago.Transferencia, 10000, null) };
        var preview = await e.Cuenta.PrevisualizarAbonoAsync(cliente, 10000, pagos, e.Sesion);
        await e.RegistrarAbono.EjecutarAsync(new(cliente, 10000, pagos, preview.Aplicaciones, preview.SaldoAnteriorMinor, "op-abono-cobro", e.Sesion));

        var (oct, octH) = Rango(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var medios = await e.Reportes.DesgloseMediosAsync(oct, octH, e.Sesion);
        Assert.Equal(10000, medios.First(m => m.Medio == MediosPago.Transferencia).AbonosMinor);
        Assert.Equal(0, medios.First(m => m.Medio == MediosPago.Transferencia).VentasMinor);
        var resumen = await e.Reportes.ResumenVentasAsync(oct, octH, e.Sesion);
        Assert.Equal(0, resumen.BrutasMinor);
        Assert.Equal(10000, resumen.CobradoMinor);
    }

    [Fact]
    public async Task Cobro_f06_y_reintegro_en_medios()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-cobro-f06a", e.Sesion));
        var ficha = (await e.Ventas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        // Diferencia positiva 2000 por débito.
        await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok.VentaId, [new(ficha.Lineas[0].Id, 1, true, null)], [new(v2, 1, 12000)],
            [new(MediosPago.Debito, 2000, null)], MediosPago.Efectivo, "Sube", "op-cambio-cobro", e.Sesion), default);

        var ok2 = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v2, 1, 12000)], [new(MediosPago.Efectivo, 12000, 12000)], "op-cobro-f06b", e.Sesion));
        var ficha2 = (await e.Ventas.ObtenerFichaAsync(ok2.VentaId, e.Sesion))!;
        // Diferencia negativa 2000 con reintegro en efectivo.
        await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok2.VentaId, [new(ficha2.Lineas[0].Id, 1, true, null)], [new(v1, 1, 10000)],
            [], MediosPago.Efectivo, "Baja", "op-cambio-reint", e.Sesion), default);

        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var medios = await e.Reportes.DesgloseMediosAsync(d, h, e.Sesion);
        Assert.Equal(2000, medios.First(m => m.Medio == MediosPago.Debito).CambiosCobrosMinor);
        Assert.Equal(2000, medios.First(m => m.Medio == MediosPago.Efectivo).ReintegrosMinor);
        var resumen = await e.Reportes.ResumenVentasAsync(d, h, e.Sesion);
        Assert.Equal(24000, resumen.CobradoMinor);
        Assert.Equal(2000, resumen.ReintegrosMinor);
        Assert.Equal(22000, resumen.FlujoNetoMinor);
    }

    [Fact]
    public async Task Credito_a_deuda_no_toca_caja_y_anulacion_reintegra_lo_cobrado()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var cliente = await e.ClienteHabilitadoAsync(100000);
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 4000, 4000)], "op-cred-deuda", e.Sesion, cliente, null, true));
        var ficha = (await e.Ventas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        // Devolución con crédito aplicado a la deuda de 6000: sin caja.
        var resultado = await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok.VentaId, [new(ficha.Lineas[0].Id, 1, true, null)], [], [],
            MediosPago.Efectivo, "Devuelve", "op-cambio-cred", e.Sesion,
            null, null, DecisionCuentaCorrienteCambio.AplicarADeuda, true, 6000), default);
        Assert.IsType<ResultadoCambio.Registrado>(resultado);

        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var resumen = await e.Reportes.ResumenVentasAsync(d, h, e.Sesion);
        Assert.Equal(10000, resumen.CreditoDevolucionesMinor);
        // Remanente 4000 tras aplicar 6000 a la deuda: reintegro real.
        Assert.Equal(4000, resumen.ReintegrosMinor);
        Assert.Equal(0, (await e.Cuenta.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);

        // Anulación con reintegro de lo cobrado (otra venta sin cambios).
        var ok2 = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v2, 1, 12000)], [new(MediosPago.Efectivo, 12000, 12000)], "op-anul-reint-v", e.Sesion));
        await e.AnularVenta.EjecutarAsync(ok2.VentaId, "Error", "op-anul-reint", e.Sesion);
        var trasAnular = await e.Reportes.ResumenVentasAsync(d, h, e.Sesion);
        Assert.Equal(4000 + 12000, trasAnular.ReintegrosMinor);
        var medios = await e.Reportes.DesgloseMediosAsync(d, h, e.Sesion);
        Assert.Equal(4000 + 12000, medios.First(m => m.Medio == MediosPago.Efectivo).ReintegrosMinor);
    }

    [Fact]
    public async Task Correccion_de_medio_reporta_vigente_sin_duplicar()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Debito, 10000, null)], "op-corr-medio", e.Sesion));
        var pagoId = (await e.PagosDeVentaAsync(ok.VentaId)).First().Id;
        await e.CorregirMedio.EjecutarAsync(pagoId, MediosPago.Efectivo, "Era efectivo", "op-corr-001", e.Sesion);

        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var medios = await e.Reportes.DesgloseMediosAsync(d, h, e.Sesion);
        // Medio vigente: efectivo 10000 con indicador; débito 0. Sin doble conteo.
        var efectivo = medios.First(m => m.Medio == MediosPago.Efectivo);
        Assert.Equal(10000, efectivo.VentasMinor);
        Assert.True(efectivo.ConCorreccion);
        Assert.Equal(0, medios.First(m => m.Medio == MediosPago.Debito).VentasMinor);
        Assert.Equal(10000, (await e.Reportes.ResumenVentasAsync(d, h, e.Sesion)).CobradoMinor);
    }
}
