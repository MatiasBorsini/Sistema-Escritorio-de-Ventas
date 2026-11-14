using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Reportes;
using LocalInfantil.Application.Features.Ventas;

namespace LocalInfantil.Tests.Reportes;

/// <summary>
/// F09-T01: brutas/netas/ajustes por fecha del evento. Los períodos
/// históricos nunca se reescriben: cada hecho entra donde ocurrió.
/// </summary>
public sealed class ReportesVentasTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static (string Desde, string Hasta) Rango(DateOnly desde, DateOnly hasta)
    {
        var r = RangoFechas.PorDias(desde, hasta, Utc);
        return (r.DesdeUtc, r.HastaUtc);
    }

    [Fact]
    public async Task Periodo_vacio_da_ceros()
    {
        using var e = await EntornoReportes.CrearAsync();
        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var resumen = await e.Reportes.ResumenVentasAsync(d, h, e.Sesion);
        Assert.Equal(new ResumenVentasPeriodo(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), resumen);
        Assert.Empty(await e.Reportes.VentasPorDiaAsync(d, h, "+0 hours", e.Sesion));
        Assert.Empty(await e.Reportes.DetalleVentasAsync(d, h, 20, 0, e.Sesion));
    }

    [Fact]
    public async Task Venta_normal_bruta_igual_neta()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await e.AbrirCajaAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 2, 10000)], [new(MediosPago.Efectivo, 20000, 20000)], "op-rv-001", e.Sesion));
        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var resumen = await e.Reportes.ResumenVentasAsync(d, h, e.Sesion);
        Assert.Equal(20000, resumen.BrutasMinor);
        Assert.Equal(1, resumen.CantidadVentas);
        Assert.Equal(0, resumen.AnulacionesMinor);
        Assert.Equal(20000, resumen.NetasMinor);
        Assert.Equal(20000, resumen.CobradoMinor);
        Assert.Equal(0, resumen.ReintegrosMinor);
        Assert.Equal(20000, resumen.FlujoNetoMinor);

        var detalle = await e.Reportes.DetalleVentasAsync(d, h, 20, 0, e.Sesion);
        var fila = Assert.Single(detalle);
        Assert.Equal("venta", fila.Tipo);
        Assert.Equal(ok.Numero, fila.Referencia);
        Assert.Equal(20000, fila.BrutaMinor);
        Assert.Equal(0, fila.AjustesMinor);
        Assert.Equal(20000, fila.NetoMinor);
        Assert.Equal(20000, fila.CobradoMinor);
        Assert.Equal("confirmada", fila.Estado);
    }

    [Fact]
    public async Task Venta_anulada_mismo_dia_resta_en_su_fecha()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await e.AbrirCajaAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-rv-anul", e.Sesion));
        await e.AnularVenta.EjecutarAsync(ok.VentaId, "Arrepentimiento", "op-rv-anul-rev", e.Sesion);
        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var resumen = await e.Reportes.ResumenVentasAsync(d, h, e.Sesion);
        Assert.Equal(10000, resumen.BrutasMinor);
        Assert.Equal(10000, resumen.AnulacionesMinor);
        Assert.Equal(1, resumen.CantidadAnulaciones);
        Assert.Equal(0, resumen.NetasMinor);

        var detalle = await e.Reportes.DetalleVentasAsync(d, h, 20, 0, e.Sesion);
        Assert.Equal(2, detalle.Count);
        Assert.Contains(detalle, f => f.Tipo == "venta" && f.NetoMinor == 10000);
        Assert.Contains(detalle, f => f.Tipo == "anulacion" && f.AjustesMinor == -10000 && f.NetoMinor == -10000);
    }

    [Fact]
    public async Task Anulacion_posterior_no_reescribe_periodo_anterior()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await e.AbrirCajaAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 2, 10000)], [new(MediosPago.Efectivo, 20000, 20000)], "op-rv-sep", e.Sesion));

        e.FijarHora(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        await e.AnularVenta.EjecutarAsync(ok.VentaId, "Devolución total", "op-rv-oct", e.Sesion);

        var (sep, sepH) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var sept = await e.Reportes.ResumenVentasAsync(sep, sepH, e.Sesion);
        Assert.Equal(20000, sept.BrutasMinor);
        Assert.Equal(0, sept.AnulacionesMinor);
        Assert.Equal(20000, sept.NetasMinor);
        Assert.Equal(20000, sept.CobradoMinor);

        var (oct, octH) = Rango(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var octu = await e.Reportes.ResumenVentasAsync(oct, octH, e.Sesion);
        Assert.Equal(0, octu.BrutasMinor);
        Assert.Equal(20000, octu.AnulacionesMinor);
        Assert.Equal(-20000, octu.NetasMinor);
        var detalle = await e.Reportes.DetalleVentasAsync(oct, octH, 20, 0, e.Sesion);
        var fila = Assert.Single(detalle);
        Assert.Equal("anulacion", fila.Tipo);
        Assert.Equal(ok.Numero, fila.Referencia);
        Assert.Equal(-20000, fila.NetoMinor);
    }

    [Fact]
    public async Task Devolucion_mismo_dia_resta_credito()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await e.AbrirCajaAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-rv-dev", e.Sesion));
        var ficha = (await e.Ventas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        var resultado = await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok.VentaId,
            [new(ficha.Lineas[0].Id, 1, true, null)],
            [],
            [],
            MediosPago.Efectivo,
            "No le gustó",
            "op-cambio-dev",
            e.Sesion), default);
        Assert.IsType<ResultadoCambio.Registrado>(resultado);
        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var resumen = await e.Reportes.ResumenVentasAsync(d, h, e.Sesion);
        Assert.Equal(10000, resumen.BrutasMinor);
        Assert.Equal(10000, resumen.CreditoDevolucionesMinor);
        Assert.Equal(0, resumen.SalidasCambioMinor);
        Assert.Equal(0, resumen.NetasMinor);
    }

    [Fact]
    public async Task Devolucion_periodo_posterior_no_reescribe_anterior()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-rv-dev2", e.Sesion));

        e.FijarHora(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        var ficha = (await e.Ventas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok.VentaId, [new(ficha.Lineas[0].Id, 1, true, null)], [], [],
            MediosPago.Efectivo, "Cambio de idea", "op-cambio-oct", e.Sesion), default);

        var (sep, sepH) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Equal(10000, (await e.Reportes.ResumenVentasAsync(sep, sepH, e.Sesion)).NetasMinor);

        var (oct, octH) = Rango(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var resumen = await e.Reportes.ResumenVentasAsync(oct, octH, e.Sesion);
        Assert.Equal(0, resumen.BrutasMinor);
        Assert.Equal(10000, resumen.CreditoDevolucionesMinor);
        Assert.Equal(-10000, resumen.NetasMinor);
        _ = v2;
    }

    [Fact]
    public async Task Cambio_a_por_b_no_infla_como_venta_nueva()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-rv-cambio", e.Sesion));

        e.FijarHora(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        var ficha = (await e.Ventas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        var resultado = await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok.VentaId,
            [new(ficha.Lineas[0].Id, 1, true, null)],
            [new(v2, 1, 12000)],
            [new(MediosPago.Efectivo, 2000, 2000)],
            MediosPago.Efectivo, "Otro talle", "op-cambio-ab", e.Sesion), default);
        Assert.IsType<ResultadoCambio.Registrado>(resultado);

        // Septiembre intacto.
        var (sep, sepH) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Equal(10000, (await e.Reportes.ResumenVentasAsync(sep, sepH, e.Sesion)).NetasMinor);

        // Octubre: entrada -20000? No: entrada 10000, salida 12000, impacto +2000.
        var (oct, octH) = Rango(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var resumen = await e.Reportes.ResumenVentasAsync(oct, octH, e.Sesion);
        Assert.Equal(0, resumen.BrutasMinor);
        Assert.Equal(10000, resumen.CreditoDevolucionesMinor);
        Assert.Equal(12000, resumen.SalidasCambioMinor);
        Assert.Equal(2000, resumen.NetasMinor);
        Assert.Equal(2000, resumen.CobradoMinor);
    }

    [Fact]
    public async Task Detalle_clasifica_devolucion_y_cambio_por_sus_salidas_persistidas()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        var venta = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 2, 10000)], [new(MediosPago.Efectivo, 20000, 20000)], "op-rv-tipo-venta", e.Sesion));
        var ficha = (await e.Ventas.ObtenerFichaAsync(venta.VentaId, e.Sesion))!;

        var devolucion = Assert.IsType<ResultadoCambio.Registrado>(await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            venta.VentaId, [new(ficha.Lineas[0].Id, 1, true, null)], [], [],
            MediosPago.Efectivo, "Devolución sin artículos nuevos", "op-rv-tipo-dev", e.Sesion), default));
        var cambio = Assert.IsType<ResultadoCambio.Registrado>(await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            venta.VentaId, [new(ficha.Lineas[0].Id, 1, true, null)], [new(v2, 1, 12000)],
            [new(MediosPago.Efectivo, 2000, 2000)], MediosPago.Efectivo, "Cambio de talle", "op-rv-tipo-cambio", e.Sesion), default));

        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var detalle = await e.Reportes.DetalleVentasAsync(d, h, 20, 0, e.Sesion);

        Assert.Contains(detalle, f => f.Tipo == "devolucion" && f.Referencia == devolucion.Numero && f.AjustesMinor == -10000);
        Assert.Contains(detalle, f => f.Tipo == "cambio" && f.Referencia == cambio.Numero);
        Assert.DoesNotContain(detalle, f => f.Tipo == "devolucion" && f.Referencia == cambio.Numero);
    }

    [Fact]
    public async Task Ajustes_positivos_por_salidas_superiores()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 2, 10000)], [new(MediosPago.Efectivo, 20000, 20000)], "op-rv-ajpos", e.Sesion));

        // Octubre: devuelve $20.000 y lleva $25.000. Ajustes = +$5.000.
        e.FijarHora(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        await e.FijarPrecio.EjecutarAsync(v2, 25000, e.Sesion);
        var ficha = (await e.Ventas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        var resultado = await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok.VentaId,
            [new(ficha.Lineas[0].Id, 2, true, null)],
            [new(v2, 1, 25000)],
            [new(MediosPago.Efectivo, 5000, 5000)],
            MediosPago.Efectivo, "Sube de gama", "op-cambio-ajpos", e.Sesion), default);
        Assert.IsType<ResultadoCambio.Registrado>(resultado);

        var (oct, octH) = Rango(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var resumen = await e.Reportes.ResumenVentasAsync(oct, octH, e.Sesion);
        Assert.Equal(0, resumen.BrutasMinor);
        Assert.Equal(20000, resumen.CreditoDevolucionesMinor);
        Assert.Equal(25000, resumen.SalidasCambioMinor);
        Assert.Equal(5000, resumen.NetasMinor);
        Assert.Equal(resumen.NetasMinor - resumen.BrutasMinor, 5000);
    }

    [Fact]
    public void Ajustes_con_signo_se_renderizan_con_signo()
    {
        var conversor = new LocalInfantil.Wpf.Infra.MinorConSignoToArsConverter();
        var cultura = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal("+ARS 5.000,00", conversor.Convert(500000L, typeof(string), null, cultura));
        Assert.Equal("-ARS 20.000,00", conversor.Convert(-2000000L, typeof(string), null, cultura));
        Assert.Equal("ARS 0,00", conversor.Convert(0L, typeof(string), null, cultura));
    }

    [Fact]
    public async Task Diferencia_positiva_cobra_y_negativa_reintegra()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        // Venta 10000 cobrada; cambio a variante de 12000 con cobro 2000.
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-rv-difpos", e.Sesion));
        var ficha = (await e.Ventas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok.VentaId, [new(ficha.Lineas[0].Id, 1, true, null)], [new(v2, 1, 12000)],
            [new(MediosPago.Debito, 2000, null)], MediosPago.Efectivo, "Sube", "op-cambio-difpos", e.Sesion), default);

        // Venta 12000 cobrada; cambio a variante de 10000 con reintegro 2000.
        var ok2 = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v2, 1, 12000)], [new(MediosPago.Efectivo, 12000, 12000)], "op-rv-difneg", e.Sesion));
        var ficha2 = (await e.Ventas.ObtenerFichaAsync(ok2.VentaId, e.Sesion))!;
        await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok2.VentaId, [new(ficha2.Lineas[0].Id, 1, true, null)], [new(v1, 1, 10000)],
            [], MediosPago.Efectivo, "Baja", "op-cambio-difneg", e.Sesion), default);

        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var resumen = await e.Reportes.ResumenVentasAsync(d, h, e.Sesion);
        Assert.Equal(22000, resumen.BrutasMinor);
        Assert.Equal(22000, resumen.CreditoDevolucionesMinor);
        Assert.Equal(22000, resumen.SalidasCambioMinor);
        Assert.Equal(22000, resumen.NetasMinor);
        Assert.Equal(24000, resumen.CobradoMinor);
        Assert.Equal(2000, resumen.ReintegrosMinor);
        Assert.Equal(22000, resumen.FlujoNetoMinor);
    }

    [Fact]
    public async Task Fiado_suma_venta_no_cobro_y_abono_suma_cobro_sin_venta()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var cliente = await e.ClienteHabilitadoAsync(100000);
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 4000, 4000)], "op-rv-fiado", e.Sesion, cliente, null, true));

        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var antes = await e.Reportes.ResumenVentasAsync(d, h, e.Sesion);
        Assert.Equal(10000, antes.BrutasMinor);
        Assert.Equal(10000, antes.NetasMinor);
        Assert.Equal(4000, antes.CobradoMinor);

        e.FijarHora(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        var pagos = new[] { new NuevoPago(MediosPago.Efectivo, 6000, 6000) };
        var preview = await e.Cuenta.PrevisualizarAbonoAsync(cliente, 6000, pagos, e.Sesion);
        await e.RegistrarAbono.EjecutarAsync(new(cliente, 6000, pagos, preview.Aplicaciones, preview.SaldoAnteriorMinor, "op-abono-oct", e.Sesion));

        var (oct, octH) = Rango(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var despues = await e.Reportes.ResumenVentasAsync(oct, octH, e.Sesion);
        Assert.Equal(0, despues.BrutasMinor);
        Assert.Equal(6000, despues.CobradoMinor);
        _ = ok;
    }

    [Fact]
    public async Task Rango_incluye_inicio_y_excluye_fin()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-rv-borde-ini", e.Sesion);

        e.FijarHora(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-rv-borde-fin", e.Sesion);

        var (sep, sepH) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Equal(1, (await e.Reportes.ResumenVentasAsync(sep, sepH, e.Sesion)).CantidadVentas);
        var (oct, octH) = Rango(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal(1, (await e.Reportes.ResumenVentasAsync(oct, octH, e.Sesion)).CantidadVentas);
    }

    [Fact]
    public async Task Ventas_por_dia_agrupa_y_detalle_pagina()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await e.AbrirCajaAsync();
        await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-rv-dia1", e.Sesion);
        e.FijarHora(new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
        await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-rv-dia2", e.Sesion);

        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var diarias = await e.Reportes.VentasPorDiaAsync(d, h, "+0 hours", e.Sesion);
        Assert.Equal(2, diarias.Count);
        Assert.Equal("2026-09-10", diarias[0].Fecha);
        Assert.Equal(10000, diarias[0].NetasMinor);
        Assert.Equal("2026-09-12", diarias[1].Fecha);

        var detalle = await e.Reportes.DetalleVentasAsync(d, h, 20, 0, e.Sesion);
        Assert.Equal(2, detalle.Count);
        Assert.All(detalle, f => Assert.Equal("venta", f.Tipo));
    }
}
